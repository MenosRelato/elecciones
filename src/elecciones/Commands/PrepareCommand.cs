using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Spectre.Console;
using Spectre.Console.Cli;
using Superpower;
using static MenosRelato.Results;
using static Spectre.Console.AnsiConsole;

namespace MenosRelato.Commands;

[Description("Preparar el dataset completo de resultados en formato JSON")]
public class PrepareCommand(ICommandApp app) : AsyncCommand<PrepareCommand.Settings>
{
    public class Settings : ElectionSettings
    {
        [CommandOption("-c|--count", IsHidden = true)]
        [Description("Total de votos a cargar")]
        public long? Count { get; set; }
    }

    //"año","distrito_id","distrito_nombre","seccionprovincial_id","seccionprovincial_nombre","seccion_id","seccion_nombre"
    public record ElectoralSectionCsv(
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
    public record BallotCsv(
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
        [property: JsonPropertyName("mesa_id")] int Station,
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
        var csvDir = Path.Combine(settings.BaseDir, "dataset", "csv");

        if (!Directory.Exists(csvDir) ||
            Directory.EnumerateFiles(csvDir, "*.csv").FirstOrDefault(x => Path.GetFileName(x).StartsWith("AmbitosElectorales")) is not { } ambitos ||
            Directory.EnumerateFiles(csvDir, "*.csv").FirstOrDefault(x => Path.GetFileName(x).StartsWith("ResultadosElectorales")) is not { } results)
        {
            if (Prompt(new SelectionPrompt<string>()
                    .Title("Continuar procesando?")
                    .AddChoices(["Si", "No"])) == "No")
                return Error("No hay dataset descargado a procesar.");
            else if (await app.RunAsync(["dataset", "-y", settings.Year.ToString(), "-e", settings.Election]) is var exit && exit < 0)
                return exit;
            else
            {
                ambitos = Directory.EnumerateFiles(csvDir, "*.csv").FirstOrDefault(x => Path.GetFileName(x).StartsWith("AmbitosElectorales"));
                if (ambitos == null)
                    return Error("No se encontraron ambitos electorales a cargar.");

                results = Directory.EnumerateFiles(csvDir, "*.csv").FirstOrDefault(x => Path.GetFileName(x).StartsWith("ResultadosElectorales"));
                if (results == null)
                    return Error("No se encontraron resultados electorales a cargar.");
            }
        }

        var es = CultureInfo.GetCultureInfo("es-AR");
        var first = true;
        var header = Array.Empty<string>();
        var election = new Election(settings.Year, settings.Election);

        await foreach (var line in File.ReadLinesAsync(ambitos, Encoding.UTF8))
        {
            if (first)
            {
                first = false;
                header = CsvSerializer.LineParser.Parse(line).Select(x => x.Trim('"')).ToArray();
                continue;
            }

            var values = CsvSerializer.LineParser.Parse(line);
            var value = CsvSerializer.Deserialize<ElectoralSectionCsv>(header, line);
            Debug.Assert(value != null);

            election
                .GetOrAddDistrict(value.DistrictId, value.DistrictName)
                .GetOrAddSection(value.SectionId, value.SectionName);
        }

        var total = await Status().StartAsync("Cargando votos", async ctx =>
        {
            long total = -1; // discount header
            if (settings.Count == null)
            {
                await foreach (var line in File.ReadLinesAsync(results!, Encoding.UTF8))
                {
                    total++;
                    ctx.Status = $"Contando votos {total:N0}";
                }
            }
            else
            {
                total = settings.Count.Value;
            }

            return total;
        });

        await Progress()
            .AutoClear(false)
            .Columns(
            [
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn(),
            ])
            .StartAsync(async ctx =>
            {
                first = true;
                var votes = 0;
                var task = ctx.AddTask("Cargando votos", new ProgressTaskSettings { MaxValue = total });
                await foreach (var line in File.ReadLinesAsync(results, Encoding.UTF8))
                {
                    if (first)
                    {
                        first = false;
                        header = CsvSerializer.LineParser.Parse(line).Select(x => x.Trim('"')).ToArray();
                        continue;
                    }

                    task.Increment(1);

                    var values = CsvSerializer.LineParser.Parse(line);
                    var value = CsvSerializer.Deserialize<BallotCsv>(header, line);
                    Debug.Assert(value != null);

                    // Always add the polling station, even if there may be no votes
                    var station = election
                        .GetOrAddDistrict(value.DistrictId, value.DistrictName)
                        .GetOrAddSection(value.SectionId, value.SectionName)
                        .GetOrAddCircuit(value.CircuitId, value.CircuitName)
                        .GetOrAddStation(value.Station, value.Electors);

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

                    var party = election.GetOrAddParty(value.PartyName);
                    var list = party?.AddList(value.ListName);

                    station.GetOrAddBallot(
                        ballotKind, value.Count,
                        election.GetOrAddPosition(value.PositionId, value.PositionName).Name,
                        party?.Name, list);

                    votes++;
                    if (votes == settings.Count)
                    {
                        election.Districts.Where(d => !d.GetBallots().Any()).ToList().ForEach(x => election.Districts.Remove(x));
                        foreach (var district in election.Districts)
                        {
                            district.Sections.Where(x => !x.GetBallots().Any()).ToList().ForEach(x => district.Sections.Remove(x));
                            foreach (var section in district.Sections)
                            {
                                section.Circuits.Where(x => !x.GetBallots().Any()).ToList().ForEach(x => section.Circuits.Remove(x));
                                foreach (var circuit in section.Circuits)
                                {
                                    circuit.Stations.Where(x => !x.Ballots.Any()).ToList().ForEach(x => circuit.Stations.Remove(x));
                                }
                            }
                        };
                        break;
                    }
                }
            });

        await ModelSerializer.SerializeAsync(election, Path.Combine(settings.BaseDir, "election.json.gz"));

        return Result(0, "Done");
    }
}
