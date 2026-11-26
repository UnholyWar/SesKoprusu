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

    MMDevice? FindCable(DataFlow flow, string name, DeviceState state)
    {
        foreach (var device in devices.EnumerateAudioEndPoints(flow, state))
        {
            var label = device.FriendlyName;
            if (label.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                label.StartsWith(name + " (", StringComparison.OrdinalIgnoreCase) ||
                (device.DeviceFriendlyName.Equals(name.StartsWith("CABLE-A") ? "VB-Audio Cable A" : "VB-Audio Virtual Cable", StringComparison.OrdinalIgnoreCase)
                 && !label.Contains("16ch", StringComparison.OrdinalIgnoreCase)))
                return device;
            device.Dispose();
        }
        return null;
    }

    MMDevice Cable(DataFlow flow, string name) => FindCable(flow, name, DeviceState.Active)
        ?? throw new InvalidOperationException($"{name} is unavailable. Open Codex Settings to repair the audio devices.");

    public const string Original = "Keep original";
    public const string Monitor = "Pass through";
    public static bool Translates(string direction) => direction != Original && direction != Monitor;

    internal record CableStatus(string Name, bool Required, bool Installed, DeviceState? Playback, DeviceState? Recording)
    {
        public bool Ready => Playback == DeviceState.Active && Recording == DeviceState.Active;
        public bool CanEnable => Playback == DeviceState.Disabled || Recording == DeviceState.Disabled;
        public bool NeedsInstall => !Installed && !Ready;
        public string Description => $"{Name} · {(Required ? "Required" : "Optional")}\nDriver: {(Installed ? "Installed" : "Not detected")}   ·   Package: {(DriverSetup.HasPackage(Name) ? "Available locally" : "Not cached")}\nPlayback: {Describe(Playback)}   ·   Recording: {Describe(Recording)}";

        static string Describe(DeviceState? state) => state switch
        {
            DeviceState.Active => "Ready", DeviceState.Disabled => "Disabled",
            DeviceState.Unplugged => "Disconnected", _ => "Not visible"
        };
    }

    public static string MicrophoneCable(string incoming) => Translates(incoming) ? "CABLE-A" : "CABLE";

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
        foreach (var prefix in new[] { "CABLE", "CABLE-A" })
        {
            using var playback = FindCable(DataFlow.Render, prefix + " Input", DeviceState.Active | DeviceState.Disabled | DeviceState.Unplugged);
            using var recording = FindCable(DataFlow.Capture, prefix + " Output", DeviceState.Active | DeviceState.Disabled | DeviceState.Unplugged);
            var description = prefix == "CABLE" ? "VB-Audio Virtual Cable" : "VB-Audio Cable A";
            bool present = controllers.Any(n => string.Equals(n, description, StringComparison.OrdinalIgnoreCase)) || playback != null || recording != null;
            result.Add(new(prefix, (prefix == "CABLE" && RoutePlayback(incoming, outgoing)) || (Translates(outgoing) && prefix == MicrophoneCable(incoming)), present, playback?.State, recording?.State));
        }
        return result;
    }

    public void EnableDisabled(string prefix)
    {
        foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
        {
            using var disabled = FindCable(flow, prefix + (flow == DataFlow.Render ? " Input" : " Output"), DeviceState.Disabled);
            if (disabled == null) continue;
            var policy = CreatePolicy();
            try { policy.SetEndpointVisibility(disabled.ID, 1); }
            finally { Marshal.ReleaseComObject(policy); }
        }
    }

    AudioTranslation? outgoingStream;
    MMDevice? recorder;
    public string? MicrophoneSignal()
    {
        if (outgoingStream == null || recorder == null) return null;
        var summary = outgoingStream.Activity.Summary;
        try { return $"{summary}\n{recorder.FriendlyName} · Windows peak: {recorder.AudioMeterInformation.MasterPeakValue:P0}"; }
        catch (COMException) { return summary + "\nWindows level unavailable."; }
    }

    readonly List<AudioTranslation> translations = new();
    readonly List<MMDevice> endpoints = new();

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
        var saved = (from flow in flows from role in Roles select new Endpoint(flow, role, DefaultId(flow, role))).ToArray();
        Local.Save("routing", saved);
        try
        {
            string? renderId = null, recordingId = null;
            if (Translates(outgoing))
            {
                var prefix = MicrophoneCable(incoming);
                var send = Hold(Cable(DataFlow.Render, prefix + " Input"));
                var virtualMic = Hold(Cable(DataFlow.Capture, prefix + " Output"));
                var mic = Hold(devices.GetDevice(saved.Single(e => e.Flow == DataFlow.Capture && e.Role == Role.Multimedia).Id));
                if (mic.ID == virtualMic.ID || mic.FriendlyName.StartsWith("CABLE Output", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Select your physical microphone first.");
                using var physicalOutput = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (physicalOutput.ID == send.ID)
                    throw new InvalidOperationException("Choose your headphones as the Windows output before enabling microphone translation.");
                outgoingStream = new AudioTranslation(mic, send, outgoing, outgoingVoice, error => failed("Outgoing microphone: " + error));
                translations.Add(outgoingStream);
                recorder = virtualMic;
                recordingId = virtualMic.ID;
            }
            if (RoutePlayback(incoming, outgoing))
            {
                using var output = Cable(DataFlow.Render, "CABLE Input");
                renderId = output.ID;
                var input = Hold(Cable(DataFlow.Capture, "CABLE Output"));
                var speakers = Hold(devices.GetDevice(saved.Single(e => e.Flow == DataFlow.Render && e.Role == Role.Multimedia).Id));
                if (speakers.ID == renderId) throw new InvalidOperationException("Select your physical speakers as the default output first.");
                if (Translates(incoming))
                    translations.Add(new AudioTranslation(input, speakers, incoming, incomingVoice, error => failed("Incoming audio: " + error)));
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
        outgoingStream = null;
        recorder = null;
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
