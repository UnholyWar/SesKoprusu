using System.IO;
using System.Net.Http;

namespace SesKoprusu;

internal static class Downloads
{
    public static async Task Copy(HttpContent content, Stream destination, Action<double?> report, CancellationToken token)
    {
        var length = content.Headers.ContentLength;
        var buffer = new byte[81920];
        long total = 0;
        long nextUpdate = 0;
        await using var input = await content.ReadAsStreamAsync(token);
        int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, count), token);
            total += count;
            if (Environment.TickCount64 >= nextUpdate)
            {
                report(length > 0 ? 100d * total / length.Value : null);
                nextUpdate = Environment.TickCount64 + 120;
            }
        }
        if (length.HasValue && total != length.Value) throw new EndOfStreamException("Download interrupted. Please retry.");
        report(100);
    }
}
