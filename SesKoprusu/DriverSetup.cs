using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace SesKoprusu;

internal static class DriverSetup
{
    static (string Zip, string Installer) Package(string cable) => cable switch
    {
        "CABLE" => ("VBCABLE_Driver_Pack46.zip", "VBCABLE_Setup_x64.exe"),
        Routing.HiFi => ("HiFiCableAsioBridgeSetup_v1007.zip", "HiFiCableAsioBridgeSetup.exe"),
        _ => throw new ArgumentOutOfRangeException(nameof(cable))
    };

    static string Cache(string name) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SesKoprusu", "drivers", name == "CABLE" ? "VB-CABLE-46.zip" : Package(name).Zip);
    static string Bundled => Path.Combine(AppContext.BaseDirectory, "drivers", "VB-CABLE", "VBCABLE_Setup_x64.exe");
    public static bool HasPackage(string name) => File.Exists(Cache(name)) || (name == "CABLE" && File.Exists(Bundled));

    public static async Task Install(HttpClient http, string cable, string? selectedZip, Action<string,double?> report, CancellationToken token)
    {
        var bundled = Bundled;
        if (selectedZip == null && cable == "CABLE" && File.Exists(bundled))
        {
            await RunInstaller(bundled, report, token);
            return;
        }
        var stage = Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SesKoprusu", "drivers", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var zip = Path.Combine(stage, "driver.zip");
            var cached = Cache(cable);
            var package = Package(cable);
            if (selectedZip != null) File.Copy(selectedZip, zip);
            else if (File.Exists(cached)) File.Copy(cached, zip);
            else
            {
                var label = $"Downloading VB-Audio {cable}…";
                report(label, null);
                using var response = await http.GetAsync($"https://download.vb-audio.com/Download_CABLE/{package.Zip}", HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                await using var file = File.Create(zip);
                await Downloads.Copy(response.Content, file, p => report(label, p), token);
            }
            report("Preparing driver files…", null);
            var extracted = Path.Combine(stage, "package");
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, extracted), token);
            var name = package.Installer;
            var exe = Directory.EnumerateFiles(extracted, name, SearchOption.AllDirectories).SingleOrDefault()
                ?? throw new InvalidDataException($"Select the official {package.Zip} for {cable}. The selected archive does not contain {name}.");
            if (selectedZip != null || !File.Exists(cached)) File.Move(zip, cached, overwrite: true);
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
