using System.Diagnostics;

namespace MenosRelato;

public static class ShellProcess
{
    public static string? GetPath(string executable)
    {
        var zip = new ProcessStartInfo(executable)
        {
            CreateNoWindow = true,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        if (Process.Start(zip) is not { } proc ||
            proc?.MainModule?.FileName is not string path ||
            !proc.WaitForExit(1000))
        {
            return null;
        }

        return path;
    }
}
