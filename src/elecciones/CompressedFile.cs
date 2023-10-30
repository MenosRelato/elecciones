using System.Diagnostics;
using System.IO.Compression;

namespace MenosRelato;

public static class GzipFile
{
    public static async Task<string> ReadAllTextAsync(string path)
    {
        Debug.Assert(File.Exists(path));
        using var stream = File.Open(path, FileMode.Open);
        using var zip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(zip);
        return await reader.ReadToEndAsync();
    }

    public static async Task WriteAllTextAsync(string path, string? contents)
    {
        using var stream = File.Create(path);
        using var zip = new GZipStream(stream, CompressionLevel.Optimal);
        using var writer = new StreamWriter(zip);
        await writer.WriteAsync(contents);
    }
}
