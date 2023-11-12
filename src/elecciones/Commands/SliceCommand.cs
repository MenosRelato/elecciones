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
        [Description("Distrito(s) a incluir en el subset.")]
        public int[] Districts { get; set; } = [];

        [CommandOption("-o|--output")]
        [Description("Directorio de salida para los archivos de cada distrito.")]
        public string Output { get; set; } = Path.Combine(Constants.DefaultCacheDir, "dataset", "slice");

        [CommandOption("-f|--format")]
        [FormatDescription()]
        [DefaultValue(Format.Json)]
        public Format Format { get; set; } = Format.Json;

        [AttributeUsage(AttributeTargets.All)]
        public class FormatDescriptionAttribute : DescriptionAttribute
        {
            public override string Description => "Formato del archivo a generar. Opciones: " + string.Join(", ", Enum.GetNames(typeof(Format)));
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var districts = settings.Districts.ToHashSet();

        bool ShouldRemove(int district) => districts?.Count > 0 && !districts.Contains(district);

        Directory.CreateDirectory(settings.Output);

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
                if (!ShouldRemove(district.Id))
                {
                    await ModelSerializer.SerializeAsync(district, Path.Combine(settings.Output, district.Id.ToString("D2") + ".json"));
                }
            }

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
                var headerline = "";
                var task = ctx.AddTask("Procesando votos", new() { MaxValue = total });

                //using var output = File.Create(settings.Output);
                //using var writer = new StreamWriter(output, Encoding.UTF8);

                await foreach (var line in File.ReadLinesAsync(results, Encoding.UTF8))
                {
                    if (first)
                    {
                        first = false;
                        header = CsvSerializer.LineParser.Parse(line).Select(x => x.Trim('"')).ToArray();
                        headerline = line;
                        continue;
                    }

                    task.Increment(1);
                    var values = CsvSerializer.LineParser.Parse(line);
                    var value = CsvSerializer.Deserialize<BallotCsv>(header, line);
                    Debug.Assert(value != null);

                    if (ShouldRemove(value.DistrictId))
                        continue;

                    var file = Path.Combine(settings.Output, value.DistrictId.ToString("D2") + ".csv");
                    if (!File.Exists(file))
                        await File.WriteAllTextAsync(file, headerline, Encoding.UTF8);

                    await File.AppendAllTextAsync(file, line + Environment.NewLine, Encoding.UTF8);
                }
            });

        return 0;
    }
}
