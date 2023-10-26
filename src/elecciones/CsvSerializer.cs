using System.Text.Json;
using Superpower.Parsers;
using Superpower;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace MenosRelato;

public static class CsvSerializer
{
    static readonly JsonSerializerOptions options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    static TextParser<string[]> lineParser =
        from value in QuotedString.CStyle.ManyDelimitedBy(Character.EqualTo(','))
        select value;

    public static T? Deserialize<T>(string[] header, string line)
    {
        var values = lineParser.Parse(line);
        Debug.Assert(values.Length == header.Length, "Header count doesn't match value count");

        var json = new JsonObject(header.Select((x, i) 
            => KeyValuePair.Create<string, JsonNode?>(x, JsonValue.Create(
                values[i].Trim('"') is var value && !string.IsNullOrEmpty(value) ? value : null))));

        return JsonSerializer.Deserialize<T>(json, options);
    }
}
