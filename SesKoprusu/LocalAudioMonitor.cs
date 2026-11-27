using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SesKoprusu;

internal sealed class LocalAudioMonitor : IDisposable
{
    readonly WasapiCapture capture;
    readonly WasapiOut? player;
    volatile bool disposed;

    public LocalAudioMonitor(MMDevice source, MMDevice headphones, Func<float> gain, Action<string> failed)
    {
        capture = new WasapiCapture(source, false, 40);
        try
        {
            player = new WasapiOut(headphones, AudioClientShareMode.Shared, false, 40);
            var queue = new BufferedWaveProvider(capture.WaveFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(160),
                DiscardOnBufferOverflow = true
            };
            var local = new VolumeSampleProvider(queue.ToSampleProvider()) { Volume = gain() };
            player.Init(local.ToWaveProvider());
            capture.DataAvailable += (_, e) =>
            {
                local.Volume = gain();
                queue.AddSamples(e.Buffer, 0, e.BytesRecorded);
            };
            capture.RecordingStopped += (_, e) =>
            {
                if (!disposed) failed(e.Exception?.Message ?? "Audio capture stopped.");
            };
            player.PlaybackStopped += (_, e) =>
            {
                if (!disposed && e.Exception != null) failed(e.Exception.Message);
            };
            player.Play();
            capture.StartRecording();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        disposed = true;
        try { capture.Dispose(); }
        finally { player?.Dispose(); }
    }
}
