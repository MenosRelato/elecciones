using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MenosRelato.Commands;

[Description("Gzip JSON files in-place")]
internal class GzipCommand : AsyncCommand<GzipCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<DIR>")]
        public string Directory { get; set; } = string.Empty;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var path = Path.Combine(Constants.DefaultCacheDir, settings.Directory);
        var files = Directory.GetFiles(path, "*.json", SearchOption.AllDirectories);
        double count = files.Count();
        double current = 0;

        await AnsiConsole.Status()
            .StartAsync("Compressing files...", async ctx =>
            {
                IProgress<string> progress = new Progress<string>(x =>
                {
                    current++;
                    ctx.Status($"Compressing files {(current / count):P} ({current} of {count})");
                });

                await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, async (file, token) =>
                {
                    var text = await File.ReadAllTextAsync(file, token);
                    // Don't keep empty files around?
                    if (text.Length == 0)
                        File.Delete(file);
                    // We may have already compressed the file
                    else if (text[0] == '[' || text[0] == '{')
                        await GzipFile.WriteAllTextAsync(file, text);
                    else
                        // Rename to .gz
                        File.Move(file, file + ".gz");

                    progress.Report(file);
                });
            });

        return 0;
    }

}