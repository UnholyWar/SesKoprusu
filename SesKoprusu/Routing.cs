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

    public string? Missing(string incoming, string outgoing, bool activate = false)
    {
        var required = new List<string>();
        if (incoming != Original) required.Add("CABLE");
        if (Translates(outgoing)) required.Add("CABLE-A");
        foreach (var prefix in required)
        foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
        {
            var name = prefix + (flow == DataFlow.Render ? " Input" : " Output");
            using var active = FindCable(flow, name, DeviceState.Active);
            if (active != null) continue;
            using var disabled = FindCable(flow, name, DeviceState.Disabled);
            if (activate && disabled != null)
            {
                var policy = CreatePolicy();
                try { policy.SetEndpointVisibility(disabled.ID, 1); }
                finally { Marshal.ReleaseComObject(policy); }
                using var restored = FindCable(flow, name, DeviceState.Active);
                if (restored != null) continue;
            }
            return prefix;
        }
        return null;
    }

    readonly List<AudioTranslation> translations = new();
    readonly List<MMDevice> endpoints = new();

    MMDevice Hold(MMDevice device)
    {
        endpoints.Add(device);
        return device;
    }

    public void Enable(string incoming, string outgoing, string incomingVoice, string outgoingVoice, Action<string> failed)
    {
        if (Pending) throw new InvalidOperationException("Restore the previous devices first.");
        var flows = new List<DataFlow>();
        if (incoming != Original) flows.Add(DataFlow.Render);
        if (Translates(outgoing) || (incoming == Monitor && outgoing == Monitor)) flows.Add(DataFlow.Capture);
        if (flows.Count == 0) throw new InvalidOperationException("Both directions use the original audio. No routing is needed.");
        var saved = (from flow in flows from role in Roles select new Endpoint(flow, role, DefaultId(flow, role))).ToArray();
        Local.Save("routing", saved);
        try
        {
            string? renderId = null, recordingId = null;
            if (Translates(outgoing))
            {
                var send = Hold(Cable(DataFlow.Render, "CABLE-A Input"));
                var virtualMic = Hold(Cable(DataFlow.Capture, "CABLE-A Output"));
                var mic = Hold(devices.GetDevice(saved.Single(e => e.Flow == DataFlow.Capture && e.Role == Role.Communications).Id));
                if (mic.ID == virtualMic.ID || mic.FriendlyName.StartsWith("CABLE Output", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Select your physical microphone first.");
                translations.Add(new AudioTranslation(mic, send, outgoing, outgoingVoice, failed));
                recordingId = virtualMic.ID;
            }
            if (incoming != Original)
            {
                using var output = Cable(DataFlow.Render, "CABLE Input");
                renderId = output.ID;
                var input = Hold(Cable(DataFlow.Capture, "CABLE Output"));
                var speakers = Hold(devices.GetDevice(saved.Single(e => e.Flow == DataFlow.Render && e.Role == Role.Multimedia).Id));
                if (speakers.ID == renderId) throw new InvalidOperationException("Select your physical speakers as the default output first.");
                if (incoming == Monitor && outgoing == Monitor) recordingId = input.ID;
                if (Translates(incoming))
                    translations.Add(new AudioTranslation(input, speakers, incoming, incomingVoice, failed));
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
