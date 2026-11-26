using System.ComponentModel;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace SesKoprusu;

public partial class MainWindow : Window
{
    static readonly string[] Voices = ["marin", "cedar", "alloy", "ash", "ballad", "coral", "echo", "fable", "nova", "onyx", "sage", "shimmer", "verse"];
    readonly Routing routing = new();
    readonly Mutex instance = new(false, @"Local\SesKoprusu.Audio");
    readonly HttpClient http = new();
    readonly CancellationTokenSource lifetime = new();
    bool ownsInstance;
    bool ready;

    public MainWindow()
    {
        InitializeComponent();
        try { ownsInstance = instance.WaitOne(0); }
        catch (AbandonedMutexException) { ownsInstance = true; }
        if (!ownsInstance) { MessageBox.Show("SesKöprüsü zaten açık."); Loaded += (_, _) => Close(); return; }
        Voice.ItemsSource = Voices;
        Voice.SelectedIndex = 0;
        try
        {
            if (Local.Has("voice") && Local.Read<string>("voice") is string voice && Voices.Contains(voice))
                Voice.SelectedItem = voice;
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        try { routing.Restore(); } catch (Exception ex) { Status.Text = ex.Message; }
        Route.IsChecked = routing.Pending;
        ready = true;
    }

    void SettingsClick(object sender, RoutedEventArgs e) =>
        Settings.Visibility = Settings.IsVisible ? Visibility.Collapsed : Visibility.Visible;

    void VoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        try { Local.Save("voice", (string)Voice.SelectedItem); }
        catch (Exception ex) { Status.Text = "Ses tercihi kaydedilemedi. " + ex.Message; }
    }

    void RouteClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Route.IsChecked == true) routing.Enable(); else routing.Restore();
            Status.Text = routing.Pending ? "Sanal yönlendirme açık." : "Önceki aygıtlar geri yüklendi.";
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        Route.IsChecked = routing.Pending;
    }

    async Task<HttpResponseMessage> Post(string path, object body)
    {
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("OpenAI bağlantısı için OPENAI_API_KEY ayarlayın.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = JsonContent.Create(body);
        var response = await http.SendAsync(request, lifetime.Token);
        if (response.IsSuccessStatusCode) return response;
        var code = response.StatusCode;
        response.Dispose();
        throw new HttpRequestException($"OpenAI isteği başarısız ({code}).");
    }

    async Task Speak(string text)
    {
        var voice = (string)Voice.SelectedItem;
        for (int start = 0; start < text.Length;)
        {
            int length = Math.Min(1000, text.Length - start);
            if (char.IsHighSurrogate(text[start + length - 1])) length--;
            using var response = await Post("audio/speech", new { model = "gpt-4o-mini-tts", voice, input = text.Substring(start, length), response_format = "wav" });
            using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync(lifetime.Token));
            using var player = new SoundPlayer(stream);
            await Task.Run(player.PlaySync, lifetime.Token);
            start += length;
        }
    }

    async Task RunAudio(Func<Task> action)
    {
        Ask.IsEnabled = Settings.IsEnabled = false;
        Status.Text = "İşleniyor…";
        try { await action(); Status.Text = "Tamamlandı."; }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { Ask.IsEnabled = Settings.IsEnabled = true; }
    }

    async void PreviewClick(object sender, RoutedEventArgs e) =>
        await RunAudio(() => Speak("Merhaba, SesKöprüsü ile Türkçe ses örneğini dinliyorsunuz."));

    async void AskClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Prompt.Text)) return;
        await RunAudio(async () =>
        {
            using var response = await Post("responses", new { model = Environment.GetEnvironmentVariable("SESKOPRUSU_CODEX_MODEL") ?? "gpt-5.1-codex", input = Prompt.Text });
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(lifetime.Token));
            Answer.Text = string.Join("\n", json.RootElement.GetProperty("output").EnumerateArray()
                .Where(item => item.GetProperty("type").GetString() == "message")
                .SelectMany(item => item.GetProperty("content").EnumerateArray())
                .Where(part => part.GetProperty("type").GetString() == "output_text")
                .Select(part => part.GetProperty("text").GetString()));
            if (Answer.Text.Length == 0) throw new InvalidOperationException("Seslendirilecek yanıt alınamadı.");
            await Speak(Answer.Text);
        });
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!ownsInstance) return;
        try { routing.Restore(); }
        catch (Exception ex) { Status.Text = ex.Message; e.Cancel = true; return; }
        lifetime.Cancel();
        instance.ReleaseMutex();
        instance.Dispose();
    }
}
