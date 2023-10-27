using System.Text.Json;
using Superpower.Parsers;
using Superpower;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MenosRelato;

public static class CsvSerializer
{
    static readonly JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static TextParser<string[]> LineParser { get; } =
        from value in QuotedString.CStyle.ManyDelimitedBy(Character.EqualTo(','))
        select value;

    public static T? Deserialize<T>(string[] header, string line)
    {
        var values = LineParser.Parse(line);
        Debug.Assert(values.Length == header.Length, "Header count doesn't match value count");

        var json = new JsonObject(header.Select((x, i) 
            => KeyValuePair.Create<string, JsonNode?>(x, JsonValue.Create(
                values[i].Trim('"') is var value && !string.IsNullOrEmpty(value) ? value : null))));

        return JsonSerializer.Deserialize<T>(json, options);
    }
}
