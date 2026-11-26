using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace SesKoprusu;

internal record SetupUpdate(Version Version, Uri Url, string Digest);

internal static class SetupUpdates
{
    const string Repo = "UnholyWar/SesKoprusu";
    const string Prefix = "setup-test-v";
    const string Asset = "SesKoprusu-Setup.exe";
    public static Version Current => typeof(SetupUpdates).Assembly.GetName().Version!;

    public static async Task<SetupUpdate?> Check(HttpClient http, CancellationToken token)
    {
        SetupUpdate? newest = null;
        for (int page = 1; ; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{Repo}/releases?per_page=100&page={page}");
            request.Headers.UserAgent.ParseAdd("SesKoprusu-Updater/1.0");
            using var response = await http.SendAsync(request, token);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            foreach (var release in json.RootElement.EnumerateArray())
            {
                var tag = release.GetProperty("tag_name").GetString() ?? "";
                if (release.GetProperty("draft").GetBoolean() || !tag.StartsWith(Prefix, StringComparison.Ordinal) ||
                    !Version.TryParse(tag[Prefix.Length..], out var version) || version <= Current ||
                    (newest != null && version <= newest.Version)) continue;
                foreach (var asset in release.GetProperty("assets").EnumerateArray())
                {
                    if (asset.GetProperty("name").GetString() != Asset) continue;
                    var url = $"https://github.com/{Repo}/releases/download/{tag}/{Asset}";
                    var digest = asset.GetProperty("digest").GetString();
                    if (asset.GetProperty("browser_download_url").GetString() != url || digest?.Length != 71 ||
                        !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid setup release metadata.");
                    newest = new(version, new Uri(url), digest[7..]);
                }
            }
            if (json.RootElement.GetArrayLength() < 100) return newest;
        }
    }

    public static async Task<string> Download(HttpClient http, SetupUpdate update, Action<double?> progress, CancellationToken token)
    {
        var folder = Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SesKoprusu", "updates")).FullName;
        var file = Path.Combine(folder, $"Setup-{update.Version}.exe");
        var temp = Path.Combine(folder, Guid.NewGuid() + ".part");
        try
        {
            using var response = await http.GetAsync(update.Url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true))
            {
                await Downloads.Copy(response.Content, stream, progress, token);
                stream.Position = 0;
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
                if (!hash.Equals(update.Digest, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Setup integrity check failed. Please retry.");
            }
            File.Move(temp, file, true);
            return file;
        }
        finally { File.Delete(temp); }
    }
}
