using NAudio.CoreAudioApi;

namespace SesKoprusu;

internal sealed class AudioTranslation : IDisposable
{
    readonly CancellationTokenSource stop = new();
    readonly Task worker;
    readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Ready => ready.Task;
    public AudioLevels Levels { get; } = new();

    public AudioTranslation(MMDevice source, MMDevice destination, string language, string voice, Action<string> failed,
        bool physicalMicrophone = false)
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
                    input, output, token, () => ready.TrySetResult(), physicalMicrophone, Levels);
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

internal sealed class AudioLevels
{
    int original, translated;

    public void RecordOriginal(ReadOnlySpan<byte> pcm) => Record(ref original, pcm);
    public void RecordTranslation(ReadOnlySpan<byte> pcm) => Record(ref translated, pcm);

    public (double Original, double Translated) Read() =>
        (Interlocked.Exchange(ref original, 0) / 32768d, Interlocked.Exchange(ref translated, 0) / 32768d);

    static void Record(ref int maximum, ReadOnlySpan<byte> pcm)
    {
        int peak = 0;
        for (int i = 0; i + 1 < pcm.Length; i += 2)
            peak = Math.Max(peak, Math.Abs((int)System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(i, 2))));

        int previous;
        do
        {
            previous = Volatile.Read(ref maximum);
            if (peak <= previous) return;
        } while (Interlocked.CompareExchange(ref maximum, peak, previous) != previous);
    }
}
