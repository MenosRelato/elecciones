using Spectre.Console;

namespace MenosRelato;

static class Results
{
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
