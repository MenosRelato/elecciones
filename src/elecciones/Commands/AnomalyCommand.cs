using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CliWrap;
using CliWrap.Buffered;
using MathNet.Numerics.Statistics;
using Spectre.Console;
using Spectre.Console.Cli;
using static MenosRelato.ConsoleExtensions;
using static Spectre.Console.AnsiConsole;

namespace MenosRelato.Commands;

[Description("Detectar anomalias en los telegramas descargados.")]
public class AnomalyCommand(ICommandApp app) : AsyncCommand<AnomalyCommand.Settings>
{
    public class Settings : ElectionSettings
    {
        [CommandOption("-c|--clean", IsHidden = true)]
        public bool Clean { get; set; }

        [CommandOption("-p|--prepare", IsHidden = true)]
        public bool PrepareOnly { get; set; }

        [CommandOption("-s|--skip", IsHidden = true)]
        public int Skip { get; set; }

        [CommandOption("-r|--reset", IsHidden = true)]
        public bool Reset { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var path = ShellProcess.GetPath("jq");
        if (path is null)
            return Error("jq no esta instalado. Por favor instalarlo ejecutando: winget install jqlang.jq");

        var telegrams = new List<Telegram>();

        var baseDir = Path.Combine(settings.BaseDir, "telegram");
        if (!Directory.Exists(baseDir))
        {
            var result = await app.RunAsync(["telegram", "-d"]);
            if (result != 0)
                return Error("No se pudieron descargar los telegramas");
        }

        var electionFile = Path.Combine(settings.BaseDir, "election.json.gz");
        if (!File.Exists(electionFile))
        {
            var result = await app.RunAsync(["download", "-r"]);
            if (result != 0)
                return Error("No se pudieron descargar los resultados");
        }

        var options = new JsonSerializerOptions(ModelSerializer.Options)
        {
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new ModelSerializer.ReusableStringConverter(),
                new ModelSerializer.DateTimeConverter(),
            },
        };

        var statsDir = Path.Combine(settings.BaseDir, "stats");
        var progress = Progress()
            .AutoClear(false)
            .Columns(
            [
                new TaskDescriptionColumn { Alignment = Justify.Left },
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn(),
            ]);

        if (settings.Reset && Directory.Exists(statsDir))
            Directory.Delete(statsDir, true);

        if (settings.Clean)
        {
            // Remove stats for telegrams without a tiff
            var nontiff = Directory.EnumerateFiles(statsDir, "*.json.gz", SearchOption.AllDirectories)
                .Where(x =>
                {
                    var name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(x));
                    var district = int.Parse(name[..2]);
                    var section = int.Parse(name[2..5]);
                    return !File.Exists(Path.Combine(baseDir, district.ToString(), section.ToString(), name + ".tiff"));
                })
                .ToList();

            foreach (var item in nontiff)
                File.Delete(item);
        }

        var telegramFiles = Directory
            .EnumerateFiles(baseDir, "*.tiff", SearchOption.AllDirectories)
            .Select(x => Path.ChangeExtension(x, ".json.gz"));

        Directory.CreateDirectory(statsDir);

        var statsFiles = Directory.EnumerateFiles(statsDir, "*.json.gz", SearchOption.AllDirectories);

        if (!Directory.Exists(statsDir) || statsFiles.Count() != telegramFiles.Count())
        {
            await progress.StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Cargando telegramas", new ProgressTaskSettings { MaxValue = telegramFiles.Count() });
                var jq = Path.GetTempFileName();
                await File.WriteAllTextAsync(jq, EmbeddedResource.GetContent("jq.txt"));

                foreach (var file in telegramFiles)
                {
                    task.Increment(1);
                    if (task.Value <= settings.Skip)
                        continue;

                    var input = await TextFile.ReadAllTextAsync(file);
                    var cmd =
                        PipeSource.FromString(input, Encoding.UTF8) |
                        Cli.Wrap(path).WithArguments(["-f", jq]);

                    var result = await cmd.ExecuteBufferedAsync(Encoding.UTF8, Encoding.UTF8);
                    var data = result.StandardOutput;

                    await TextFile.WriteAllTextAsync(Path.Combine(statsDir, Path.GetFileNameWithoutExtension(file)), data, true);
                    var telegram = JsonSerializer.Deserialize<Telegram>(data, options);

                    Debug.Assert(telegram != null);
                    telegrams.Add(telegram);
                }

                task.Value = task.MaxValue;
                task.StopTask();
                task = ctx.AddTask("Optimizando estadisticas").IsIndeterminate(true);
                await JsonFile.SerializeAsync(telegrams, Path.Combine(settings.BaseDir, "telegrams.json.gz"));
                task.StopTask();
            });
        }
        else if (File.Exists(Path.Combine(settings.BaseDir, "telegrams.json.gz")))
        {
            await progress.StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Cargando telegramas").IsIndeterminate(true);
                telegrams = await ModelSerializer.DeserializeAsync<List<Telegram>>(Path.Combine(settings.BaseDir, "telegrams.json.gz"));
                task.StopTask();
            });
        }
        else
        {
            await progress.StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Cargando telegramas", new ProgressTaskSettings { MaxValue = statsFiles.Count() });

                foreach (var file in statsFiles)
                {
                    var data = await TextFile.ReadAllTextAsync(file);
                    var telegram = JsonSerializer.Deserialize<Telegram>(data, options);

                    Debug.Assert(telegram != null);
                    telegrams.Add(telegram);
                    task.Increment(1);
                }

                task.Value = task.MaxValue;
                task.StopTask();
                task = ctx.AddTask("Optimizando estadisticas").IsIndeterminate(true);
                await JsonFile.SerializeAsync(telegrams, Path.Combine(settings.BaseDir, "telegrams.json.gz"));
                task.StopTask();
            });
        }

        if (settings.Clean)
        {
            // Remove stats for telegrams without a tiff
            telegrams.RemoveAll(x =>
            {
                var district = int.Parse(x.Id[..2]);
                var section = int.Parse(x.Id[2..5]);
                return !File.Exists(Path.Combine(baseDir, district.ToString(), section.ToString(), x.Id + ".tiff"));
            });

            await JsonFile.SerializeAsync(telegrams, Path.Combine(settings.BaseDir, "telegrams.json.gz"));
        }

        if (settings.PrepareOnly)
            return 0;

        var anomalies = new HashSet<Telegram>(EqualityComparer<Telegram>.Create((x, y) => x?.Id == y?.Id, x => x.Id.GetHashCode()));
        void AddAnomalies(IEnumerable<Telegram> telegrams)
        {
            foreach (var telegram in telegrams)
                anomalies?.Add(telegram);
        }

        foreach (var district in telegrams.Where(x => x.TelegramUrl != null).GroupBy(x => x.District))
        {
            var dstats = Stats.Calculate(district);
            await ModelSerializer.SerializeAsync(dstats, Path.Combine(statsDir, $"{district.Key.Id:D2}.json"));

            foreach (var section in district.GroupBy(x => x.Section))
            {
                var sstats = Stats.Calculate(section);
                await ModelSerializer.SerializeAsync(dstats, Path.Combine(statsDir, $"{district.Key.Id:D2}-{section.Key.Id:D3}.json"));

                foreach (var circuit in section.GroupBy(x => x.Circuit))
                {
                    var cstats = Stats.Calculate(circuit);
                    await ModelSerializer.SerializeAsync(dstats, Path.Combine(statsDir, $"{district.Key.Id:D2}-{section.Key.Id:D3}-{circuit.Key.Replace(' ', '_')}.json"));

                    foreach (var local in circuit.GroupBy(x => x.Local))
                    {
                        var lstats = Stats.Calculate(local);
                        await ModelSerializer.SerializeAsync(dstats, Path.Combine(statsDir, $"{local.Key.Id}.json"));
                        AddAnomalies(lstats.FindAnomalies(local, "Establecimiento"));
                    }
                    AddAnomalies(cstats.FindAnomalies(circuit, "Circuito"));
                }
                AddAnomalies(sstats.FindAnomalies(section, "Seccion"));
            }
            AddAnomalies(dstats.FindAnomalies(district, "Distrito"));
        }

        var anomaliesDir = Path.Combine(settings.BaseDir, "anomalies");
        Directory.CreateDirectory(anomaliesDir);

        await ModelSerializer.SerializeAsync(anomalies, Path.Combine(settings.BaseDir, "anomalies.json.gz"));

        foreach (var anomaly in anomalies)
        {
            await ModelSerializer.SerializeAsync(anomaly with
            {
                PageUrl = "https://resultados.gob.ar" + anomaly.PageUrl,
                TelegramUrl = Constants.AzureStorageUrl + anomaly.TelegramUrl,
            }, Path.Combine(anomaliesDir, $"{anomaly.Id}.json.gz"));
        }

        WriteLine($"Anomalies {Math.Truncate(100d * anomalies.Count / telegrams.Count * 100) / 100}%");

        // by user
        var userAnomalies = anomalies
            .Where(x => x.User != null)
            .GroupBy(x => x.User)
            .ToDictionary(x => x.Key, x => (double)x.Count());

        // calculate anomaly stats (lower quartile, upper quartile and iqr for all telegrams and by user)
        var users = telegrams
            .GroupBy(x => x.User)
            .Where(x => userAnomalies.ContainsKey(x.Key))
            .Select(x => new { x.Key, Percentage = Math.Truncate(userAnomalies[x.Key] / x.Count() * 1000) / 100 })
            .ToDictionary(x => x.Key, x => x.Percentage);

        var values = users.Values.ToArray();

        var lowerBoundary = Statistics.LowerQuartile(values) - (1.5 * Statistics.InterquartileRange(values));
        var upperBoundary = Statistics.UpperQuartile(values) + (1.5 * Statistics.InterquartileRange(values));

        // user anomalies
        var anomalyUsers = users
            .Where(x => x.Value < lowerBoundary || x.Value > upperBoundary)
            .OrderByDescending(x => x.Value)
            .ToArray();

        await ModelSerializer.SerializeAsync(new
        {
            Anomalies = anomalyUsers.Select(x => new { x.Key.Id, x.Key.Name, Percentage = Math.Truncate(100 * x.Value) / 100 }),
            Stats = new
            {
                Mean = Statistics.Mean(values),
                LowerQuartile = Statistics.LowerQuartile(values),
                UpperQuartile = Statistics.UpperQuartile(values),
                StandardDeviation = Statistics.StandardDeviation(values),
                Variance = Statistics.Variance(values),
                Median = Statistics.Median(values),
                InterquartileRange = Statistics.InterquartileRange(values),
            },
        }, Path.Combine(anomaliesDir, "users.json.gz"));

        if (await ModelSerializer.DeserializeAsync(electionFile) is { } election)
        {
            var votes = election.GetBallots()
                .Where(x => x.Kind == BallotKind.Positive && x.Position == "PRESIDENTE Y VICE")
                .GroupBy(x => x.Party!, x => x.Count)
                .ToDictionary(x => x.Key, x => x.Sum())
                .OrderByDescending(x => x.Value)
                .Take(5)
                .ToDictionary();

            var anomalyVotes = anomalies.SelectMany(x => x.Parties.Select(p => new { p.Name, p.Votes }))
                .GroupBy(x => x.Name)
                .ToDictionary(x => x.Key, x => x.Sum(y => y.Votes));

            // Create a table that has three columns with: 
            // - Votes 
            // - Votes percentage of total votes
            // - Adjusted votes (without anomalies)
            // - Adjusted percentage of total votes (without anomalies)
            // Each row is a party
            var table = new Table()
                .AddColumn("Partido")
                .AddColumn("Votos")
                .AddColumn("Porcentaje")
                .AddColumn("Votos ajustados")
                .AddColumn("Porcentaje ajustado");

            var totalVotes = votes.Sum(x => x.Value);
            var totalAnomalyVotes = anomalyVotes.Sum(x => x.Value);
            var totalAdjustedVotes = totalVotes - totalAnomalyVotes;

            foreach (var party in votes.OrderByDescending(x => x.Value))
            {
                var votesPercentage = Math.Truncate(100d * votes[party.Key] / totalVotes * 100) / 100;
                var anomalyVotesPercentage = anomalyVotes.ContainsKey(party.Key)
                    ? Math.Truncate(100d * anomalyVotes[party.Key] / totalAnomalyVotes * 100) / 100 
                    : 0;

                var adjustedVotes = anomalyVotes.ContainsKey(party.Key)
                    ? votes[party.Key] - anomalyVotes[party.Key]
                    : votes[party.Key];

                var adjustedVotesPercentage = Math.Truncate(100d * adjustedVotes / totalAdjustedVotes * 100) / 100;

                table.AddRow(
                    party.Key!,
                    votes[party.Key].ToString("N"),
                    $"{votesPercentage}%",
                    adjustedVotes.ToString("N"),
                    $"{adjustedVotesPercentage}%");
            }

            Render(table);
        }

        return 0;
    }
}
