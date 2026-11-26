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
    static readonly string[] Languages = ["Çevirme", "Türkçe", "İngilizce", "Almanca", "Fransızca", "İspanyolca", "Arapça", "Rusça", "Japonca"];
    string SpeechKey => Key.Password.Length > 0 ? Key.Password.Trim() : Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
    bool HasSpeechKey => !string.IsNullOrWhiteSpace(SpeechKey);
    bool connected;
    bool cliFound;

    void UpdateControls()
    {
        Login.IsEnabled = ready && !busy && cliFound;
        Test.IsEnabled = ready && !busy && cliFound;
        Preview.IsEnabled = ready && !busy && connected && HasSpeechKey;
        Incoming.IsEnabled = Outgoing.IsEnabled = !routing.Pending && !busy;
        Route.IsEnabled = ready && !busy && (routing.Pending || connected);
        SpeechState.Text = HasSpeechKey ? "Ses anahtarı tanımlı." : "Çeviri ve ses önizlemesi için API anahtarı girin.";
    }

    async Task<bool> CheckSession()
    {
        connected = false;
        signedIn = false;
        try
        {
            cliFound = CodexSession.FindCli() != null;
            if (!cliFound) { SessionState.Text = "CLI bulunamadı · Codex Ayarları’ndan seçin"; return false; }
            signedIn = await CodexSession.SignedIn(lifetime.Token);
            if (!signedIn) { SessionState.Text = "Oturum kapalı · Codex Ayarları’ndan giriş yapın"; return false; }
            SessionState.Text = "Codex bağlantısı test ediliyor…";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(40));
            await CodexSession.Ask("Yalnızca OK yaz.", timeout.Token);
            connected = true;
            SessionState.Text = "●  Codex bağlı";
        }
        catch (Exception ex) { SessionState.Text = "○  Bağlantı doğrulanamadı"; Status.Text = ex is OperationCanceledException ? "Bağlantı testi zaman aşımına uğradı." : ex.Message; }
        finally { UpdateControls(); }
        return connected;
    }

    async void TestClick(object sender, RoutedEventArgs e)
    {
        busy = true;
        UpdateControls();
        try { await CheckSession(); }
        finally { busy = false; UpdateControls(); }
    }

    void KeyChanged(object sender, RoutedEventArgs e) { if (ready) UpdateControls(); }

    void ChooseCli(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Codex CLI|codex*.exe", Title = "Resmî Codex CLI çalıştırılabilir dosyasını seçin" };
        if (picker.ShowDialog(this) != true) return;
        try { Local.Save("cli", picker.FileName); TestClick(sender, e); }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    void DownloadCli(object sender, RoutedEventArgs e) => OpenPage("https://github.com/openai/codex/releases/latest");

    async void LoginClick(object sender, RoutedEventArgs e)
    {
        busy = true;
        UpdateControls();
        SessionState.Text = "Codex penceresindeki giriş adımlarını tamamlayın…";
        try { await CodexSession.Login(lifetime.Token); }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { await CheckSession(); busy = false; UpdateControls(); }
    }

    void SecurityClick(object sender, RoutedEventArgs e) => OpenPage("https://chatgpt.com/#settings/Security");

    void OpenPage(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    public MainWindow()
    {
        InitializeComponent();
        try { ownsInstance = instance.WaitOne(0); }
        catch (AbandonedMutexException) { ownsInstance = true; }
        if (!ownsInstance) { MessageBox.Show("SesKöprüsü zaten açık."); Loaded += (_, _) => Close(); return; }
        Incoming.ItemsSource = Outgoing.ItemsSource = Languages;
        Incoming.SelectedIndex = Outgoing.SelectedIndex = 0;
        try
        {
            if (Local.Has("languages") && Local.Read<string[]>("languages") is [var incoming, var outgoing])
            {
                if (Languages.Contains(incoming)) Incoming.SelectedItem = incoming;
                if (Languages.Contains(outgoing)) Outgoing.SelectedItem = outgoing;
            }
        }
        catch (Exception ex) { Status.Text = ex.Message; }
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
        Loaded += (_, e) => TestClick(this, e);
    }

    void SettingsClick(object sender, RoutedEventArgs e)
    {
        bool open = Settings.Visibility == Visibility.Collapsed;
        Settings.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        HomePane.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        Nav.Content = open ? "Geri" : "Codex Ayarları";
    }

    void LanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        try { Local.Save("languages", new[] { (string)Incoming.SelectedItem, (string)Outgoing.SelectedItem }); }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

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
                string incoming = (string)Incoming.SelectedItem, outgoing = (string)Outgoing.SelectedItem;
                if ((incoming != "Çevirme" || outgoing != "Çevirme") && !HasSpeechKey)
                    throw new InvalidOperationException("Çeviri için Codex Ayarları’na ses API anahtarını girin.");
                routing.Enable(incoming, outgoing, (string)Voice.SelectedItem, SpeechKey, http, error => Dispatcher.InvokeAsync(() =>
                {
                    try { routing.Restore(); Status.Text = error; }
                    catch (Exception ex) { Status.Text = ex.Message; }
                    Route.IsChecked = routing.Pending;
                    UpdateControls();
                }));
            }
            else routing.Restore();
            Status.Text = routing.Pending ? "Sanal yönlendirme açık." : "Önceki aygıtlar geri yüklendi.";
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { busy = false; Route.IsChecked = routing.Pending; UpdateControls(); }
    }

    async Task<HttpResponseMessage> Post(string path, object body)
    {
        var key = SpeechKey;
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
