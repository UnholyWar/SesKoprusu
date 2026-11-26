using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SesKoprusu;

internal sealed class AccountServer : IDisposable
{
    readonly Process process;
    int sequence;
    readonly Queue<JsonElement> pendingAudio = new();
    bool streaming;
    AccountServer(Process child) => process = child;

    public static async Task<AccountServer> Connect(CancellationToken token, bool realtime = false)
    {
        var info = CodexSession.StartInfo();
        info.RedirectStandardInput = true;
        info.StandardInputEncoding = new System.Text.UTF8Encoding(false);
        info.ArgumentList.Add("app-server");
        info.ArgumentList.Add("--listen");
        info.ArgumentList.Add("stdio://");
        var server = new AccountServer(Process.Start(info) ?? throw new IOException("Could not start App Server."));
        server.process.ErrorDataReceived += (_, _) => { };
        server.process.BeginErrorReadLine();
        try
        {
            await server.Call("initialize", new { clientInfo = new { name = "seskoprusu", title = "SesKöprüsü", version = "1.0" }, capabilities = new { experimentalApi = realtime } }, token);
            await server.Send(new { method = "initialized" });
            return server;
        }
        catch { server.Dispose(); throw; }
    }

    async Task Send(object message)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message));
        await process.StandardInput.FlushAsync();
    }

    internal Task<JsonElement> NextEvent(CancellationToken token) =>
        pendingAudio.TryDequeue(out var item) ? Task.FromResult(item) : Read(token);

    async Task<JsonElement> Read(CancellationToken token)
    {
        var line = await process.StandardOutput.ReadLineAsync(token) ?? throw new IOException("App Server disconnected.");
        using var json = JsonDocument.Parse(line);
        return json.RootElement.Clone();
    }

    internal Task AppendAudio(string threadId, byte[] pcm) => Send(new { id = ++sequence, method = "thread/realtime/appendAudio", @params = new { threadId, audio = new { data = Convert.ToBase64String(pcm), sampleRate = 24000, numChannels = 1 } } });

    public async Task<JsonElement> Call(string method, object parameters, CancellationToken token)
    {
        if (method == "thread/realtime/start") streaming = true;
        var id = ++sequence;
        await Send(new { id, method, @params = parameters });
        while (true)
        {
            var message = await Read(token);
            if (!message.TryGetProperty("id", out var reply) || !reply.TryGetInt32(out var n) || n != id)
            {
                if (streaming && message.TryGetProperty("method", out var name) && name.GetString()?.StartsWith("thread/realtime/") == true)
                    pendingAudio.Enqueue(message);
                continue;
            }
            if (message.TryGetProperty("error", out var error)) throw new IOException($"App Server request failed ({method}). {Detail(error, "message")}");
            return message.GetProperty("result");
        }
    }

    internal static string Detail(JsonElement payload, string field)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(field, out var value) ||
            value.ValueKind != JsonValueKind.String) return "No further details were provided.";
        var detail = value.GetString()!.Trim();
        return detail.Length > 400 ? detail[..400] : detail;
    }

    public async Task<bool> HasChatGptAccount(CancellationToken token, bool refreshToken = false)
    {
        var result = await Call("account/read", new { refreshToken }, token);
        return result.GetProperty("account") is { ValueKind: System.Text.Json.JsonValueKind.Object } account &&
               account.GetProperty("type").GetString() == "chatgpt";
    }

    public async Task Login(Action<string, Uri> show, CancellationToken token)
    {
        var start = await Call("account/login/start", new { type = "chatgptDeviceCode" }, token);
        var id = start.GetProperty("loginId").GetString();
        try
        {
            var url = new Uri(start.GetProperty("verificationUrl").GetString()!);
            if (url.Scheme != "https" || url.Host is not ("auth.openai.com" or "chatgpt.com") || !url.IsDefaultPort || url.UserInfo.Length != 0)
                throw new IOException("Could not verify the ChatGPT sign-in address.");
            show(start.GetProperty("userCode").GetString()!, url);
            while (true)
            {
                var message = await Read(token);
                if (!message.TryGetProperty("method", out var method) || method.GetString() != "account/login/completed") continue;
                var result = message.GetProperty("params");
                if (result.GetProperty("loginId").GetString() != id) continue;
                if (!result.GetProperty("success").GetBoolean()) throw new IOException("Sign-in was not completed. Try again with a fresh code.");
                return;
            }
        }
        catch (OperationCanceledException)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await Call("account/login/cancel", new { loginId = id }, timeout.Token); } catch { /* Closing the server also ends its pending flow. */ }
            throw;
        }
    }

    bool closed;
    public void Dispose()
    {
        if (closed) return;
        closed = true;
        try { if (!process.HasExited) process.Kill(true); }
        finally { process.Dispose(); }
    }
}
