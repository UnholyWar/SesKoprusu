using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SesKoprusu;

internal static class CodexVoice
{
    public const string Default = "Codex default";

    public static async Task<string[]> Choices(CancellationToken token)
    {
        using var server = await AccountServer.Connect(token, true);
        var catalog = await server.Call("thread/realtime/listVoices", new { }, token);
        return [Default, .. catalog.GetProperty("voices").GetProperty("v2").EnumerateArray().Select(x => x.GetString()!)];
    }

    public static async Task<string> Start(AccountServer server, string voice, string instructions, CancellationToken token)
    {
        var thread = await server.Call("thread/start", new { ephemeral = true, sandbox = "read-only", approvalPolicy = "never" }, token);
        var threadId = thread.GetProperty("thread").GetProperty("id").GetString()!;
        await server.Call("thread/realtime/start", new { threadId, version = "v2", outputModality = "audio",
            voice = voice == Default ? null : voice, prompt = instructions, transport = new { type = "websocket" } }, token);
        while (true)
        {
            var message = await server.NextEvent(token);
            if (!message.TryGetProperty("method", out var method) || !message.TryGetProperty("params", out var payload) ||
                !payload.TryGetProperty("threadId", out var id) || id.GetString() != threadId) continue;
            if (method.GetString() == "thread/realtime/started") return threadId;
            CheckVoiceFailure(method.GetString(), payload);
        }
    }

    static void CheckVoiceFailure(string? method, System.Text.Json.JsonElement payload)
    {
        if (method == "thread/realtime/error")
            throw new IOException("Codex voice error: " + AccountServer.Detail(payload, "message"));
        if (method == "thread/realtime/closed")
            throw new IOException("Codex voice connection closed: " + AccountServer.Detail(payload, "reason"));
    }

    public static async Task Play(AccountServer server, string threadId, MMDevice destination, bool preview, CancellationToken token)
    {
        using var player = new WasapiOut(destination, AudioClientShareMode.Shared, false, 80);
        BufferedWaveProvider? buffer = null;
        while (true)
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
            if (preview) wait.CancelAfter(buffer == null ? TimeSpan.FromSeconds(35) : TimeSpan.FromSeconds(3));
            System.Text.Json.JsonElement message;
            try { message = await server.NextEvent(wait.Token); }
            catch (OperationCanceledException) when (preview && buffer != null && !token.IsCancellationRequested) { break; }
            if (message.TryGetProperty("error", out var error)) throw new IOException("Codex rejected the audio request: " + AccountServer.Detail(error, "message"));
            if (!message.TryGetProperty("method", out var method) || !message.TryGetProperty("params", out var p) ||
                !p.TryGetProperty("threadId", out var id) || id.GetString() != threadId) continue;
            if (preview && buffer != null && method.GetString() == "thread/realtime/closed"
                && (!p.TryGetProperty("reason", out var reason) || reason.GetString() is null or "completed" or "normal"))
                break;
            CheckVoiceFailure(method.GetString(), p);
            if (method.GetString() != "thread/realtime/outputAudio/delta") continue;
            var audio = p.GetProperty("audio");
            var bytes = Convert.FromBase64String(audio.GetProperty("data").GetString()!);
            if (buffer == null)
            {
                buffer = new BufferedWaveProvider(new WaveFormat(audio.GetProperty("sampleRate").GetInt32(), 16, audio.GetProperty("numChannels").GetInt32()))
                    { BufferDuration = TimeSpan.FromSeconds(45) };
                player.Init(buffer);
                player.Play();
            }
            buffer.AddSamples(bytes, 0, bytes.Length);
        }
        while (buffer!.BufferedBytes > 0) await Task.Delay(60, token);
        await Task.Delay(100, token);
    }

    public static async Task Preview(string voice, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(1));
        using var server = await AccountServer.Connect(timeout.Token, true);
        var id = await Start(server, voice, "Read the requested sample in Turkish, exactly once.", timeout.Token);
        await server.Call("thread/realtime/appendText", new { threadId = id, text = "Say: Merhaba, SesKöprüsü ile Türkçe ses örneğini dinliyorsunuz." }, timeout.Token);
        using var devices = new MMDeviceEnumerator();
        using var speakers = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        await Play(server, id, speakers, true, timeout.Token);
    }
}
