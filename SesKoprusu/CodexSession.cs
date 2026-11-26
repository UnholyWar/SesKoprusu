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

    public static async Task<bool> SignedIn(CancellationToken token)
    {
        using var server = await AccountServer.Connect(token);
        return await server.HasChatGptAccount(token);
    }

}
