using System.ComponentModel;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Diagnostics;
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
    bool signedIn;
    bool busy;
    bool HasSpeechKey => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

    void UpdateControls()
    {
        Login.IsEnabled = ready && !busy && !signedIn;
        Ask.IsEnabled = Prompt.IsEnabled = ready && signedIn && !busy;
        Preview.IsEnabled = Ask.IsEnabled && HasSpeechKey;
        Route.IsEnabled = ready && !busy && (routing.Pending || signedIn);
        Ask.Content = HasSpeechKey ? "Sor ve seslendir" : "Sor";
        SpeechState.Text = HasSpeechKey ? "OpenAI ses API anahtarı tanımlı." : "Sesli okuma için OPENAI_API_KEY tanımlayın. Codex yanıtları metin olarak gösterilir.";
    }

    async Task<bool> CheckSession()
    {
        signedIn = false;
        try { signedIn = await CodexSession.SignedIn(lifetime.Token); }
        catch (Exception ex) { Status.Text = ex.Message; }
        SessionState.Text = signedIn ? "ChatGPT oturumu açık" : "Oturum açmanız gerekiyor";
        UpdateControls();
        return signedIn;
    }

    async void LoginClick(object sender, RoutedEventArgs e)
    {
        busy = true;
        UpdateControls();
        SessionState.Text = "Açılan Codex penceresindeki adımları tamamlayın…";
        try { await CodexSession.Login(lifetime.Token); }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { await CheckSession(); busy = false; UpdateControls(); }
    }

    void SecurityClick(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://chatgpt.com/#settings/Security") { UseShellExecute = true }); }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

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
        UpdateControls();
        Loaded += async (_, _) => await CheckSession();
    }

    void SettingsClick(object sender, RoutedEventArgs e) =>
        Settings.Visibility = Settings.IsVisible ? Visibility.Collapsed : Visibility.Visible;

    void VoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        try { Local.Save("voice", (string)Voice.SelectedItem); }
        catch (Exception ex) { Status.Text = "Ses tercihi kaydedilemedi. " + ex.Message; }
    }

    async void RouteClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!routing.Pending)
            {
                busy = true;
                UpdateControls();
                if (!await CheckSession()) return;
                routing.Enable();
            }
            else routing.Restore();
            Status.Text = routing.Pending ? "Sanal yönlendirme açık." : "Önceki aygıtlar geri yüklendi.";
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { busy = false; Route.IsChecked = routing.Pending; UpdateControls(); }
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
        busy = true;
        UpdateControls();
        Status.Text = "İşleniyor…";
        try { if (!await CheckSession()) { Status.Text = "Devam etmek için Codex Ayarları üzerinden oturum açın."; return; } await action(); Status.Text = "Tamamlandı."; }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { busy = false; UpdateControls(); }
    }

    async void PreviewClick(object sender, RoutedEventArgs e) =>
        await RunAudio(() => Speak("Merhaba, SesKöprüsü ile Türkçe ses örneğini dinliyorsunuz."));

    async void AskClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Prompt.Text)) return;
        await RunAudio(async () =>
        {
            Answer.Text = await CodexSession.Ask(Prompt.Text, lifetime.Token);
            if (HasSpeechKey) await Speak(Answer.Text);
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
