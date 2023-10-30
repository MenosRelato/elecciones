using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NuGet.Common;
using Polly.Retry;
using Polly;
using Spectre.Console.Cli;
using static MenosRelato.Results;
using static Spectre.Console.AnsiConsole;
using System.IO.Compression;

namespace MenosRelato.Commands;

[Description("Descarga telegramas de la eleccion con metadata en formato JSON (compactado con GZip)")]
internal class TelegramCommand(AsyncLazy<IBrowser> browser, ResiliencePipeline resilience) : AsyncCommand<TelegramCommand.Settings>
{
    public class Settings : ElectionSettings
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

        [CommandOption("-d|--download", IsHidden = true)]
        [Description("Descarga telegramas directa, sin pre-procesamiento de distritos")]
        public bool DownloadOnly { get; init; } = false;

        [CommandOption("-z|--zip")]
        [Description("Comprimir JSON de metadata con GZip")]
        [DefaultValue(true)]
        public bool Zip { get; set; } = true;
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
        public List<Station> Stations { get; } = new();
    }
    record Station(string Code, string Url);

    static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var chrome = await browser;

        if (!settings.DownloadOnly)
        {
            var districts = 0;
            await Status().StartAsync("Contando distritos electorales", async ctx =>
            {
                await resilience.ExecuteAsync(async _ =>
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
                    districts = 0;
                    foreach (var district in await page.GetByRole(AriaRole.Option).AllAsync())
                    {
                        districts++;
                    }
                });
            });

            await Status().StartAsync("Indexando distritos electorales", async ctx =>
            {
                var progress = new Progress<string>(status => ctx.Status = status);
                var values = Enumerable.Range(0, districts).Skip(settings.Skip).Take(settings.Take);

                await Parallel.ForEachAsync(values, new ParallelOptions { MaxDegreeOfParallelism = settings.Paralellize }, async (i, c) =>
                {
                    var prepare = new PrepareTelegram(chrome, resilience, settings, progress);
                    await prepare.ExecuteAsync();
                });
            });
        }

        await Status().StartAsync("Descargando telegramas", async ctx =>
        {
            var progress = new Progress<string>(status => ctx.Status = status);
            var path = Path.Combine(settings.BaseDir, "telegrama");
            var districts = Directory.EnumerateFiles(path, "*.json");

            await Parallel.ForEachAsync(districts, new ParallelOptions { MaxDegreeOfParallelism = settings.Paralellize }, async (x, c) =>
            {
                var district = Newtonsoft.Json.JsonConvert.DeserializeObject<District>(await GzipFile.ReadAllTextAsync(x));
                Debug.Assert(district != null);
                var prepare = new DownloadTelegram(district, settings, progress);
                await prepare.ExecuteAsync();
            });
        });

        return 0;
    }

    static async Task SaveAsync<T>(T value, string fileName, bool zip)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetDirectoryName(fileName))!);
            
            if (zip)
            {
                using var json = File.Create(fileName + ".gz");
                using var gzip = new GZipStream(json, CompressionLevel.Optimal);
                await JsonSerializer.SerializeAsync(gzip, value, jsonOptions);
            }
            else
            {
                using var json = File.Create(fileName);
                await JsonSerializer.SerializeAsync(json, value, jsonOptions);
            }
        }
        catch (IOException e)
        {
            Error($"No se pudo guardar el archivo {fileName}: {e.Message}");
        }
    }

    class DownloadTelegram(District district, Settings settings, IProgress<string> progress)
    {
        public async Task ExecuteAsync()
        {
            using var http = Constants.CreateHttp();
            http.BaseAddress = new Uri("https://resultados.gob.ar");

            foreach (var circuit in district.Sections.SelectMany(s => s.Circuits))
            {
                foreach (var station in circuit.Institutions.SelectMany(i => i.Stations))
                {
                    var districtId = int.Parse(station.Code[..2]);
                    var sectionId = int.Parse(station.Code[2..5]);
                    var circuitId = circuit.Name;
                    Debug.Assert(circuitId != null);

                    var path = Path.Combine(settings.BaseDir, "telegrama", districtId.ToString(), sectionId.ToString(), circuitId);
                    Directory.CreateDirectory(path);

                    var scope = await http.GetStringAsync("/backend-difu/scope/data/getScopeData/" + station.Code + "/1");
                    Debug.Assert(!string.IsNullOrEmpty(scope));
                    var sdata = Newtonsoft.Json.Linq.JObject.Parse(scope);
                    if (settings.Zip)
                        await GzipFile.WriteAllTextAsync(Path.Combine(path, station.Code + ".scope.json.gz"), sdata.ToString());
                    else
                        await File.WriteAllTextAsync(Path.Combine(path, station.Code + ".scope.json"), sdata.ToString());

                    var location = string.Join(" - ", sdata.SelectTokens("$.fathers[*].name").Reverse().Skip(1).Select(x => x.ToString()));

                    var tiff = await http.GetStringAsync("/backend-difu/scope/data/getTiff/" + station.Code);
                    if (string.IsNullOrEmpty(tiff))
                    {
                        progress.Report($"[red]x[/] No hay telegrama {location} - {station.Code}");
                        continue;
                    }

                    Debug.Assert(!string.IsNullOrEmpty(tiff));

                    dynamic tdata = Newtonsoft.Json.Linq.JObject.Parse(tiff);
                    if (settings.Zip)
                        await GzipFile.WriteAllTextAsync(Path.Combine(path, station.Code + ".json.gz"), tdata.ToString());
                    else
                        await File.WriteAllTextAsync(Path.Combine(path, station.Code + ".json"), tdata.ToString());

                    var img = Convert.FromBase64String((string)tdata.encodingBinary);
                    await File.WriteAllBytesAsync(Path.Combine(path, station.Code + ".png"), img);

                    progress.Report($"Descargado telegrama {location} - {station.Code}");
                }
            }
        }
    }

    class PrepareTelegram(IBrowser browser, ResiliencePipeline resilience, Settings settings, IProgress<string> progress)
    {
        public async Task ExecuteAsync()
        {
            await using var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(5000);
           
            var actions = new Stack<Func<Task>>();

            actions.Push(() => page.GotoAsync("https://resultados.gob.ar/", new PageGotoOptions
            {
                Timeout = 0,
                WaitUntil = WaitUntilState.NetworkIdle,
            }));

            var districtButton = page.GetByLabel(new Regex(@"\s*Selecciona un distrito presionando enter"));
            var sectionButton = page.GetByLabel(new Regex("Selecciona una sección presionando enter")).First;
            var circuitButton = page.GetByLabel(new Regex("Selecciona un circuito")).First;
            var localButton = page.GetByLabel(new Regex("Selecciona un local")).First;
            var stationButton = page.GetByLabel(new Regex("de mesa presionando enter")).First;

            actions.Push(() => page.GetByLabel("Filtro por ámbito").ClickAsync());
            actions.Push(() => districtButton.ClickAsync());

            var count = 0;
            var districts = new List<District>();

            var retry = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = int.MaxValue,
                    BackoffType = DelayBackoffType.Linear,
                    Delay = TimeSpan.FromSeconds(2),
                    OnRetry = async x =>
                    {
                        MarkupLine($"[red]x[/] Reintento #{x.AttemptNumber + 1}");
                        foreach (var action in actions.Reverse())
                            await action();
                    },
                })
                .Build();

            // Resiliently execute the initial actions.
            await resilience.ExecuteAsync(async _ =>
            {
                foreach (var action in actions.Reverse())
                    await action();
            });

            async Task<IDisposable> PushExecuteAsync(Func<Task> action)
            {
                await retry!.ExecuteAsync(async _ => await action());
                actions!.Push(action);
                return new Poper(actions);
            }

            foreach (var district in await page.GetByRole(AriaRole.Option).AllAsync())
            {
                if (count < settings.Skip)
                {
                    count++;
                    continue;
                }

                using var dc = await PushExecuteAsync(() => district.ClickAsync());
                districts.Add(new(await retry.ExecuteAsync(async _ => await districtButton.TextContentAsync())));

                using var ds = await PushExecuteAsync(() => sectionButton.ClickAsync());

                foreach (var section in await page.GetByRole(AriaRole.Option).AllAsync())
                {
                    using var sc = await PushExecuteAsync(() => section.ClickAsync());
                    districts[^1].Sections.Add(new(await retry.ExecuteAsync(async _ => await sectionButton.TextContentAsync())));

                    using var ss = await PushExecuteAsync(() => circuitButton.ClickAsync());

                    foreach (var circuit in await page.GetByRole(AriaRole.Option).AllAsync())
                    {
                        using var cc = await PushExecuteAsync(() => circuit.ClickAsync());
                        districts[^1].Sections[^1].Circuits.Add(new(await retry.ExecuteAsync(async _ => await circuitButton.TextContentAsync())));

                        using var cs = await PushExecuteAsync(() => localButton.ClickAsync());

                        foreach (var local in await page.GetByRole(AriaRole.Option).AllAsync())
                        {
                            using var lc = await PushExecuteAsync(() => local.ClickAsync());
                            districts[^1].Sections[^1].Circuits[^1].Institutions.Add(new(await retry.ExecuteAsync(async _ => await localButton.TextContentAsync())));
                            progress.Report($"{districts[^1].Name} - {districts[^1].Sections[^1].Name} - {districts[^1].Sections[^1].Circuits[^1].Name} - {districts[^1].Sections[^1].Circuits[^1].Institutions[^1].Name}");

                            using var ls = await PushExecuteAsync(() => stationButton.ClickAsync());
                            
                            foreach (var station in await page.GetByRole(AriaRole.Option).AllAsync())
                            {
                                using var tc = await PushExecuteAsync(() => station.ClickAsync());
                                var code = await retry.ExecuteAsync(async _ => await stationButton.TextContentAsync());
                                var url = await retry.ExecuteAsync(async _ => await page.GetByText("Aplicar filtros").GetAttributeAsync("href"));

                                Debug.Assert(code != null && url != null);
                                districts[^1].Sections[^1].Circuits[^1].Institutions[^1].Stations.Add(new(code, url));

                                await retry.ExecuteAsync(async _ => await stationButton.ClickAsync());

                                var districtFile = Path.Combine(settings.BaseDir, "telegrama", $"{code[..2]}.json{(settings.Zip ? "gz" : "")}");
                                if (File.Exists(districtFile))
                                {
                                    Result(0, $"Distrito {districts[^1].Name} ya esta indexado");
                                    return;
                                }
                            }

                            await retry.ExecuteAsync(async _ => await localButton.ClickAsync());
                        }

                        await retry.ExecuteAsync(async _ => await circuitButton.ClickAsync());
                    }

                    await retry.ExecuteAsync(async _ => await sectionButton.ClickAsync());
                }

                var districtId = int.Parse(districts[^1].Sections[0].Circuits[0].Institutions[0].Stations[0].Code[..2]);

                await SaveAsync(districts[^1], 
                    Path.Combine(settings.BaseDir, "telegrama", $"{districtId}.json"), 
                    settings.Zip);

                break;
            }
        }

        class Poper(Stack<Func<Task>> stack) : IDisposable
        {
            public void Dispose() => stack.Pop();
        }
    }
}
