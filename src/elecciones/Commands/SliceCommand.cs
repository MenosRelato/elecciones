using System.ComponentModel;
using Spectre.Console.Cli;

namespace MenosRelato.Commands;

public class SliceCommand(ICommandApp app) : AsyncCommand<SliceCommand.Settings>
{
    public class Settings : ElectionSettings
    {
        [CommandOption("-d|--district <VALUES>")]
        [Description("Distritos a incluir en el subset")]
        public string[] Districts { get; set; } = Array.Empty<string>();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var file = Path.Combine(settings.BaseDir, Constants.ResultsFile);

        if (!File.Exists(file))
        {

        }

        var election = await ModelSerializer.DeserializeAsync(Path.Combine(settings.BaseDir, Constants.ResultsFile));

        throw new NotImplementedException();
    }
}
