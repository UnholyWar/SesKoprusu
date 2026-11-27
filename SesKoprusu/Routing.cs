using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Runtime.InteropServices;

namespace SesKoprusu;

internal sealed class Routing
{
    internal record Endpoint(DataFlow Flow, Role Role, string Id);
    static readonly Role[] Roles = [Role.Console, Role.Multimedia, Role.Communications];
    readonly MMDeviceEnumerator devices = new();
    WasapiCapture? capture;
    WasapiOut? monitor;
    public bool Pending => Local.Has("routing");

    public const string HiFi = "Hi-Fi Cable";
    static string ControllerName(string cable) => cable == HiFi ? "VB-Audio Hi-Fi Cable" : "VB-Audio Virtual Cable";

    static bool MatchesCable(MMDevice device, string cable, DataFlow flow)
    {
        var label = device.FriendlyName;
        var name = cable + (flow == DataFlow.Render ? " Input" : " Output");
        return !label.Contains("16ch", StringComparison.OrdinalIgnoreCase) &&
            (label.Equals(name, StringComparison.OrdinalIgnoreCase) ||
             label.StartsWith(name + " (", StringComparison.OrdinalIgnoreCase) ||
             device.DeviceFriendlyName.Equals(ControllerName(cable), StringComparison.OrdinalIgnoreCase));
    }

    MMDevice? FindCable(DataFlow flow, string cable, DeviceState state)
    {
        MMDevice? candidate = null;
        foreach (var device in devices.EnumerateAudioEndPoints(flow, state))
        {
            if (MatchesCable(device, cable, flow))
            {
                if (device.State == DeviceState.Active)
                {
                    candidate?.Dispose();
                    return device;
                }
                if (candidate == null || (device.State == DeviceState.Disabled && candidate.State != DeviceState.Disabled))
                {
                    candidate?.Dispose();
                    candidate = device;
                    continue;
                }
            }
            device.Dispose();
        }
        return candidate;
    }

    MMDevice Cable(DataFlow flow, string name) => FindCable(flow, name, DeviceState.Active)
        ?? throw new InvalidOperationException($"{name} is unavailable. Open Codex Settings to repair the audio devices.");

    public const string Original = "Keep original";
    public const string Monitor = "Pass through";
    public static bool Translates(string direction) => direction != Original && direction != Monitor;

    internal record CableStatus(string Name, bool Required, bool Installed, DeviceState? Playback, DeviceState? Recording, string? FormatIssue)
    {
        public bool Ready => Playback == DeviceState.Active && Recording == DeviceState.Active && FormatIssue == null;
        public bool CanEnable => Playback == DeviceState.Disabled || Recording == DeviceState.Disabled;
        public bool NeedsInstall => !Installed && !Ready;
        public string Requirement => Required ? "Required" : "Optional";
        public string Description => $"Driver: {(Installed ? "Installed" : "Not detected")}   ·   Package: {(DriverSetup.HasPackage(Name) ? "Available locally" : "Not cached")}\nPlayback: {Describe(Playback)}   ·   Recording: {Describe(Recording)}";

        static string Describe(DeviceState? state) => state switch
        {
            DeviceState.Active => "Ready", DeviceState.Disabled => "Disabled",
            DeviceState.Unplugged => "Disconnected", _ => "Not visible"
        };
    }

    public static string MicrophoneCable(string incoming) => Translates(incoming) ? HiFi : "CABLE";

    static bool RoutePlayback(string incoming, string outgoing) =>
        Translates(incoming) || (incoming == Monitor && !Translates(outgoing));

    public string MicrophoneName()
    {
        try
        {
            var original = Pending ? Local.Read<Endpoint[]>("routing").FirstOrDefault(e =>
                e.Flow == DataFlow.Capture && e.Role == Role.Multimedia) : null;
            using var mic = original == null ? devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia) : devices.GetDevice(original.Id);
            return mic.FriendlyName;
        }
        catch (COMException) { return "Unavailable · select a microphone in Windows Sound"; }
    }

    public List<CableStatus> Inspect(string incoming, string outgoing)
    {
        var controllers = AudioControllers.Present();
        var result = new List<CableStatus>();
        foreach (var prefix in new[] { "CABLE", HiFi })
        {
            using var playback = FindCable(DataFlow.Render, prefix, DeviceState.Active | DeviceState.Disabled | DeviceState.Unplugged);
            using var recording = FindCable(DataFlow.Capture, prefix, DeviceState.Active | DeviceState.Disabled | DeviceState.Unplugged);
            var description = ControllerName(prefix);
            bool present = controllers.Any(n => string.Equals(n, description, StringComparison.OrdinalIgnoreCase)) || playback != null || recording != null;
            result.Add(new(prefix, (prefix == "CABLE" && RoutePlayback(incoming, outgoing)) || (Translates(outgoing) && prefix == MicrophoneCable(incoming)), present, playback?.State, recording?.State,
                prefix == HiFi && playback?.State == DeviceState.Active && recording?.State == DeviceState.Active
                    ? CheckHiFiFormat(playback, recording) : null));
        }
        return result;
    }

    static string? CheckHiFiFormat(MMDevice input, MMDevice output)
    {
        try
        {
            using var playback = input.AudioClient;
            using var recording = output.AudioClient;
            var inputRate = playback.MixFormat.SampleRate;
            var outputRate = recording.MixFormat.SampleRate;
            return inputRate == outputRate ? null :
                $"Hi-Fi Cable Input is {inputRate} Hz and Output is {outputRate} Hz. In Windows Sound, set both default formats to the same rate, then refresh.";
        }
        catch (COMException)
        {
            return "Windows could not read the Hi-Fi Cable formats. Close apps using the cable and refresh devices.";
        }
    }

    public void EnableDisabled(string prefix)
    {
        foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
        {
            using var disabled = FindCable(flow, prefix, DeviceState.Disabled);
            if (disabled == null) continue;
            var policy = CreatePolicy();
            try { policy.SetEndpointVisibility(disabled.ID, 1); }
            finally { Marshal.ReleaseComObject(policy); }
        }
    }

    readonly List<AudioTranslation> translations = new();
    readonly List<MMDevice> endpoints = new();
    public AudioLevels? IncomingLevels { get; private set; }
    public AudioLevels? OutgoingLevels { get; private set; }

    MMDevice Hold(MMDevice device)
    {
        endpoints.Add(device);
        return device;
    }

    public async Task EnableAsync(string incoming, string outgoing, string incomingVoice, string outgoingVoice, Action<string> failed, CancellationToken token)
    {
        if (Pending) throw new InvalidOperationException("Restore the previous devices first.");
        var flows = new List<DataFlow>();
        if (RoutePlayback(incoming, outgoing)) flows.Add(DataFlow.Render);
        if (Translates(outgoing)) flows.Add(DataFlow.Capture);
        if (flows.Count == 0) throw new InvalidOperationException("Both directions use the original audio. No routing is needed.");
        if (Translates(incoming) && !Translates(outgoing))
            CheckIncomingOnlyMicrophone();
        var saved = (from flow in flows from role in Roles select new Endpoint(flow, role, DefaultId(flow, role))).ToArray();
        Local.Save("routing", saved);
        try
        {
            string? renderId = null, recordingId = null;
            if (Translates(outgoing))
            {
                var prefix = MicrophoneCable(incoming);
                var send = Hold(Cable(DataFlow.Render, prefix));
                var virtualMic = Hold(Cable(DataFlow.Capture, prefix));
                var mic = Hold(devices.GetDevice(saved.Single(e => e.Flow == DataFlow.Capture && e.Role == Role.Multimedia).Id));
                if (MatchesCable(mic, "CABLE", DataFlow.Capture) || MatchesCable(mic, HiFi, DataFlow.Capture)) throw new InvalidOperationException("Select your physical microphone first.");
                using var physicalOutput = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (MatchesCable(physicalOutput, "CABLE", DataFlow.Render) || MatchesCable(physicalOutput, HiFi, DataFlow.Render))
                    throw new InvalidOperationException("Choose your headphones as the Windows output before enabling microphone translation.");
                if (prefix == HiFi && CheckHiFiFormat(send, virtualMic) is { } issue)
                    throw new InvalidOperationException(issue);
                var translation = new AudioTranslation(mic, send, outgoing, outgoingVoice,
                    error => failed("Outgoing microphone: " + error), physicalMicrophone: true);
                translations.Add(translation);
                OutgoingLevels = translation.Levels;
                recordingId = virtualMic.ID;
            }
            if (RoutePlayback(incoming, outgoing))
            {
                using var output = Cable(DataFlow.Render, "CABLE");
                renderId = output.ID;
                var input = Hold(Cable(DataFlow.Capture, "CABLE"));
                var speakers = Hold(devices.GetDevice(saved.Single(e => e.Flow == DataFlow.Render && e.Role == Role.Multimedia).Id));
                if (MatchesCable(speakers, "CABLE", DataFlow.Render) || MatchesCable(speakers, HiFi, DataFlow.Render)) throw new InvalidOperationException("Select your physical speakers as the default output first.");
                if (Translates(incoming))
                {
                    var translation = new AudioTranslation(input, speakers, incoming, incomingVoice,
                        error => failed("Incoming audio: " + error));
                    translations.Add(translation);
                    IncomingLevels = translation.Levels;
                }
                else
                {
                    capture = new WasapiCapture(input);
                    var buffer = new BufferedWaveProvider(capture.WaveFormat) { DiscardOnBufferOverflow = true };
                    capture.DataAvailable += (_, e) => buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
                    monitor = new WasapiOut(speakers, AudioClientShareMode.Shared, false, 80);
                    monitor.Init(buffer);
                    monitor.Play();
                    capture.StartRecording();
                }
            }
            // Switch Windows defaults only after every requested interpreter has opened its capture stream.
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(token);
            startup.CancelAfter(TimeSpan.FromSeconds(60));
            var pendingStarts = translations.Select(t => t.Ready).ToList();
            while (pendingStarts.Count > 0)
            {
                var started = await Task.WhenAny(pendingStarts).WaitAsync(startup.Token);
                await started;
                pendingStarts.Remove(started);
            }
            token.ThrowIfCancellationRequested();
            if (!Pending) throw new InvalidOperationException("Routing was stopped during startup.");
            foreach (var e in saved)
            {
                Set(e, e.Flow == DataFlow.Render ? renderId! : recordingId!);
            }
        }
        catch { Restore(); throw; }
    }

    void CheckIncomingOnlyMicrophone()
    {
        foreach (var role in Roles)
        {
            try
            {
                using var mic = devices.GetDefaultAudioEndpoint(DataFlow.Capture, role);
                if (MatchesCable(mic, "CABLE", DataFlow.Capture))
                    throw new InvalidOperationException("CABLE Output is capturing incoming audio. Choose your physical microphone in Windows Sound and in the call app.");
            }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x80070490))
            {
                // Incoming translation also works when there is no default microphone.
            }
        }
    }

    string DefaultId(DataFlow flow, Role role)
    {
        using var endpoint = devices.GetDefaultAudioEndpoint(flow, role);
        return endpoint.ID;
    }

    static IPolicy CreatePolicy() => (IPolicy)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9"), true)!)!;

    void Set(Endpoint e, string id)
    {
        var policy = CreatePolicy();
        try { policy.SetDefaultEndpoint(id, e.Role); }
        finally { Marshal.ReleaseComObject(policy); }
        if (DefaultId(e.Flow, e.Role) != id) throw new InvalidOperationException("Windows did not confirm the device change.");
    }

    public void Restore()
    {
        if (!Pending) return;
        var errors = new List<Exception>();
        foreach (var e in Local.Read<Endpoint[]>("routing"))
        {
            try { Set(e, e.Id); } catch (Exception ex) { errors.Add(ex); }
        }
        if (errors.Count > 0) throw new AggregateException("Reconnect the devices and retry restoration.", errors);
        capture?.Dispose(); capture = null;
        monitor?.Dispose(); monitor = null;
        foreach (var translation in translations) translation.Dispose();
        translations.Clear();
        IncomingLevels = OutgoingLevels = null;
        foreach (var endpoint in endpoints) endpoint.Dispose();
        endpoints.Clear();
        Local.Delete("routing");
    }

    // IPolicyConfig vtable slots preceding SetDefaultEndpoint (Windows Vista+).
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicy
    {
        void GetMixFormat(); void GetDeviceFormat(); void ResetDeviceFormat(); void SetDeviceFormat();
        void GetProcessingPeriod(); void SetProcessingPeriod(); void GetShareMode(); void SetShareMode();
        void GetPropertyValue(); void SetPropertyValue();
        void SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, Role role);
        void SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }
}
