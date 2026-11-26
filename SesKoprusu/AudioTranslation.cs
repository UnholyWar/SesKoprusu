using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SesKoprusu;

internal sealed class AudioTranslation : IDisposable
{
    readonly CancellationTokenSource stop = new();
    readonly Channel<byte[]> clips = Channel.CreateBounded<byte[]>(2);
    readonly WasapiCapture capture;
    readonly MemoryStream chunk = new();
    readonly Task worker;

    public AudioTranslation(MMDevice source, MMDevice destination, string language, string voice,
        string key, HttpClient http, Action<string> failed)
    {
        capture = new WasapiCapture(source);
        var format = capture.WaveFormat;
        capture.DataAvailable += (_, e) =>
        {
            chunk.Write(e.Buffer, 0, e.BytesRecorded);
            if (chunk.Length < format.AverageBytesPerSecond * 4) return;
            if (!clips.Writer.TryWrite(chunk.ToArray()) && !stop.IsCancellationRequested)
                {
                    stop.Cancel();
                    failed("Çeviri ses akışına yetişemedi. Bağlantıyı kontrol edip tekrar deneyin.");
                }
            chunk.SetLength(0);
        };
        worker = ProcessClips();
        try { capture.StartRecording(); } catch { Dispose(); throw; }

        async Task ProcessClips()
        {
            var token = stop.Token;
            try
            {
                await foreach (var samples in clips.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    using var raw = new RawSourceWaveStream(new MemoryStream(samples), format);
                    using var resampled = new MediaFoundationResampler(raw, new WaveFormat(16000, 16, 1));
                    using var wav = new MemoryStream();
                    WaveFileWriter.WriteWavFileToStream(wav, resampled);
                    var bytes = wav.ToArray();
                    bool audible = false;
                    for (int i = 80; i + 1 < bytes.Length; i += 16)
                        if (Math.Abs((int)BitConverter.ToInt16(bytes, i)) > 240) { audible = true; break; }
                    if (!audible) continue;
                    using var form = new MultipartFormDataContent();
                    form.Add(new StringContent("whisper-1"), "model");
                    form.Add(new ByteArrayContent(bytes), "file", "speech.wav");
                    using var transcript = await Send("audio/transcriptions", form, token);
                    using var json = await JsonDocument.ParseAsync(await transcript.Content.ReadAsStreamAsync(token), cancellationToken: token);
                    var sourceText = json.RootElement.GetProperty("text").GetString();
                    if (string.IsNullOrWhiteSpace(sourceText)) continue;
                    using var translation = await Send("chat/completions", JsonContent.Create(new
                    {
                        model = "gpt-4o-mini",
                        messages = new[] {
                            new { role = "system", content = $"Kullanıcının konuşmasını {language} diline çevir. Yalnızca çeviriyi yaz." },
                            new { role = "user", content = sourceText }
                        }
                    }), token);
                    using var answer = JsonDocument.Parse(await translation.Content.ReadAsStringAsync(token));
                    var translated = answer.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                    using var spoken = await Send("audio/speech", JsonContent.Create(new { model = "gpt-4o-mini-tts", voice, input = translated, response_format = "wav" }), token);
                    using var reader = new WaveFileReader(new MemoryStream(await spoken.Content.ReadAsByteArrayAsync(token)));
                    using var player = new WasapiOut(destination, AudioClientShareMode.Shared, false, 80);
                    player.Init(reader);
                    player.Play();
                    while (player.PlaybackState == PlaybackState.Playing) await Task.Delay(80, token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) { if (!token.IsCancellationRequested) failed(ex.Message); }
        }

        async Task<HttpResponseMessage> Send(string path, HttpContent body, CancellationToken token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/" + path) { Content = body };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            var response = await http.SendAsync(request, token);
            if (response.IsSuccessStatusCode) return response;
            var code = response.StatusCode;
            response.Dispose();
            throw new HttpRequestException($"Ses API isteği başarısız ({code}).");
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        capture.Dispose();
        clips.Writer.TryComplete();
        chunk.Dispose();
        _ = worker.ContinueWith(_ => stop.Dispose(), TaskScheduler.Default);
    }
}
