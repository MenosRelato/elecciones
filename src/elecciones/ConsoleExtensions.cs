using Spectre.Console;
using Spectre.Console.Cli;

namespace MenosRelato;

static class ConsoleExtensions
{
    public static void Advanced(this ICommandConfigurator command, bool showAdvanced)
    {
        if (!showAdvanced)
            command.IsHidden();
    }

    public static int Error(string message)
    {
        AnsiConsole.MarkupLine($"[red]x[/] " + message);
        return -1;
    }

    public static int Result(int code, string message)
    {
        if (code < 0)
            AnsiConsole.MarkupLine($"[red]x[/] " + message);
        else
            AnsiConsole.MarkupLine($"[green]✓[/] " + message);

        return code;
    }
}
