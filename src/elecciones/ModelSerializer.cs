using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MenosRelato;

public static class ModelSerializer
{
    public static async Task<Election?> DeserializeAsync(string path)
    {
        using var stream = File.OpenRead(path);
        if (Path.GetExtension(path) == ".gz")
        {
            using var zip = new GZipStream(stream, CompressionMode.Decompress);
            return await DeserializeAsync(zip);
        }
        else
        {
            return await DeserializeAsync(stream);
        }
    }

    static async Task<Election?> DeserializeAsync(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var settings = new JsonSerializerOptions
        {
            Converters = { new ReusableStringConverter() },
        };

        return await JsonSerializer.DeserializeAsync<Election>(stream, settings);
    }

    public static async Task SerializeAsync(Election election, string path)
    {
        using var stream = File.Create(path);
        if (Path.GetExtension(path) == ".gz")
            await SerializeAsync(election, stream);
        else
            await SerializeAsync(election, stream);
    }

    static async Task SerializeAsync(Election election, Stream stream) => await JsonSerializer.SerializeAsync(stream, election, new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // We're zipping anyway, indenting won't take much extra
        WriteIndented = true,
    });

    class ReusableStringConverter : JsonConverter<string>
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

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => throw new NotImplementedException();
    }
}
