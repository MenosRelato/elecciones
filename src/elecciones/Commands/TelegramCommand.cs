using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NuGet.Common;
using Spectre.Console.Cli;
using static MenosRelato.Results;
using static Spectre.Console.AnsiConsole;

namespace MenosRelato.Commands;

[Description("Descarga telegramas de la eleccion")]
internal class TelegramCommand(AsyncLazy<IBrowser> browser) : AsyncCommand<TelegramCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("-s|--skip")]
        [Description("# de distritos a saltear")]
        public int Skip { get; init; } = 0;

        [CommandOption("-t|--take")]
        [Description("# de distritos a procesar")]
        public int Take { get; init; } = int.MaxValue;

        [CommandOption("-p|--paralell")]
        [Description("# de items a procesar en paralelo")]
        public int Paralellize { get; init; } = 4;
    }

    record District(string? Name)
    {
        public List<Section> Sections { get; } = new();
    }
    record Section(string? Name)
    {
        public List<Circuit> Circuits { get; } = new();
    }
    record Circuit(string? Name)
    {
        public List<Institution> Institutions { get; } = new();
    }
    record Institution(string? Name)
    {
        public List<Table> Tables { get; } = new();
    }
    record Table(string Code, string Url);

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var chrome = await browser;
        var districts = 0;
        await Status().StartAsync("Determinando distritos electorales", async ctx =>
        {
            await using var context = await chrome.NewContextAsync();
            var page = await context.NewPageAsync();
            await page.GotoAsync("https://resultados.gob.ar/", new PageGotoOptions
            {
                Timeout = 0,
                WaitUntil = WaitUntilState.NetworkIdle,
            });
            await page.GetByLabel("Filtro por ámbito").ClickAsync();
            await page.GetByLabel(new Regex(@"\s*Selecciona un distrito presionando enter")).ClickAsync();
            foreach (var district in await page.GetByRole(AriaRole.Option).AllAsync())
            {
                districts++;
            }
        });

        await Status().StartAsync("Determinando distritos electorales", async ctx =>
        {
            var progress = new Progress<string>(status => ctx.Status = status);
            var values = Enumerable.Range(0, districts).Skip(settings.Skip).Take(settings.Take);

            await Parallel.ForEachAsync(values, new ParallelOptions { MaxDegreeOfParallelism = settings.Paralellize }, async (i, c) =>
            {
                var prepare = new PrepareTelegram(chrome, i, progress);
                await prepare.ExecuteAsync();
            });
        });

        await Status().StartAsync("Determinando distritos electorales", async ctx =>
        {
            var progress = new Progress<string>(status => ctx.Status = status);
            var values = Enumerable.Range(0, districts).Skip(settings.Skip).Take(settings.Take);

            await Parallel.ForEachAsync(values, new ParallelOptions { MaxDegreeOfParallelism = settings.Paralellize }, async (i, c) =>
            {
                var prepare = new PrepareTelegram(chrome, i, progress);
                await prepare.ExecuteAsync();
            });
        });

        return 0;
    }

    static async Task SaveAsync<T>(T value, string fileName)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
            var path = Path.Combine(Constants.DefaultCacheDir, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var json = File.Create(Path.Combine(Constants.DefaultCacheDir, fileName));
            await JsonSerializer.SerializeAsync(json, value, options);
        }
        catch (IOException e)
        {
            Error($"No se pudo guardar el archivo {fileName}: {e.Message}");
        }
    }

    class DownloadTelegram(District district, IBrowser browser, IProgress<string> progress)
    {
        public async Task ExecuteAsync()
        {
            await using var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();
            var tables = district.Sections.SelectMany(s => s.Circuits.SelectMany(c => c.Institutions.SelectMany(i => i.Tables))).ToList();

            foreach (var table in tables)
            {

            }
        }
    }

    class PrepareTelegram(IBrowser browser, int skip, IProgress<string> progress)
    {
        public async Task ExecuteAsync()
        {
            await using var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();

            await page.GotoAsync("https://resultados.gob.ar/", new PageGotoOptions
            {
                Timeout = 0,
                WaitUntil = WaitUntilState.NetworkIdle,
            });

            await page.GetByLabel("Filtro por ámbito").ClickAsync();
            await page.GetByLabel(new Regex(@"\s*Selecciona un distrito presionando enter")).ClickAsync();

            var count = 0;
            var districts = new List<District>();

            foreach (var district in await page.GetByRole(AriaRole.Option).AllAsync())
            {
                if (count < skip)
                {
                    count++;
                    continue;
                }

                districts.Add(new(await district.TextContentAsync()));
                await district.ClickAsync();
                await page.GetByLabel(new Regex("Selecciona una sección presionando enter")).First.ClickAsync();

                foreach (var section in await page.GetByRole(AriaRole.Option).AllAsync())
                {
                    districts[^1].Sections.Add(new(await section.TextContentAsync()));
                    await section.ClickAsync();
                    await page.GetByLabel(new Regex("Selecciona un circuito")).First.ClickAsync();
                    foreach (var circuit in await page.GetByRole(AriaRole.Option).AllAsync())
                    {
                        districts[^1].Sections[^1].Circuits.Add(new(await circuit.TextContentAsync()));
                        await circuit.ClickAsync();
                        await page.GetByLabel(new Regex("Selecciona un local")).First.ClickAsync();
                        foreach (var local in await page.GetByRole(AriaRole.Option).AllAsync())
                        {
                            districts[^1].Sections[^1].Circuits[^1].Institutions.Add(new(await local.TextContentAsync()));
                            progress.Report($"{districts[^1].Name} - {districts[^1].Sections[^1].Name} - {districts[^1].Sections[^1].Circuits[^1].Name} - {districts[^1].Sections[^1].Circuits[^1].Institutions[^1].Name}");
                            await local.ClickAsync();
                            await page.GetByLabel(new Regex("de mesa presionando enter")).First.ClickAsync();
                            foreach (var table in await page.GetByRole(AriaRole.Option).AllAsync())
                            {
                                var name = await table.TextContentAsync();
                                await table.ClickAsync();
                                var url = await page.GetByText("Aplicar filtros").GetAttributeAsync("href");
                                Debug.Assert(name != null && url != null);
                                districts[^1].Sections[^1].Circuits[^1].Institutions[^1].Tables.Add(new(name, url));
                                await page.GetByLabel(new Regex("de mesa presionando enter")).First.ClickAsync();
                            }
                            await page.GetByLabel(new Regex("Selecciona un local")).First.ClickAsync();
                        }
                        await page.GetByLabel(new Regex("Selecciona un circuito")).First.ClickAsync();
                    }
                    await page.GetByLabel(new Regex("Selecciona una sección presionando enter")).First.ClickAsync();
                }

                await SaveAsync(districts[^1], Path.Combine("web", districts[^1].Sections[0].Circuits[0].Institutions[0].Tables[0].Code[..2] + ".json"));
                break;
            }
        }
    }
}
