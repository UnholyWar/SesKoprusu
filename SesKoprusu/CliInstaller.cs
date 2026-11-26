using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace SesKoprusu;

internal static class CliInstaller
{
    // Windows x64 is the setup target. Pin both release and digest.
    const string Release = "rust-v0.172.0";
    const string Digest = "3f42b12274b353ecb3ec555e6c8d0a43f6c6cb29c3a546179bc12dee4855deb6";
    static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SesKoprusu", "tools", Release);
    public static string Executable => Path.Combine(DirectoryPath, "codex.exe");

    public static async Task Ensure(HttpClient http, Action<double?> report, CancellationToken token)
    {
        if (CodexSession.FindCli() != null) return;
        Directory.CreateDirectory(DirectoryPath);
        var temp = Path.Combine(DirectoryPath, Guid.NewGuid() + ".part");
        try
        {
            using var response = await http.GetAsync($"https://github.com/openai/codex/releases/download/{Release}/codex-x86_64-pc-windows-msvc.exe", HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true))
            {
                await Downloads.Copy(response.Content, file, report, token);
                file.Position = 0;
                var digest = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
                if (!digest.Equals(Digest, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Codex download failed its integrity check.");
            }
            File.Move(temp, Executable, true);
        }
        finally { File.Delete(temp); }
    }
}
