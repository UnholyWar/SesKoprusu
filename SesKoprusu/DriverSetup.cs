using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace SesKoprusu;

internal static class DriverSetup
{
    public static async Task Install(HttpClient http, string? licensedZip, Action<string,double?> report, CancellationToken token)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "drivers", "VB-CABLE", "VBCABLE_Setup_x64.exe");
        if (licensedZip == null && File.Exists(bundled))
        {
            await RunInstaller(bundled, report, token);
            return;
        }
        var stage = Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SesKoprusu", "drivers", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var zip = Path.Combine(stage, "driver.zip");
            if (licensedZip == null)
            {
                report("Downloading VB-CABLE…", null);
                using var response = await http.GetAsync("https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack46.zip", HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                await using var file = File.Create(zip);
                await Downloads.Copy(response.Content, file, p => report("Downloading VB-CABLE…", p), token);
            }
            else File.Copy(licensedZip, zip);
            report("Preparing driver files…", null);
            var extracted = Path.Combine(stage, "package");
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, extracted), token);
            var name = licensedZip == null ? "VBCABLE_Setup_x64.exe" : "VBCable_AB_PackSetup.exe";
            var exe = Directory.EnumerateFiles(extracted, name, SearchOption.AllDirectories).SingleOrDefault()
                ?? throw new InvalidDataException($"The driver package does not contain {name}.");
            await RunInstaller(exe, report, token);
        }
        finally { Directory.Delete(stage, true); }
    }

    static async Task RunInstaller(string exe, Action<string,double?> report, CancellationToken token)
    {
        report("Complete the VB-Audio installer and approve the Windows administrator prompt.", null);
        token.ThrowIfCancellationRequested();
        using var installer = Process.Start(new ProcessStartInfo(exe)
                { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(exe)! })
                ?? throw new IOException("The VB-Audio installer could not start.");
        await installer.WaitForExitAsync();
    }
}
