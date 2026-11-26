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

    MMDevice Cable(DataFlow flow, string name) => devices.EnumerateAudioEndPoints(flow, DeviceState.Active)
        .Single(d => d.FriendlyName.StartsWith(name + " (", StringComparison.OrdinalIgnoreCase) &&
                     d.FriendlyName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase));

    public void Enable()
    {
        if (Pending) throw new InvalidOperationException("Önce önceki aygıtları geri yükleyin.");
        using var output = Cable(DataFlow.Render, "CABLE Input");
        using var input = Cable(DataFlow.Capture, "CABLE Output");
        var saved = (from flow in new[] { DataFlow.Render, DataFlow.Capture }
                     from role in Roles select new Endpoint(flow, role, DefaultId(flow, role))).ToArray();
        using var speakers = devices.GetDevice(saved.Single(e => e.Flow == DataFlow.Render && e.Role == Role.Multimedia).Id);
        if (speakers.ID == output.ID) throw new InvalidOperationException("Önce fiziksel ses çıkışını seçin.");
        Local.Save("routing", saved);
        try
        {
            capture = new WasapiCapture(input);
            var buffer = new BufferedWaveProvider(capture.WaveFormat) { DiscardOnBufferOverflow = true };
            capture.DataAvailable += (_, e) => buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            monitor = new WasapiOut(speakers, AudioClientShareMode.Shared, false, 80);
            monitor.Init(buffer);
            monitor.Play();
            capture.StartRecording();
            foreach (var e in saved) Set(e, e.Flow == DataFlow.Render ? output.ID : input.ID);
        }
        catch { Restore(); throw; }
    }

    string DefaultId(DataFlow flow, Role role)
    {
        using var endpoint = devices.GetDefaultAudioEndpoint(flow, role);
        return endpoint.ID;
    }

    void Set(Endpoint e, string id)
    {
        var policy = (IPolicy)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9"), true)!)!;
        try { policy.SetDefaultEndpoint(id, e.Role); }
        finally { Marshal.ReleaseComObject(policy); }
        if (DefaultId(e.Flow, e.Role) != id) throw new InvalidOperationException("Windows aygıt değişikliğini doğrulamadı.");
    }

    public void Restore()
    {
        if (!Pending) return;
        var errors = new List<Exception>();
        foreach (var e in Local.Read<Endpoint[]>("routing"))
        {
            try { Set(e, e.Id); } catch (Exception ex) { errors.Add(ex); }
        }
        if (errors.Count > 0) throw new AggregateException("Aygıtları bağlayıp geri yüklemeyi yeniden deneyin.", errors);
        capture?.Dispose(); capture = null;
        monitor?.Dispose(); monitor = null;
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
    }
}
