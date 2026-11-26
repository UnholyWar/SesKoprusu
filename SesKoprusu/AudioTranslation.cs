using NAudio.CoreAudioApi;

namespace SesKoprusu;

internal sealed class AudioTranslation : IDisposable
{
    readonly CancellationTokenSource stop = new();
    readonly Task worker;
    readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal sealed class Signal
    {
        long microphone, recognized, returned;
        public void SentSpeech() => Interlocked.Exchange(ref microphone, Environment.TickCount64);
        public void RecognizedSpeech() => Interlocked.Exchange(ref recognized, Environment.TickCount64);
        public void ReceivedSpeech() => Interlocked.Exchange(ref returned, Environment.TickCount64);
        public string Summary => $"Mic audio sent: {Age(Interlocked.Read(ref microphone))}   ·   Codex input transcript: {Age(Interlocked.Read(ref recognized))}\nCodex audio received: {Age(Interlocked.Read(ref returned))}";
        static string Age(long tick) => tick == 0 ? "waiting" : $"{(Environment.TickCount64 - tick) / 1000}s ago";
    }

    public Signal Activity { get; } = new();
    public Task Ready => ready.Task;

    public AudioTranslation(MMDevice source, MMDevice destination, string language, string voice, Action<string> failed, bool physicalMicrophone = false)
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
                    input, output, token, () => ready.TrySetResult(), physicalMicrophone ? Activity : null);
            }
            catch (Exception ex)
            {
                if (!ready.TrySetException(ex) && !disposed) failed(ex.Message);
            }
        });
    }

    volatile bool disposed;
    public void Dispose()
    {
        disposed = true;
        stop.Cancel();
        ready.TrySetCanceled();
        _ = worker.ContinueWith(_ => stop.Dispose(), TaskScheduler.Default);
    }
}
