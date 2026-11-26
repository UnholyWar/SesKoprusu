using System.Diagnostics;
using System.IO;
using System.Text;

namespace SesKoprusu;

internal static class CodexSession
{
    static async Task<(int Code, string Output, string Error)> Run(string[] args, CancellationToken token, string? prompt = null, bool interactive = false)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SesKoprusu");
        var work = Directory.CreateDirectory(Path.Combine(root, "session")).FullName;
        var exe = Path.Combine(AppContext.BaseDirectory, "codex.exe");
        if (!File.Exists(exe)) throw new FileNotFoundException("Codex bileşeni bulunamadı. SesKöprüsü kurulumunu yenileyin.");
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, WorkingDirectory = work, CreateNoWindow = !interactive,
            RedirectStandardOutput = !interactive, RedirectStandardError = !interactive, RedirectStandardInput = prompt != null
        };
        info.Environment["CODEX_HOME"] = Directory.CreateDirectory(Path.Combine(root, "codex")).FullName;
        foreach (var name in new[] { "OPENAI_API_KEY", "CODEX_API_KEY", "CODEX_ACCESS_TOKEN" }) info.Environment.Remove(name);
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("forced_login_method=\"chatgpt\"");
        foreach (var arg in args) info.ArgumentList.Add(arg);
        if (prompt != null) info.StandardInputEncoding = new UTF8Encoding(false);
        if (!interactive) info.StandardOutputEncoding = Encoding.UTF8;
        using var process = Process.Start(info) ?? throw new IOException("Codex başlatılamadı.");
        using var cancel = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var output = interactive ? Task.FromResult("") : process.StandardOutput.ReadToEndAsync();
        var error = interactive ? Task.FromResult("") : process.StandardError.ReadToEndAsync();
        if (prompt != null) { await process.StandardInput.WriteAsync(prompt); process.StandardInput.Close(); }
        await process.WaitForExitAsync(token);
        return (process.ExitCode, await output, await error);
    }

    public static async Task<bool> SignedIn(CancellationToken token)
    {
        var status = await Run(["login", "status"], token);
        return status.Code == 0 && (status.Output + status.Error).Contains("Logged in using ChatGPT", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task Login(CancellationToken token)
    {
        var result = await Run(["login", "--device-auth"], token, interactive: true);
        if (result.Code != 0) throw new InvalidOperationException("Giriş tamamlanmadı. Hesap izinlerini kontrol edip tekrar deneyin.");
    }

    public static async Task<string> Ask(string prompt, CancellationToken token)
    {
        var args = new List<string> { "exec", "--sandbox", "read-only", "--ephemeral", "--skip-git-repo-check" };
        if (Environment.GetEnvironmentVariable("SESKOPRUSU_CODEX_MODEL") is { Length: > 0 } model) args.AddRange(["--model", model]);
        args.Add("-");
        var result = await Run(args.ToArray(), token, prompt);
        if (result.Code != 0 || string.IsNullOrWhiteSpace(result.Output)) throw new IOException("Codex yanıt vermedi. Oturumu ve internet bağlantısını kontrol edin.");
        return result.Output.Trim();
    }
}
