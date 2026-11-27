using System.ComponentModel;
using System.Net.Http;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SesKoprusu;

public partial class MainWindow : Window
{
    bool updatingVoices;
    bool catalogLoaded;
    readonly Routing routing = new();
    readonly DispatcherTimer meterTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly Mutex instance = new(false, @"Local\SesKoprusu.Audio");
    readonly HttpClient http = new();
    readonly CancellationTokenSource lifetime = new();
    bool ownsInstance;
    bool ready;
    bool signedIn;
    bool busy;
    static readonly string[] Languages = ["Pass through", "Turkish", "English", "German", "French", "Spanish", "Arabic", "Russian", "Japanese", Routing.Original];
    bool connected;
    bool cliFound;
    bool returnToGuide;
    double guideOffset;
    CancellationTokenSource? loginAttempt;
    Uri? verificationUrl;

    void UpdateControls()
    {
        MicSetup.IsEnabled = ImportPackage.IsEnabled = Repair.IsEnabled = DeviceRefresh.IsEnabled = ready && !busy && !routing.Pending;
        UpdateButton.IsEnabled = ready && !busy && !routing.Pending;
        Login.Content = signedIn ? "Sign in again" : "Sign in with a device code";
        Login.IsEnabled = ready && !busy && !routing.Pending;
        Reconnect.IsEnabled = ready && !busy;
        Test.IsEnabled = ready && cliFound && (!busy || verificationUrl != null);
        Preview.IsEnabled = PreviewSend.IsEnabled = ready && !busy && connected && !routing.Pending;
        Voice.IsEnabled = SendVoice.IsEnabled = Incoming.IsEnabled = Outgoing.IsEnabled = !routing.Pending && !busy;
        bool active = routing.Pending;
        bool showMeters = active && (routing.IncomingLevels != null || routing.OutgoingLevels != null);
        meterTimer.IsEnabled = showMeters;
        RefreshMeters();
        Route.IsEnabled = ready && !busy && (active || connected);
        BridgeState.Text = active ? "Bridge on" : "Bridge off";
        BridgeHint.Text = active ? ActiveBridgeHint()
            : busy ? "Please wait while the current operation finishes."
            : connected ? "Choose the languages below, then turn on the bridge."
            : signedIn ? "Open Account in Codex Settings to reconnect."
            : "Open Codex Settings to connect your account.";
    }

    string ActiveBridgeHint()
    {
        if (!Routing.Translates(IncomingMode))
            return "Turn off the bridge to change languages or restore your devices.";
        var microphone = Routing.Translates(OutgoingMode) ? "Hi-Fi Cable Output" : "your physical microphone";
        return $"In your call app, select CABLE Input as the speaker and {microphone} as the microphone.";
    }

    void RefreshMeters()
    {
        UpdateMeter(routing.IncomingLevels, IncomingMeters, IncomingOriginal, IncomingTranslation);
        UpdateMeter(routing.OutgoingLevels, OutgoingMeters, OutgoingOriginal, OutgoingTranslation);
    }

    static void UpdateMeter(AudioLevels? levels, FrameworkElement meters, ProgressBar original, ProgressBar translation)
    {
        meters.Visibility = levels == null ? Visibility.Collapsed : Visibility.Visible;
        var peaks = levels?.Read() ?? (Original: 0d, Translated: 0d);
        original.Value = peaks.Original;
        translation.Value = peaks.Translated;
    }

    async Task<bool> CheckSession()
    {
        connected = false;
        try
        {
            cliFound = CodexSession.FindCli() != null;
            if (!cliFound) { signedIn = false; SessionState.Text = "Codex will be installed when you sign in."; return false; }
            using var accountTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            accountTimeout.CancelAfter(TimeSpan.FromSeconds(15));
            signedIn = await CodexSession.SignedIn(accountTimeout.Token);
            if (!signedIn) { SessionState.Text = "Signed out · Open Codex Settings to sign in"; return false; }
            SessionState.Text = "Testing the Codex connection…";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(40));
            using var server = await AccountServer.Connect(timeout.Token);
            await server.Call("account/rateLimits/read", new { }, timeout.Token);
            connected = true;
            SessionState.Text = "Codex connected";
            if (!catalogLoaded)
            {
                try
                {
                    var voices = await CodexVoice.Choices(timeout.Token);
                    LoadVoices(voices);
                    SpeechState.Text = "Choose a voice for each direction. Codex default uses your session’s voice.";
                    catalogLoaded = true;
                }
                catch (Exception) { SpeechState.Text = "Voice list unavailable. Preview to check Codex realtime access."; }
            }
        }
        catch (Exception ex) { SessionState.Text = signedIn ? "Session saved · Retry connection" : "Account status unavailable"; Status.Text = ex is OperationCanceledException ? "The connection test timed out." : ex.Message; }
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
        await RefreshSession();
    }

    async Task RefreshSession()
    {
        busy = true;
        UpdateControls();
        try { await CheckSession(); }
        finally { busy = false; UpdateControls(); }
    }



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
            catalogLoaded = false;
            if (login)
                await server.Login((code, url) =>
                {
                    UserCode.Text = code;
                    verificationUrl = url;
                    Challenge.Visibility = Visibility.Visible;
                    SessionState.Text = "Waiting for browser approval…";
                    UpdateControls();
                    OpenPage(url.AbsoluteUri);
                }, attempt.Token);
            else
                await server.HasChatGptAccount(attempt.Token, refreshToken: true);
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
    }

    void MinimizeClick(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    void MaximizeClick(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    void CloseClick(object sender, RoutedEventArgs e) => Close();

    async void FirstRender(object? sender, EventArgs e)
    {
        ContentRendered -= FirstRender;
        var launchDelay = Task.Delay(TimeSpan.FromSeconds(2.5));
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Background);
        if (!IsLoaded) return;
        try { ownsInstance = instance.WaitOne(0); }
        catch (AbandonedMutexException) { ownsInstance = true; }
        if (!ownsInstance)
        {
            MessageBox.Show(this, "SesKöprüsü is already running.", Title);
            Close();
            return;
        }
        UpdateState.Text = $"Installed version {SetupUpdates.Current.ToString(3)}";
        meterTimer.Tick += (_, _) => RefreshMeters();
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
        LoadVoices([CodexVoice.Default]);
        try { routing.Restore(); } catch (Exception ex) { Status.Text = ex.Message; }
        Route.IsChecked = routing.Pending;
        ready = true;
        UpdateControls();
        if (!Local.Has("onboarding")) ShowPage(GuidePane);
        await launchDelay;
        if (!IsLoaded || lifetime.IsCancellationRequested) return;
        Workspace.Visibility = Visibility.Visible;
        LaunchScreen.Visibility = Visibility.Collapsed;
        await RefreshSession();
    }

    void SettingsClick(object sender, RoutedEventArgs e)
    {
        if (GuidePane.IsVisible) FinishGuideClick(sender, e);
        else if (Settings.IsVisible) ShowPage(returnToGuide ? GuidePane : HomePane);
        else ShowSettings();
    }

    void ShowPage(FrameworkElement page)
    {
        if (GuidePane.IsVisible) guideOffset = PageScroll.VerticalOffset;
        GuidePane.Visibility = page == GuidePane ? Visibility.Visible : Visibility.Collapsed;
        HomePane.Visibility = page == HomePane ? Visibility.Visible : Visibility.Collapsed;
        Settings.Visibility = page == Settings ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = page == GuidePane ? "Getting started" : page == Settings ? "Codex settings" : "Audio bridge";
        Nav.Content = page == GuidePane ? "Skip guide" : page == Settings
            ? (returnToGuide ? "Back to guide" : "Back to bridge") : "Codex Settings";
        PageScroll.ScrollToVerticalOffset(page == GuidePane ? guideOffset : 0);
    }

    void ShowSettings(TabItem? tab = null)
    {
        returnToGuide = GuidePane.IsVisible;
        ShowPage(Settings);
        if (tab != null) tab.IsSelected = true;
        TryRefreshDrivers();
        RecorderHint.Text = Routing.Translates(OutgoingMode)
            ? $"Recording input: {Routing.MicrophoneCable(IncomingMode)} Output. Turn on the bridge before opening Windows Sound Recorder."
            : "To record your translated microphone, choose an outgoing language on the home screen.";
    }

    void OpenGuideClick(object sender, RoutedEventArgs e) => ShowPage(GuidePane);
    void GuideAccountClick(object sender, RoutedEventArgs e) => ShowSettings(AccountTab);
    void GuideDevicesClick(object sender, RoutedEventArgs e) => ShowSettings(DeviceTab);
    void GuideLanguagesClick(object sender, RoutedEventArgs e) => ShowPage(HomePane);

    void FinishGuideClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Local.Has("onboarding")) Local.Save("onboarding", true);
        }
        catch (Exception ex) { Status.Text = $"Could not save guide preference. {ex.Message}"; }
        ShowPage(HomePane);
        guideOffset = 0;
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

    string MicSetupCable => Routing.Translates(OutgoingMode) ? Routing.MicrophoneCable(IncomingMode) : Routing.MicrophoneCable(Routing.Original);

    void SoundSettingsClick(object sender, RoutedEventArgs e) => OpenPage("ms-settings:sound");

    void RefreshDrivers()
    {
        var inventory = routing.Inspect(IncomingMode, OutgoingMode);
        var microphone = inventory.Single(d => d.Name == MicSetupCable);
        string hint = Routing.Translates(OutgoingMode) ? $"Target: {OutgoingMode}" :
            "For a microphone-only test, select Keep original for incoming audio and a language for outgoing audio.";
        MicPath.Text = $"Source: {routing.MicrophoneName()}\nRecorder input: {microphone.Name} Output\n{hint}\n" +
            (microphone.FormatIssue != null ? "Microphone sample rate needs attention." : microphone.Ready ? "Microphone driver ready." : microphone.CanEnable ? "Virtual microphone disabled." :
             microphone.Installed ? "Driver installed. Check endpoint visibility in Windows Sound." : "Virtual microphone driver required.");
        MicSetup.Content = microphone.CanEnable ? "Enable microphone devices" : $"Set up microphone driver ({microphone.Name})";
        MicSetup.Visibility = microphone.CanEnable || microphone.NeedsInstall ? Visibility.Visible : Visibility.Collapsed;
        DriverState.ItemsSource = inventory;
        var unavailable = inventory.FirstOrDefault(d => d.Required && !d.Ready);
        Repair.Visibility = unavailable != microphone && (unavailable is { CanEnable: true } or { NeedsInstall: true }) ? Visibility.Visible : Visibility.Collapsed;
        ImportPackage.Visibility = unavailable is { NeedsInstall: true } ? Visibility.Visible : Visibility.Collapsed;
        Repair.Content = unavailable?.CanEnable == true ? "Enable devices" :
            unavailable != null && DriverSetup.HasPackage(unavailable.Name) ? "Install local package" : "Set up driver";
        DeviceHint.Text = unavailable == null ? "No installation needed for the selected directions." :
            unavailable.FormatIssue != null ? unavailable.FormatIssue :
            unavailable.CanEnable ? "Enable the disabled endpoints, then refresh." :
            unavailable.Installed ? (System.Windows.SystemParameters.IsRemoteSession
                ? "Remote Desktop session: connect at the host PC or choose Play on remote computer in Remote Audio settings, reconnect and refresh."
                : "The driver is installed. Check playback and recording devices in Windows Sound, then refresh. A recent driver installation may require a restart.") :
            unavailable.Name == Routing.HiFi ? "Install VB-Audio Hi-Fi Cable for two-way translation. Windows will ask for administrator approval." :
            "Driver registration requires Windows administrator approval.";
    }

    void RefreshDevicesClick(object sender, RoutedEventArgs e) => TryRefreshDrivers();

    async void RepairClick(object sender, RoutedEventArgs e)
    {
        busy = true;
        UpdateControls();
        try
        {
            var inventory = routing.Inspect(IncomingMode, OutgoingMode);
            var device = sender == MicSetup ? inventory.Single(d => d.Name == MicSetupCable) :
                inventory.FirstOrDefault(d => d.Required && !d.Ready);
            if (device == null) { Status.Text = "Audio devices are ready."; return; }
            if (device.CanEnable) routing.EnableDisabled(device.Name);
            else if (device.NeedsInstall)
            {
                string? zip = null;
                if (sender == ImportPackage)
                {
                    var picker = new Microsoft.Win32.OpenFileDialog { Filter = "VB-Audio driver package|*.zip", Title = $"Select the official {device.Name} driver ZIP" };
                    if (picker.ShowDialog(this) != true) return;
                    zip = picker.FileName;
                }
                await DriverSetup.Install(http, device.Name, zip, Progress, lifetime.Token);
            }
            Status.Text = "Device status refreshed.";
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { Transfer.Visibility = Visibility.Collapsed; busy = false; UpdateControls(); TryRefreshDrivers(); }
    }

    void TryRefreshDrivers()
    {
        try { RefreshDrivers(); } catch (Exception ex) { DriverState.ItemsSource = null; DeviceHint.Text = ex.Message; MicPath.Text = "Microphone status unavailable."; MicSetup.Visibility = ImportPackage.Visibility = Repair.Visibility = Visibility.Collapsed; }
    }

    void LanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        try { Local.Save("languages", new[] { (string)Incoming.SelectedItem, (string)Outgoing.SelectedItem }); }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    void LoadVoices(string[] voices)
    {
        updatingVoices = true;
        try
        {
            Voice.ItemsSource = SendVoice.ItemsSource = voices;
            Voice.SelectedIndex = SendVoice.SelectedIndex = 0;
            if (Local.Has("voice") && Local.Read<string>("voice") is string incoming && voices.Contains(incoming))
                Voice.SelectedItem = incoming;
            if (Local.Has("outgoingVoice") && Local.Read<string>("outgoingVoice") is string outgoing && voices.Contains(outgoing))
                SendVoice.SelectedItem = outgoing;
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { updatingVoices = false; }
    }

    void VoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || updatingVoices) return;
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
                if (routing.Inspect(IncomingMode, OutgoingMode).FirstOrDefault(d => d.Required && !d.Ready) is { } unavailable)
                {
                    ShowSettings(DeviceTab);
                    Status.Text = unavailable.FormatIssue ?? $"{unavailable.Name} is unavailable for routing in this session. See Devices for details.";
                    return;
                }
                if (!await CheckSession()) return;
                string incoming = (string)Incoming.SelectedItem, outgoing = (string)Outgoing.SelectedItem;
                Status.Text = Routing.Translates(incoming) && Routing.Translates(outgoing)
                    ? "Connecting incoming audio and outgoing microphone…" : "Connecting audio…";
                await routing.EnableAsync(incoming, outgoing, (string)Voice.SelectedItem, (string)SendVoice.SelectedItem, error => Dispatcher.InvokeAsync(() =>
                {
                    try { routing.Restore(); Status.Text = error; }
                    catch (Exception ex) { Status.Text = ex.Message; }
                    Route.IsChecked = routing.Pending;
                    UpdateControls();
                }), lifetime.Token);
            }
            else routing.Restore();
            Status.Text = routing.Pending && Routing.Translates(OutgoingMode)
                ? (Routing.Translates(IncomingMode) ? $"Headphones: {IncomingMode}. " : "") + $"Microphone: {OutgoingMode} → {Routing.MicrophoneCable(IncomingMode)} Output."
                : routing.Pending ? "Audio bridge is on. Your microphone sends its original audio." : "Your previous devices have been restored.";
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { busy = false; Route.IsChecked = routing.Pending; UpdateControls(); }
    }

    async Task RunAudio(Func<Task> action, string direction)
    {
        busy = true;
        UpdateControls();
        Status.Text = $"Preparing {direction} voice preview…";
        try { if (!await CheckSession()) { Status.Text = "Sign in through Codex Settings to continue."; return; } await action(); Status.Text = "Voice preview finished."; }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { busy = false; UpdateControls(); }
    }

    async void PreviewClick(object sender, RoutedEventArgs e)
    {
        bool outgoing = sender == PreviewSend;
        var voice = (string)(outgoing ? SendVoice : Voice).SelectedItem;
        var target = outgoing ? OutgoingMode : IncomingMode;
        await RunAudio(() => CodexVoice.Preview(voice, Routing.Translates(target) ? target : "Turkish", lifetime.Token), outgoing ? "outgoing" : "incoming");
    }

    SetupUpdate? availableUpdate;

    async void UpdateClick(object sender, RoutedEventArgs e)
    {
        if (busy || routing.Pending) return;
        busy = true;
        UpdateControls();
        try
        {
            if (availableUpdate == null)
            {
                UpdateState.Text = "Checking for a new Windows setup…";
                availableUpdate = await SetupUpdates.Check(http, lifetime.Token);
                UpdateState.Text = availableUpdate == null ? $"Up to date · {SetupUpdates.Current.ToString(3)}" :
                    $"Version {availableUpdate.Version} is available. Save your work before installing.";
                if (availableUpdate != null) UpdateButton.Content = "Download and install";
                return;
            }
            var file = await SetupUpdates.Download(http, availableUpdate, p => Progress("Downloading update…", p), lifetime.Token);
            using var installer = Process.Start(new ProcessStartInfo(file) { UseShellExecute = true, Verb = "runas" });
            if (installer == null) throw new InvalidOperationException("Setup could not start.");
            Close();
        }
        catch (Exception ex) { UpdateState.Text = "Update failed. " + ex.Message; }
        finally { Transfer.Visibility = Visibility.Collapsed; busy = false; UpdateControls(); }
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!ownsInstance) return;
        try { routing.Restore(); }
        catch (Exception ex) { Status.Text = ex.Message; e.Cancel = true; return; }
        meterTimer.Stop();
        lifetime.Cancel();
        instance.ReleaseMutex();
        instance.Dispose();
    }
}
