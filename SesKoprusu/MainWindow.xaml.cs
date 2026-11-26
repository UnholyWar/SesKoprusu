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
    static readonly string[] Languages = ["Pass through", "Turkish", "English", "German", "French", "Spanish", "Arabic", "Russian", "Japanese", Routing.Original];
    string SpeechKey => Key.Password.Length > 0 ? Key.Password.Trim() : Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
    bool HasSpeechKey => !string.IsNullOrWhiteSpace(SpeechKey);
    bool connected;
    bool cliFound;
    CancellationTokenSource? loginAttempt;
    Uri? verificationUrl;

    void UpdateControls()
    {
        Repair.IsEnabled = ready && !busy && !routing.Pending;
        Login.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        Login.IsEnabled = ready && !busy && !signedIn;
        Reconnect.IsEnabled = ready && !busy;
        Test.IsEnabled = ready && cliFound && (!busy || verificationUrl != null);
        Preview.IsEnabled = ready && !busy && connected && HasSpeechKey;
        Voice.IsEnabled = SendVoice.IsEnabled = Incoming.IsEnabled = Outgoing.IsEnabled = !routing.Pending && !busy;
        Route.IsEnabled = ready && !busy && (routing.Pending || connected);
        SpeechState.Text = HasSpeechKey ? "Audio API key provided." : "Enter your API key for translation and voice previews.";
    }

    async Task<bool> CheckSession()
    {
        connected = false;
        try
        {
            cliFound = CodexSession.FindCli() != null;
            if (!cliFound) { signedIn = false; SessionState.Text = "Codex will be installed at first sign-in"; return false; }
            using var accountTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            accountTimeout.CancelAfter(TimeSpan.FromSeconds(15));
            signedIn = await CodexSession.SignedIn(accountTimeout.Token);
            if (!signedIn) { SessionState.Text = "Signed out · Open Codex Settings to sign in"; return false; }
            SessionState.Text = "Testing the Codex connection…";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(40));
            await CodexSession.Ask("Reply with OK.", timeout.Token);
            connected = true;
            SessionState.Text = "●  Codex connected";
        }
        catch (Exception ex) { SessionState.Text = signedIn ? "Session saved · Retry connection" : "○  Account status unavailable"; Status.Text = ex is OperationCanceledException ? "The connection test timed out." : ex.Message; }
        finally { UpdateControls(); }
        return connected;
    }

    async void TestClick(object sender, RoutedEventArgs e)
    {
        if (loginAttempt != null)
        {
            Status.Text = "Waiting for device approval in your browser.";
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(loginAttempt.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                if (await CodexSession.SignedIn(timeout.Token)) Status.Text = "Signed in.";
            }
            catch (Exception) { if (loginAttempt != null) Status.Text = "Could not read account status."; }
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
            if (CodexSession.FindCli() == null) SessionState.Text = "Downloading Codex…";
            await CliInstaller.Ensure(http, p => Progress("Downloading Codex…", p), attempt.Token);
            Transfer.Visibility = Visibility.Collapsed;
            cliFound = true;
            Status.Text = "";
            SessionState.Text = "Connecting to App Server…";
            using var server = await AccountServer.Connect(attempt.Token);
            if (login && !await server.HasChatGptAccount(attempt.Token))
                await server.Login((code, url) =>
                {
                    UserCode.Text = code;
                    verificationUrl = url;
                    Challenge.Visibility = Visibility.Visible;
                    SessionState.Text = "Waiting for browser approval…";
                    UpdateControls();
                    OpenPage(url.AbsoluteUri);
                }, attempt.Token);
        }
        catch (OperationCanceledException) { Status.Text = "The request was canceled or expired."; }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally
        {
            Transfer.Visibility = Visibility.Collapsed;
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
        if (!ownsInstance) { MessageBox.Show("SesKöprüsü is already running."); Loaded += (_, _) => Close(); return; }
        Incoming.ItemsSource = Outgoing.ItemsSource = Languages;
        Incoming.SelectedIndex = Outgoing.SelectedIndex = 0;
        try
        {
            if (Local.Has("languages") && Local.Read<string[]>("languages") is [var incoming, var outgoing])
            {
                string[] legacy = ["Çevirme", "Türkçe", "İngilizce", "Almanca", "Fransızca", "İspanyolca", "Arapça", "Rusça", "Japonca"];
                if (Array.IndexOf(legacy, incoming) is var i && i >= 0) incoming = Languages[i];
                if (Array.IndexOf(legacy, outgoing) is var j && j >= 0) outgoing = Languages[j];
                if (Languages.Contains(incoming)) Incoming.SelectedItem = incoming;
                if (Languages.Contains(outgoing)) Outgoing.SelectedItem = outgoing;
            }
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        Voice.ItemsSource = SendVoice.ItemsSource = Voices;
        Voice.SelectedIndex = SendVoice.SelectedIndex = 0;
        try
        {
            if (Local.Has("voice") && Local.Read<string>("voice") is string voice && Voices.Contains(voice))
                Voice.SelectedItem = voice;
            SendVoice.SelectedItem = Voice.SelectedItem;
            if (Local.Has("outgoingVoice") && Local.Read<string>("outgoingVoice") is string send && Voices.Contains(send))
                SendVoice.SelectedItem = send;
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
        if (open) TryRefreshDrivers();
        Nav.Content = open ? "Back" : "Codex Settings";
    }

    void Progress(string label, double? value)
    {
        Transfer.Visibility = Visibility.Visible;
        Transfer.IsIndeterminate = value == null;
        Transfer.Value = value ?? 0;
        Status.Text = value.HasValue ? $"{label} {value:0}%" : label;
    }

    string IncomingMode => (string)Incoming.SelectedItem;
    string OutgoingMode => (string)Outgoing.SelectedItem;

    void RefreshDrivers()
    {
        var missing = routing.Missing(IncomingMode, OutgoingMode);
        DriverState.Text = missing == null ? "Required audio devices are ready." :
            $"{missing} is missing or disabled. Select Install or enable devices.";
    }

    void LicenseClick(object sender, RoutedEventArgs e) => OpenPage("https://vb-audio.com/Cable/");

    async void RepairClick(object sender, RoutedEventArgs e)
    {
        busy = true;
        UpdateControls();
        try
        {
            var missing = routing.Missing(IncomingMode, OutgoingMode, activate: true);
            if (missing == null) { Status.Text = "Audio devices are ready."; return; }
            while (missing != null)
            {
                string? zip = null;
                if (missing == "CABLE-A")
                {
                    var picker = new Microsoft.Win32.OpenFileDialog { Filter = "VB-Audio A+B package|*.zip", Title = "Select your licensed VB-CABLE A+B package" };
                    if (picker.ShowDialog(this) != true) { Status.Text = "Use Get Cable A+B to obtain the vendor package."; return; }
                    zip = picker.FileName;
                }
                await DriverSetup.Install(http, zip, Progress, lifetime.Token);
                var remaining = routing.Missing(IncomingMode, OutgoingMode, activate: true);
                if (remaining == missing) break; // The installer may require a reboot.
                missing = remaining;
            }
            Status.Text = routing.Missing(IncomingMode, OutgoingMode, true) == null ? "Devices ready. Return to the bridge and switch it on." :
                "Finish the driver installation and restart Windows if requested. Then check the devices again.";
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { Transfer.Visibility = Visibility.Collapsed; busy = false; UpdateControls(); TryRefreshDrivers(); }
    }

    void TryRefreshDrivers()
    {
        try { RefreshDrivers(); } catch (Exception ex) { DriverState.Text = ex.Message; }
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
        try { var box = (ComboBox)sender; Local.Save(box == Voice ? "voice" : "outgoingVoice", (string)box.SelectedItem); }
        catch (Exception ex) { Status.Text = "Could not save the voice preference. " + ex.Message; }
    }

    async void RouteClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!routing.Pending)
            {
                busy = true;
                UpdateControls();
                if (IncomingMode == Routing.Original && !Routing.Translates(OutgoingMode))
                {
                    Status.Text = "Original audio is already active in both directions.";
                    return;
                }
                if (routing.Missing(IncomingMode, OutgoingMode) is string missing)
                {
                    if (!Settings.IsVisible) SettingsClick(this, e);
                    DeviceTab.IsSelected = true;
                    Status.Text = $"{missing} needs setup. Select Install or enable devices to continue.";
                    return;
                }
                if (!await CheckSession()) return;
                string incoming = (string)Incoming.SelectedItem, outgoing = (string)Outgoing.SelectedItem;
                if ((Routing.Translates(incoming) || Routing.Translates(outgoing)) && !HasSpeechKey)
                    throw new InvalidOperationException("Enter your audio API key in Codex Settings to translate.");
                routing.Enable(incoming, outgoing, (string)Voice.SelectedItem, (string)SendVoice.SelectedItem, SpeechKey, http, error => Dispatcher.InvokeAsync(() =>
                {
                    try { routing.Restore(); Status.Text = error; }
                    catch (Exception ex) { Status.Text = ex.Message; }
                    Route.IsChecked = routing.Pending;
                    UpdateControls();
                }));
            }
            else routing.Restore();
            Status.Text = routing.Pending ? "Audio bridge is on." : "Your previous devices have been restored.";
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { busy = false; Route.IsChecked = routing.Pending; UpdateControls(); }
    }

    async Task<HttpResponseMessage> Post(string path, object body)
    {
        var key = SpeechKey;
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Enter your OpenAI API key in Codex Settings.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = JsonContent.Create(body);
        var response = await http.SendAsync(request, lifetime.Token);
        if (response.IsSuccessStatusCode) return response;
        var code = response.StatusCode;
        response.Dispose();
        throw new HttpRequestException($"OpenAI request failed ({code}).");
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
        Status.Text = "Working…";
        try { if (!await CheckSession()) { Status.Text = "Sign in through Codex Settings to continue."; return; } await action(); Status.Text = "Done."; }
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
