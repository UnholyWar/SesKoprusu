using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SesKoprusu;

internal sealed class AccountServer : IDisposable
{
    readonly Process process;
    int sequence;
    AccountServer(Process child) => process = child;

    public static async Task<AccountServer> Connect(CancellationToken token)
    {
        var info = CodexSession.StartInfo();
        info.RedirectStandardInput = true;
        info.StandardInputEncoding = new System.Text.UTF8Encoding(false);
        info.ArgumentList.Add("app-server");
        info.ArgumentList.Add("--listen");
        info.ArgumentList.Add("stdio://");
        var server = new AccountServer(Process.Start(info) ?? throw new IOException("App Server başlatılamadı."));
        server.process.ErrorDataReceived += (_, _) => { };
        server.process.BeginErrorReadLine();
        try
        {
            await server.Call("initialize", new { clientInfo = new { name = "seskoprusu", title = "SesKöprüsü", version = "1.0" }, capabilities = new { } }, token);
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

    async Task<JsonElement> Read(CancellationToken token)
    {
        var line = await process.StandardOutput.ReadLineAsync(token) ?? throw new IOException("App Server bağlantısı kesildi.");
        using var json = JsonDocument.Parse(line);
        return json.RootElement.Clone();
    }

    public async Task<JsonElement> Call(string method, object parameters, CancellationToken token)
    {
        var id = ++sequence;
        await Send(new { id, method, @params = parameters });
        while (true)
        {
            var message = await Read(token);
            if (!message.TryGetProperty("id", out var reply) || !reply.TryGetInt32(out var n) || n != id) continue;
            if (message.TryGetProperty("error", out _)) throw new IOException($"App Server isteği başarısız ({method}). Codex sürümünü ve hesap izinlerini kontrol edin.");
            return message.GetProperty("result");
        }
    }

    public async Task Login(Action<string, Uri> show, CancellationToken token)
    {
        var start = await Call("account/login/start", new { type = "chatgptDeviceCode" }, token);
        var id = start.GetProperty("loginId").GetString();
        try
        {
            var url = new Uri(start.GetProperty("verificationUrl").GetString()!);
            if (url.Scheme != "https" || url.Host is not ("auth.openai.com" or "chatgpt.com") || !url.IsDefaultPort || url.UserInfo.Length != 0)
                throw new IOException("ChatGPT doğrulama adresi doğrulanamadı.");
            show(start.GetProperty("userCode").GetString()!, url);
            while (true)
            {
                var message = await Read(token);
                if (!message.TryGetProperty("method", out var method) || method.GetString() != "account/login/completed") continue;
                var result = message.GetProperty("params");
                if (result.GetProperty("loginId").GetString() != id) continue;
                if (!result.GetProperty("success").GetBoolean()) throw new IOException("Giriş tamamlanmadı. Yeni kodla tekrar deneyin.");
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

    public void Dispose()
    {
        try { if (!process.HasExited) process.Kill(true); }
        finally { process.Dispose(); }
    }
}
