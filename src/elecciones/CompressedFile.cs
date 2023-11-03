using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using NuGet.Protocol.Plugins;
using Spectre.Console;

namespace MenosRelato;

public static class JsonFile
{
    static readonly JsonSerializerOptions options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task<string> ReadAllTextAsync(string fileName, bool compressed = false)
    {
        if (compressed)
            return await GzipFile.ReadAllTextAsync(fileName + ".gz");

        return await File.ReadAllTextAsync(fileName);
    }

    public static async Task WriteAllTextAsync(string fileName, string? contents, bool compressed = false)
    {
        if (compressed)
            await GzipFile.WriteAllTextAsync(fileName + ".gz", contents);
        else
            await File.WriteAllTextAsync(fileName, contents);
    }

    public static async Task SerializeAsync<T>(T value, string fileName, bool compressed = true)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetDirectoryName(fileName))!);

            if (compressed)
            {
                using var json = File.Create(fileName + ".gz");
                using var gzip = new GZipStream(json, CompressionLevel.Optimal);
                await JsonSerializer.SerializeAsync(gzip, value, options);
            }
            else
            {
                using var json = File.Create(fileName);
                await JsonSerializer.SerializeAsync(json, value, options);
            }
        }
        catch (IOException e)
        {
            AnsiConsole.MarkupLine($"[red]x[/] No se pudo guardar el archivo {fileName}: {e.Message}");
            Debug.Fail($"No se pudo guardar el archivo {fileName}: {e.Message}", e.Message);
        }
    }
}

public static class GzipFile
{
    public static async Task<string> ReadAllTextAsync(string path)
    {
        Debug.Assert(File.Exists(path));
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
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
