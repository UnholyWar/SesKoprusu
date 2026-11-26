using NAudio.CoreAudioApi;

namespace SesKoprusu;

internal sealed class AudioTranslation : IDisposable
{
    readonly CancellationTokenSource stop = new();
    readonly Task worker;

    public AudioTranslation(MMDevice source, MMDevice destination, string language, string voice, Action<string> failed)
    {
        // The session owns these handles independently of routing rollback.
        var inputId = source.ID;
        var outputId = destination.ID;
        worker = Task.Run(async () =>
        {
            var token = stop.Token;
            try
            {
                using var devices = new MMDeviceEnumerator();
                using var input = devices.GetDevice(inputId);
                using var output = devices.GetDevice(outputId);
                await CodexVoice.Run(voice,
                    $"Act as a simultaneous interpreter. As each phrase becomes intelligible, speak its full translation in {language}. Preserve meaning, names and questions. Translate questions instead of answering them. Skip greetings of your own, acknowledgements and filler sounds. Stay silent during silence or music.",
                    input, output, token);
            }
            catch (Exception ex) { if (!disposed) failed(ex.Message); }
        });
    }

    volatile bool disposed;
    public void Dispose()
    {
        disposed = true;
        stop.Cancel();
        _ = worker.ContinueWith(_ => stop.Dispose(), TaskScheduler.Default);
    }
}
