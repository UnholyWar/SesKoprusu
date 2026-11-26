using System.IO;
using System.Text.Json;

namespace SesKoprusu;

internal static class Local
{
    static string PathFor(string name) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SesKoprusu", name + ".json");
    public static bool Has(string name) => File.Exists(PathFor(name));
    public static T Read<T>(string name) => JsonSerializer.Deserialize<T>(File.ReadAllText(PathFor(name))) ?? throw new InvalidDataException(name);
    public static void Save<T>(string name, T value)
    {
        var path = PathFor(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value));
        File.Move(path + ".tmp", path, true);
    }
    public static void Delete(string name) => File.Delete(PathFor(name));
}
