using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MenosRelato;

public static class ModelSerializer
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // We're zipping anyway, indenting won't take much extra
        WriteIndented = true,
    };

    public static Task<Election?> DeserializeAsync(string path) => DeserializeAsync<Election>(path);

    public static async Task<T?> DeserializeAsync<T>(string path)
    {
        using var stream = File.OpenRead(path);
        if (Path.GetExtension(path) == ".gz")
        {
            using var zip = new GZipStream(stream, CompressionMode.Decompress);
            return await DeserializeAsync<T>(zip);
        }
        else
        {
            return await DeserializeAsync<T>(stream);
        }
    }

    static async Task<T?> DeserializeAsync<T>(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var settings = new JsonSerializerOptions
        {
            // Reusable string MUST be short-lived.
            Converters = { new ReusableStringConverter() },
        };

        return await JsonSerializer.DeserializeAsync<T>(stream, settings);
    }

    public static async Task SerializeAsync<T>(T model, string path)
    {
        using var stream = File.Create(path);
        if (Path.GetExtension(path) == ".gz")
        {
            using var zip = new GZipStream(stream, CompressionLevel.SmallestSize);
            await SerializeAsync(model, zip);
        }
        else
        {
            await SerializeAsync(model, stream);
        }
    }

    static async Task SerializeAsync<T>(T model, Stream stream) => await JsonSerializer.SerializeAsync(stream, model, new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // We're zipping anyway, indenting won't take much extra
        WriteIndented = true,
    });

    public class ReusableStringConverter : JsonConverter<string>
    {
        readonly Dictionary<string, string> _items = new();

        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            var str = reader.GetString();
            if (str == null)
                return null;

            if (str.Length == 0)
                return string.Empty;

            if (_items.TryGetValue(str, out var item))
            {
                return item;
            }
            else
            {
                _items[str] = str;
                return str;
            }
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
    }

    public class DateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString()!;
            return DateTime.Parse(value);
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString("O"));
        }
    }
}
