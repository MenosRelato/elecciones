using System.Diagnostics;
using System.IO.Compression;
using Humanizer;
using Spectre.Console.Cli;
using static Spectre.Console.AnsiConsole;

namespace MenosRelato.Commands;

internal class DownloadCommand(IHttpClientFactory factory) : AsyncCommand
{
    //const string paso = "https://www.argentina.gob.ar/sites/default/files/dine-resultados/2023-PROVISORIOS_PASO.zip";
    const string gral = "https://www.argentina.gob.ar/sites/default/files/2023_generales_0.zip";
    readonly string stampfile = Path.Combine(Constants.DefaultCacheDir, "datos", Path.GetFileName(Path.ChangeExtension(gral, ".etag")));
    readonly string csvdir = Path.Combine(Constants.DefaultCacheDir, "datos", "csv");

    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        Directory.CreateDirectory(csvdir);

        using var http = factory.CreateClient();
        using var response = await http.GetAsync(gral, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        Debug.Assert(response.Headers.ETag != null, "Couldn't get ETag from zip download response.");

        if (!File.Exists(Path.Combine(stampfile)) ||
            File.ReadAllText(stampfile) is not { } etag || 
            etag != response.Headers.ETag.Tag.Trim('"'))
        {
            var zip = Path.Combine(Constants.DefaultCacheDir, "datos", Path.GetFileName(gral));
            await Status().StartAsync("Descargando dataset actualizado", async ctx =>
            {
                using var content = await response.Content.ReadAsStreamAsync();
                using var output = File.Create(zip, 8192);

                var bytes = 0L;
                var buffer = new byte[8192];
                var eof = false;

                do
                {
                    var read = await content.ReadAsync(buffer);
                    if (read == 0)
                    {
                        eof = true;
                    }
                    else
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read));
                        bytes += read;
                        ctx.Status = $"Descargando dataset actualizado ({bytes.Bytes()} de {(response.Content.Headers.ContentLength ?? -1).Bytes()})";
                    }
                }
                while (!eof);

                File.WriteAllText(stampfile, response.Headers.ETag.Tag.Trim('"'));
                MarkupLine($"[green]✓[/] El dataset de elecciones generales 2023 fue actualizado.");
            });

            // Delete existing files
            Directory.Delete(csvdir, true);
            Directory.CreateDirectory(csvdir);

            Status().Start("Descomprimiendo dataset", _ => ZipFile.ExtractToDirectory(zip, csvdir));
            MarkupLine($"[green]✓[/] {Directory.EnumerateFiles(csvdir).Count()} archivos extraidos con {Directory.EnumerateFiles(csvdir).Select(x => new FileInfo(x).Length).Sum().Bytes()}.");
        }
        else
        {
            MarkupLine($"[green]✓[/] El dataset de elecciones generales 2023 esta actualizado.");
        }

        return 0;
    }
}
