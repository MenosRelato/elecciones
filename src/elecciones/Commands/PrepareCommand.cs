using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Humanizer;
using Spectre.Console;
using Spectre.Console.Cli;
using Superpower;
using static MenosRelato.Results;
using static Spectre.Console.AnsiConsole;

namespace MenosRelato.Commands;

[Description("Preparar el dataset completo de resultados en formato JSON.")]
internal class PrepareCommand(ICommandApp app) : AsyncCommand<PrepareCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("-y|--year")]
        [Description("Año de la eleccion a cargar")]
        [DefaultValue(2023)]
        public int Year { get; set; } = 2023;

        [CommandOption("-k|--kind")]
        [Description("Tipo de eleccion a cargar")]
        [DefaultValue(ElectionKind.General)]
        public ElectionKind Kind { get; set; } = ElectionKind.General;


        [CommandOption("-j|--json")]
        [Description("Crear un archivo JSON de texto legible")]
        [DefaultValue(false)]
        public bool Json { get; set; } = false;

        [CommandOption("-z|--zip")]
        [Description("Comprimir el JSON de texto legible con GZip")]
        [DefaultValue(false)]
        public bool Zip { get; set; } = false;

        [CommandOption("-c|--count", IsHidden = true)]
        [Description("Total de votos a cargar")]
        public long? Count { get; set; }
    }

    //"año","distrito_id","distrito_nombre","seccionprovincial_id","seccionprovincial_nombre","seccion_id","seccion_nombre"
    record ElectoralSection(
        [property: JsonPropertyName("año")] int Year,
        [property: JsonPropertyName("distrito_id")] int DistrictId,
        [property: JsonPropertyName("distrito_nombre")] string DistrictName,
        [property: JsonPropertyName("seccionprovincial_id")] int ProvincialId,
        [property: JsonPropertyName("seccionprovincial_nombre")] string ProvincialName,
        [property: JsonPropertyName("seccion_id")] int SectionId,
        [property: JsonPropertyName("seccion_nombre")] string SectionName);

    // "año","eleccion_tipo","recuento_tipo","padron_tipo",
    // "distrito_id","distrito_nombre","seccionprovincial_id","seccionprovincial_nombre","seccion_id","seccion_nombre",
    // "circuito_id","circuito_nombre","mesa_id","mesa_tipo","mesa_electores","cargo_id","cargo_nombre","agrupacion_id","agrupacion_nombre","lista_numero","lista_nombre",
    // "votos_tipo","votos_cantidad"
    record Ballot(
        [property: JsonPropertyName("año")] int Year,
        [property: JsonPropertyName("eleccion_tipo")] string Election,
        [property: JsonPropertyName("distrito_id")] int DistrictId,
        [property: JsonPropertyName("distrito_nombre")] string DistrictName,
        [property: JsonPropertyName("seccionprovincial_id")] int ProvincialId,
        [property: JsonPropertyName("seccionprovincial_nombre")] string ProvincialName,
        [property: JsonPropertyName("seccion_id")] int SectionId,
        [property: JsonPropertyName("seccion_nombre")] string SectionName,
        [property: JsonPropertyName("circuito_id")] string CircuitId,
        [property: JsonPropertyName("circuito_nombre")] string? CircuitName,
        [property: JsonPropertyName("mesa_id")] int Booth,
        [property: JsonPropertyName("mesa_electores")] int Electors,
        [property: JsonPropertyName("cargo_id")] int PositionId,
        [property: JsonPropertyName("cargo_nombre")] string PositionName,
        [property: JsonPropertyName("agrupacion_id")] int? PartyId,
        [property: JsonPropertyName("agrupacion_nombre")] string? PartyName,
        [property: JsonPropertyName("lista_numero")] int? ListId,
        [property: JsonPropertyName("lista_nombre")] string? ListName,
        [property: JsonPropertyName("votos_tipo")] string Kind,
        [property: JsonPropertyName("votos_cantidad")] int Count);

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (!Directory.Exists(Constants.CsvDir) ||
            Directory.EnumerateFiles(Constants.CsvDir, "*.csv").FirstOrDefault(x => Path.GetFileName(x).StartsWith("AmbitosElectorales")) is not { }  ambitos ||
            Directory.EnumerateFiles(Constants.CsvDir, "*.csv").FirstOrDefault(x => Path.GetFileName(x).StartsWith("ResultadosElectorales")) is not { } results)
        {
            if (Prompt(new SelectionPrompt<string>()
                    .Title("Continuar procesando?")
                    .AddChoices(["Si", "No"])) == "No")
                return Error("No hay dataset descargado a procesar.");
            else if (await app.RunAsync(["dowload"]) is var exit && exit < 0)
                return exit;
            else
            {
                ambitos = Directory.EnumerateFiles(Constants.CsvDir, "*.csv").FirstOrDefault(x => Path.GetFileName(x).StartsWith("AmbitosElectorales"));
                if (ambitos == null)
                    return Error("No se encontraron ambitos electorales a cargar.");

                results = Directory.EnumerateFiles(Constants.CsvDir, "*.csv").FirstOrDefault(x => Path.GetFileName(x).StartsWith("ResultadosElectorales"));
                if (results == null)
                    return Error("No se encontraron resultados electorales a cargar.");
            }
        }

        var es = CultureInfo.GetCultureInfo("es-AR");
        var first = true;
        var header = Array.Empty<string>();
        var election = new Election(settings.Year, settings.Kind);

        await foreach (var line in File.ReadLinesAsync(ambitos, Encoding.UTF8))
        {
            if (first)
            {
                first = false;
                header = CsvSerializer.LineParser.Parse(line).Select(x => x.Trim('"')).ToArray();
                continue;
            }

            var values = CsvSerializer.LineParser.Parse(line);
            var value = CsvSerializer.Deserialize<ElectoralSection>(header, line);
            Debug.Assert(value != null);

            election
                .GetOrAddDistrict(value.DistrictId, value.DistrictName)
                .GetOrAddProvincial(value.ProvincialId, value.ProvincialName)
                .GetOrAddSection(value.SectionId, value.SectionName);
        }

        await Status().StartAsync("Cargando votos", async ctx =>
        {
            first = true;
            long count = 0;
            long total = -1; // discount header
            if (settings.Count == null)
            {
                await foreach (var line in File.ReadLinesAsync(results, Encoding.UTF8))
                {
                    total++;
                    ctx.Status = $"Contando votos {total:N0}";
                }
            }
            else
            {
                total = settings.Count.Value;
            }

            var watch = Stopwatch.StartNew();
            await foreach (var line in File.ReadLinesAsync(results, Encoding.UTF8))
            {
                if (first)
                {
                    first = false;
                    header = CsvSerializer.LineParser.Parse(line).Select(x => x.Trim('"')).ToArray();
                    continue;
                }

#if DEBUG
                if (count == 10 &&
                    Prompt(new SelectionPrompt<string>()
                        .Title("Continuar procesando?")
                        .AddChoices(["Si", "No"])) == "No")
                {
                    election.Districts.RemoveAll(x => !x.GetBallots().Any());
                    election.Districts.ForEach(d =>
                    {
                        d.Provincials.Where(x => !x.GetBallots().Any()).ToList().ForEach(x => d.Provincials.Remove(x));
                        foreach (var p in d.Provincials)
                        {
                            p.Sections.Where(x => !x.GetBallots().Any()).ToList().ForEach(x => p.Sections.Remove(x));
                            foreach (var s in p.Sections)
                            {
                                s.Circuits.Where(x => !x.GetBallots().Any()).ToList().ForEach(x => s.Circuits.Remove(x));
                                foreach (var c in s.Circuits)
                                {
                                    c.Booths.Where(x => !x.Ballots.Any()).ToList().ForEach(x => c.Booths.Remove(x));
                                }
                            }
                        }
                    });
                    break;
                }
#endif

                count++;
                var remaining = TimeSpan.FromTicks(watch.Elapsed.Ticks * (total - count) / count);
                ctx.Status = $"Cargando votos {count:N0} de {total:N0} (faltan {remaining.Humanize(culture: es)})";

                var values = CsvSerializer.LineParser.Parse(line);
                var value = CsvSerializer.Deserialize<Ballot>(header, line);
                Debug.Assert(value != null);

                // Don't waste persistence with default value counts
                if (value.Count == 0)
                    continue;

                var electionKind = value.Election switch
                {
                    "PASO" => ElectionKind.Primary,
                    "GENERAL" => ElectionKind.General,
                    _ => throw new ArgumentException($"Unexpected election value {value.Election}"),
                };

                Debug.Assert(election.Kind == electionKind);

                var ballotKind = value.Kind switch
                {
                    "POSITIVO" => BallotKind.Positive,
                    "EN BLANCO" => BallotKind.Blank,
                    "NULO" => BallotKind.Null,
                    "IMPUGNADO" => BallotKind.Contested,
                    "RECURRIDO" => BallotKind.Appealed,
                    "COMANDO" => BallotKind.Command,
                    _ => throw new ArgumentException($"Unexpected ballot kind {value.Kind}"),
                };

                election
                    .GetOrAddDistrict(value.DistrictId, value.DistrictName)
                    .GetOrAddProvincial(value.ProvincialId, value.ProvincialName)
                    .GetOrAddSection(value.SectionId, value.SectionName)
                    .GetOrAddCircuit(value.CircuitId, value.CircuitName)
                    .GetOrAddBooth(value.Booth, value.Electors)
                    .GetOrAddBallot(
                        ballotKind, value.Count,
                        election.GetOrAddPosition(value.PositionId, value.PositionName).Id,
                        election.GetOrAddParty(value.PartyId, value.PartyName)?.Id,
                        election.GetOrAddParty(value.PartyId, value.PartyName)?.GetOrAddList(value.ListId, value.ListName)?.Id);
            }
        });

        var fileName = $"elecciones-{election.Year}-{election.Kind}";

        if (settings.Json)
        {
            var options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };

            if (settings.Zip)
            {
                using var json = File.Create(Path.Combine(Constants.DefaultCacheDir, fileName + ".json.gz"));
                using var zip = new GZipStream(json, CompressionLevel.Optimal);
                await JsonSerializer.SerializeAsync(zip, election, options);
            }
            else
            {
                using var json = File.Create(Path.Combine(Constants.DefaultCacheDir, fileName + ".json"));
                await JsonSerializer.SerializeAsync(json, election, options);
            }
        }

        // Always create the internal reference file for stats/processing.
        using (var stream = File.Create(Path.Combine(Constants.DefaultCacheDir, fileName + ".ref.gz")))
        {
            using var zip = new GZipStream(stream, CompressionLevel.Optimal);
            // For the GZip version, which we'll use to read and process, we want to preserve references 
            // to make it as small as possible.
            await JsonSerializer.SerializeAsync(zip, election, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
                ReferenceHandler = ReferenceHandler.Preserve,
            });
        }

        return Result(0, "Done");
    }
}
