using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Spectre.Console;
using Spectre.Console.Cli;
using Superpower;
using static MenosRelato.Commands.PrepareCommand;
using static MenosRelato.ConsoleExtensions;
using static Spectre.Console.AnsiConsole;

namespace MenosRelato.Commands;

[Description("Crear un subset de datos para uno o mas distritos, en formato JSON o CSV.")]
public class SliceCommand(ICommandApp app) : AsyncCommand<SliceCommand.Settings>
{
    public enum Format
    {
        Json,
        Csv
    }

    public class Settings : ElectionSettings
    {
        [CommandOption("-d|--district <VALUES>")]
        [Description("Distrito(s) a incluir en el subset")]
        public int[] Districts { get; set; } = [];

        [CommandOption("-o|--output")]
        [Description("Archivo de salida con el subset de datos. Por defecto 'slice.json' (o '.csv')")]
        public string Output { get; set; } = "slice.json";

        [CommandOption("-f|--format")]
        [FormatDescription()]
        [DefaultValue(Format.Json)]
        public Format Format { get; set; } = Format.Json;

        public override ValidationResult Validate()
        {
            if (Districts.Length == 0)
                return ValidationResult.Error("Debe especificar al menos un distrito.");

            return base.Validate();
        }

        [AttributeUsage(AttributeTargets.All)]
        public class FormatDescriptionAttribute : DescriptionAttribute
        {
            public override string Description => "Formato del archivo a generar. Opciones: " + string.Join(", ", Enum.GetNames(typeof(Format)));
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var districts = settings.Districts.ToHashSet();

        if (settings.Format == Format.Json)
        {
            var file = Path.Combine(settings.BaseDir, Constants.ResultsFile);

            if (!File.Exists(file))
                await app.RunAsync(["download", "-r"]);


            var election = await ModelSerializer.DeserializeAsync(Path.Combine(settings.BaseDir, Constants.ResultsFile));
            if (election is null)
                return -1;

            foreach (var district in election.Districts.ToArray())
            {
                if (!districts.Contains(district.Id))
                    election.Districts.Remove(district);
            }

            await ModelSerializer.SerializeAsync(election, settings.Output);

            return 0;
        }

        var csvDir = Path.Combine(settings.BaseDir, "dataset", "csv");
        var results = Directory.EnumerateFiles(csvDir, "*.csv").FirstOrDefault(x => Path.GetFileName(x).StartsWith("ResultadosElectorales"));
        if (results is null)
        {
            await app.RunAsync(["dataset"]);

            results = Directory.EnumerateFiles(csvDir, "*.csv").FirstOrDefault(x => Path.GetFileName(x).StartsWith("ResultadosElectorales"));
            if (results is null)
                return Error("No se encontraron resultados electorales a cargar.");
        }

        if (Path.GetExtension(Path.GetFileName(settings.Output)) is ".json")
            settings.Output = Path.ChangeExtension(settings.Output, ".csv");


        var total = await Status().StartAsync("Contando votos", async ctx =>
        {
            long total = -1; // discount header
            await foreach (var line in File.ReadLinesAsync(results!, Encoding.UTF8))
            {
                total++;
                ctx.Status = $"Contando votos {total:N0}";
            }
            return total;
        });

        await Progress()
            .AutoClear(false)
            .Columns(
            [
                new TaskDescriptionColumn { Alignment = Justify.Left },
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn(),
            ]).StartAsync(async ctx =>
            {
                var first = true;
                var header = Array.Empty<string>();
                var task = ctx.AddTask("Procesando votos", new() { MaxValue = total });

                using var output = File.Create(settings.Output);
                using var writer = new StreamWriter(output, Encoding.UTF8);

                await foreach (var line in File.ReadLinesAsync(results, Encoding.UTF8))
                {
                    if (first)
                    {
                        first = false;
                        header = CsvSerializer.LineParser.Parse(line).Select(x => x.Trim('"')).ToArray();
                        await writer.WriteLineAsync(line);
                        continue;
                    }

                    task.Increment(1);
                    var values = CsvSerializer.LineParser.Parse(line);
                    var value = CsvSerializer.Deserialize<BallotCsv>(header, line);
                    Debug.Assert(value != null);

                    if (!districts.Contains(value.DistrictId))
                        continue;

                    await writer.WriteLineAsync(line);
                }
            });

        return 0;
    }
}
