using System.ComponentModel;
using Spectre.Console.Cli;

namespace MenosRelato.Commands;

public class SliceCommand(ICommandApp app) : AsyncCommand<SliceCommand.Settings>
{
    public class Settings : ElectionSettings
    {
        [CommandOption("-d|--district <VALUES>")]
        [Description("Distritos a incluir en el subset")]
        public int[] Districts { get; set; } = [];

        [CommandArgument(0, "[output]")]
        [Description("Archivo de salida con el subset de datos")]
        [DefaultValue("slice.json")]
        public string Output { get; set; } = "slice.json";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var file = Path.Combine(settings.BaseDir, Constants.ResultsFile);

        if (!File.Exists(file))
            await app.RunAsync(["download", "-r"]);

        var election = await ModelSerializer.DeserializeAsync(Path.Combine(settings.BaseDir, Constants.ResultsFile));
        if (election is null)
            return -1;

        var districts = settings.Districts.ToHashSet();

        foreach (var district in election.Districts.ToArray())
        {
            if (!districts.Contains(district.Id))
                election.Districts.Remove(district);    
        }

        await ModelSerializer.SerializeAsync(election, settings.Output);
        
        return 0;
    }
}
