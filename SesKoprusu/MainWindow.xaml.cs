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
    CancellationTokenSource? loginAttempt;
    Uri? verificationUrl;

    void UpdateControls()
    {
        Login.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        Login.IsEnabled = ready && !busy && !signedIn;
        Reconnect.IsEnabled = ready && !busy;
        Test.IsEnabled = ready && cliFound && (!busy || verificationUrl != null);
        Preview.IsEnabled = ready && !busy && connected && HasSpeechKey;
        Incoming.IsEnabled = Outgoing.IsEnabled = !routing.Pending && !busy;
        Route.IsEnabled = ready && !busy && (routing.Pending || connected);
        SpeechState.Text = HasSpeechKey ? "Ses anahtarı tanımlı." : "Çeviri ve ses önizlemesi için API anahtarı girin.";
    }

    async Task<bool> CheckSession()
    {
        connected = false;
        try
        {
            cliFound = CodexSession.FindCli() != null;
            if (!cliFound) { signedIn = false; SessionState.Text = "İlk bağlantıda Codex otomatik hazırlanır"; return false; }
            using var accountTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            accountTimeout.CancelAfter(TimeSpan.FromSeconds(15));
            signedIn = await CodexSession.SignedIn(accountTimeout.Token);
            if (!signedIn) { SessionState.Text = "Oturum kapalı · Codex Ayarları’ndan giriş yapın"; return false; }
            SessionState.Text = "Codex bağlantısı test ediliyor…";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(40));
            await CodexSession.Ask("Yalnızca OK yaz.", timeout.Token);
            connected = true;
            SessionState.Text = "●  Codex bağlı";
        }
        catch (Exception ex) { SessionState.Text = signedIn ? "Oturum kayıtlı · Bağlantıyı yeniden deneyin" : "○  Hesap durumu doğrulanamadı"; Status.Text = ex is OperationCanceledException ? "Bağlantı testi zaman aşımına uğradı." : ex.Message; }
        finally { UpdateControls(); }
        return connected;
    }

    async void TestClick(object sender, RoutedEventArgs e)
    {
        if (loginAttempt != null)
        {
            Status.Text = "Tarayıcıda cihaz kodu onayı bekleniyor.";
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(loginAttempt.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                if (await CodexSession.SignedIn(timeout.Token)) Status.Text = "Hesap oturumu açık.";
            }
            catch (Exception) { if (loginAttempt != null) Status.Text = "Hesap durumu alınamadı."; }
            return;
        }
        busy = true;
        UpdateControls();
        try { await CheckSession(); }
        finally { busy = false; UpdateControls(); }
    }

    void KeyChanged(object sender, RoutedEventArgs e) { if (ready) UpdateControls(); }

    void VerificationClick(object sender, RoutedEventArgs e)
    {
        if (verificationUrl != null) OpenPage(verificationUrl.AbsoluteUri);
    }

    void CancelClick(object sender, RoutedEventArgs e) => loginAttempt?.Cancel();

    async void ReconnectClick(object sender, RoutedEventArgs e) => await StartAccountFlow(false);
    async void LoginClick(object sender, RoutedEventArgs e) => await StartAccountFlow(true);

    async Task StartAccountFlow(bool login)
    {
        if (busy) return;
        busy = true;
        Status.Text = "";
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        attempt.CancelAfter(TimeSpan.FromMinutes(20));
        loginAttempt = attempt;
        CancelLogin.Visibility = Visibility.Visible;
        UpdateControls();
        try
        {
            if (CodexSession.FindCli() == null) SessionState.Text = "Codex indiriliyor…";
            await CliInstaller.Ensure(http, attempt.Token);
            cliFound = true;
            SessionState.Text = "App Server’a bağlanılıyor…";
            using var server = await AccountServer.Connect(attempt.Token);
            if (login && !await server.HasChatGptAccount(attempt.Token))
                await server.Login((code, url) =>
                {
                    UserCode.Text = code;
                    verificationUrl = url;
                    Challenge.Visibility = Visibility.Visible;
                    SessionState.Text = "Tarayıcıda giriş onayı bekleniyor…";
                    UpdateControls();
                    OpenPage(url.AbsoluteUri);
                }, attempt.Token);
        }
        catch (OperationCanceledException) { Status.Text = "İşlem iptal edildi veya süresi doldu."; }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally
        {
            verificationUrl = null;
            UserCode.Clear();
            Challenge.Visibility = CancelLogin.Visibility = Visibility.Collapsed;
            loginAttempt = null;
            if (!lifetime.IsCancellationRequested) await CheckSession();
            busy = false;
            UpdateControls();
        }
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
