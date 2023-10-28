using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NuGet.Common;
using Polly;
using Polly.Retry;
using Spectre.Console.Cli;
using static MenosRelato.Results;
using static Spectre.Console.AnsiConsole;

namespace MenosRelato.Commands;

[Description("Descarga telegramas de la eleccion")]
internal class TelegramCommand(AsyncLazy<IBrowser> browser) : AsyncCommand<TelegramCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("-s|--skip", IsHidden = true)]
        [Description("# de items a saltear")]
        public int Skip { get; init; } = 0;

        [CommandOption("-t|--take", IsHidden = true)]
        [Description("# de items a procesar")]
        public int Take { get; init; } = int.MaxValue;
    }

    record District(string? Name)
    {
        public List<Section> Sections { get; } = new();
    }
    record Section(string? Name)
    {
        public List<Municipality> Municipalities { get; } = new();
    }
    record Municipality(string? Name)
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
        var page = await chrome.NewPageAsync();
        var gopt = new PageGotoOptions
        {
            Timeout = 0,
            WaitUntil = WaitUntilState.NetworkIdle,
        };

        var initActions = new Task[]
        {
            //page.GotoAsync("about:blank", gopt),
            page.GotoAsync("https://resultados.gob.ar/", gopt),
            page.GetByLabel("Filtro por ámbito").ClickAsync(),
        };

        async Task InitAsync()
        {
            foreach (var action in initActions!)
                await action;
        }

        var retryActions = new List<Task>(initActions);

        var retry = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = int.MaxValue,
                Delay = TimeSpan.FromSeconds(2),
                OnRetry = async x =>
                {
                    MarkupLine($"[red]x[/] Reintento #{x.AttemptNumber + 1}");

                    foreach (var action in retryActions)
                        await action;
                },
            })
            .Build();

        var districts = new List<District>();
        try
        {
            await InitAsync();

            await page.GetByLabel(new Regex(@"\s*Selecciona un distrito presionando enter")).ClickAsync();

            await Status().StartAsync("Procesando secciones electorales", async ctx =>
            {
                var count = 0;
                foreach (var district in await page.GetByRole(AriaRole.Option).AllAsync())
                {
                    if (count < settings.Skip)
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
                        await page.GetByLabel(new Regex("Selecciona un municipio")).First.ClickAsync();
                        foreach (var muni in await page.GetByRole(AriaRole.Option).AllAsync())
                        {
                            districts[^1].Sections[^1].Municipalities.Add(new(await muni.TextContentAsync()));
                            await muni.ClickAsync();
                            await page.GetByLabel(new Regex("Selecciona un circuito")).First.ClickAsync();
                            foreach (var circuit in await page.GetByRole(AriaRole.Option).AllAsync())
                            {
                                districts[^1].Sections[^1].Municipalities[^1].Circuits.Add(new(await circuit.TextContentAsync()));
                                await circuit.ClickAsync();
                                await page.GetByLabel(new Regex("Selecciona un local")).First.ClickAsync();
                                foreach (var local in await page.GetByRole(AriaRole.Option).AllAsync())
                                {
                                    districts[^1].Sections[^1].Municipalities[^1].Circuits[^1].Institutions.Add(new(await local.TextContentAsync()));
                                    ctx.Status = $"{districts[^1].Name} - {districts[^1].Sections[^1].Name} - {districts[^1].Sections[^1].Municipalities[^1].Name} - {districts[^1].Sections[^1].Municipalities[^1].Circuits[^1].Name} - {districts[^1].Sections[^1].Municipalities[^1].Circuits[^1].Institutions[^1].Name}";
                                    await local.ClickAsync();
                                    await page.GetByLabel(new Regex("de mesa presionando enter")).First.ClickAsync();
                                    foreach (var table in await page.GetByRole(AriaRole.Option).AllAsync())
                                    {
                                        var name = await table.TextContentAsync();
                                        await table.ClickAsync();
                                        var url = await page.GetByText("Aplicar filtros").GetAttributeAsync("href");
                                        Debug.Assert(name != null && url != null);
                                        districts[^1].Sections[^1].Municipalities[^1].Circuits[^1].Institutions[^1].Tables.Add(new(name, url));
                                        await page.GetByLabel(new Regex("de mesa presionando enter")).First.ClickAsync();
                                    }
                                    await page.GetByLabel(new Regex("Selecciona un local")).First.ClickAsync();
                                }
                                await SaveAsync(districts, "resultados.json");
                                await page.GetByLabel(new Regex("Selecciona un circuito")).First.ClickAsync();
                            }
                            await SaveAsync(districts, "resultados.json");
                            await page.GetByLabel(new Regex("Selecciona un municipio")).First.ClickAsync();
                        }
                        await SaveAsync(districts, "resultados.json");
                        await page.GetByLabel(new Regex("Selecciona una sección presionando enter")).First.ClickAsync();
                    }

                    await SaveAsync(districts, "resultados.json");
                    await SaveAsync(districts[^1], Path.Combine("web", districts[^1].Sections[0].Municipalities[0].Circuits[0].Institutions[0].Tables[0].Code[..2] + ".json"));

                    if ((count - settings.Skip) == settings.Take)
                        break;

                    await retry.ExecuteAsync(async _ => await page.GetByLabel(new Regex(@"\s*Selecciona un distrito presionando enter")).ClickAsync());
                }
            });
        }
        finally
        {
            await SaveAsync(districts, "web.json");
        }

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
}
