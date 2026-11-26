using System.Diagnostics;
using System.IO;
using System.Text;

namespace SesKoprusu;

internal static class CodexSession
{
    public static string? FindCli()
    {
        if (File.Exists(CliInstaller.Executable)) return CliInstaller.Executable;
        var bundled = Path.Combine(AppContext.BaseDirectory, "codex.exe");
        if (File.Exists(bundled)) return bundled;
        return null;
    }

    internal static ProcessStartInfo StartInfo()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SesKoprusu");
        var work = Directory.CreateDirectory(Path.Combine(root, "session")).FullName;
        var exe = FindCli() ?? throw new InvalidOperationException("Open Codex Settings to install the CLI.");
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, WorkingDirectory = work, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8
        };
        info.Environment["CODEX_HOME"] = Directory.CreateDirectory(Path.Combine(root, "codex")).FullName;
        foreach (var name in new[] { "OPENAI_API_KEY", "CODEX_API_KEY", "CODEX_ACCESS_TOKEN" }) info.Environment.Remove(name);
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("forced_login_method=\"chatgpt\"");
        return info;
    }

    static async Task<(int Code, string Output, string Error)> Run(string[] args, CancellationToken token, string? prompt = null)
    {
        var info = StartInfo();
        info.RedirectStandardInput = prompt != null;
        foreach (var arg in args) info.ArgumentList.Add(arg);
        if (prompt != null) info.StandardInputEncoding = new UTF8Encoding(false);
        using var process = Process.Start(info) ?? throw new IOException("Could not start Codex.");
        using var cancel = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (prompt != null) { await process.StandardInput.WriteAsync(prompt); process.StandardInput.Close(); }
        await process.WaitForExitAsync(token);
        return (process.ExitCode, await output, await error);
    }

    public static async Task<bool> SignedIn(CancellationToken token)
    {
        using var server = await AccountServer.Connect(token);
        return await server.HasChatGptAccount(token);
    }

    public static async Task<string> Ask(string prompt, CancellationToken token)
    {
        var args = new List<string> { "exec", "--sandbox", "read-only", "--ephemeral", "--skip-git-repo-check" };
        if (Environment.GetEnvironmentVariable("SESKOPRUSU_CODEX_MODEL") is { Length: > 0 } model) args.AddRange(["--model", model]);
        args.Add("-");
        var result = await Run(args.ToArray(), token, prompt);
        if (result.Code != 0 || string.IsNullOrWhiteSpace(result.Output)) throw new IOException("Codex did not respond. Check your session and network connection.");
        return result.Output.Trim();
    }
}
