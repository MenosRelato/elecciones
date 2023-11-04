using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Newtonsoft.Json.Linq;
using NuGet.Common;
using Polly;
using Polly.Retry;
using Spectre.Console;
using Spectre.Console.Cli;
using static MenosRelato.ConsoleExtensions;
using static Spectre.Console.AnsiConsole;

namespace MenosRelato.Commands;

[Description("Descarga telegramas de la eleccion con metadata en formato JSON (compactado con GZip)")]
internal class TelegramCommand(AsyncLazy<IBrowser> browser, ResiliencePipeline resilience, IHttpClientFactory httpFactory) : AsyncCommand<TelegramCommand.Settings>
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

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (!settings.DownloadOnly)
        {
            var chrome = await browser;
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
                    var prepare = new PrepareTelegram(chrome, resilience, settings.BaseDir, i, true, progress);
                    await prepare.ExecuteAsync();
                });
            });
        }

        await Progress()
            .HideCompleted(true)
            .Columns(
            [
                new TaskDescriptionColumn { Alignment = Justify.Left },
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn(),
            ])
            .StartAsync(async ctx =>
        {
            var path = Path.Combine(settings.BaseDir, "telegram");
            var districts = new[] { "*.json", "*.json.gz" }
                .SelectMany(ext => Directory.EnumerateFiles(path, ext))
                .Skip(settings.Skip).Take(settings.Take);

            var file = Path.Combine(settings.BaseDir, "election.json.gz");
            var election = await ModelSerializer.DeserializeAsync(Path.Combine(settings.BaseDir, file));
            Debug.Assert(election != null);

            await Parallel.ForEachAsync(districts, new ParallelOptions { MaxDegreeOfParallelism = settings.Paralellize }, async (x, c) =>
            {
                var json = x.EndsWith(".gz") ? await GzipFile.ReadAllTextAsync(x) : await File.ReadAllTextAsync(x);
                var district = Newtonsoft.Json.JsonConvert.DeserializeObject<District>(json);
                Debug.Assert(district != null);
                var prepare = new DownloadTelegram(district, settings, resilience, httpFactory, ctx);
                await prepare.ExecuteAsync();

                MenosRelato.District? dm = default;

                foreach (var section in district.Sections)
                {
                    double total = section.Circuits.SelectMany(c => c.Institutions).SelectMany(s => s.Stations).Count();
                    var task = ctx.AddTask($"Actualizando {district.Name} - {section.Name}".PadRight(30), new ProgressTaskSettings { MaxValue = total });

                    MenosRelato.Section? sm = default;

                    foreach (var station in section.Circuits.SelectMany(c => c.Institutions).SelectMany(i => i.Stations))
                    {
                        task.Increment(1d);
                        if (dm is null)
                        {
                            var districtId = int.Parse(station.Code[..2]);
                            dm = election.Districts.FirstOrDefault(d => d.Id == districtId);
                            Debug.Assert(dm != null);
                        }
                        if (sm is null)
                        {
                            var sectionId = int.Parse(station.Code[2..5]);
                            sm = dm.Sections.FirstOrDefault(s => s.Id == sectionId);
                            Debug.Assert(sm != null);
                        }

                        var stm = sm.Circuits.SelectMany(x => x.Stations).FirstOrDefault(s => s.Code == station.Code);
                        Debug.Assert(stm != null);
                        stm.WebUrl = station.Url;
                        if (stm.HasTelegram == true && stm.TelegramFile is string tfile && 
                            JObject.Parse(await JsonFile.ReadAllTextAsync(Path.ChangeExtension(tfile, ".json"), true)) is JObject meta && 
                            meta.Value<string>("fileName") is string tiff)
                        {
                            stm.TelegramUrl = $"/{election.Year}/{election.Kind.ToLowerInvariant()}/telegram/{dm.Id}/{sm.Id}/{tiff}";
                        }
                    }
                }
            });

            // The election model is always zipped.
            await ModelSerializer.SerializeAsync(election, Path.Combine(settings.BaseDir, "election.json.gz"));
        });

        var file = Path.Combine(settings.BaseDir, "election.json.gz");
        var election = await ModelSerializer.DeserializeAsync(Path.Combine(settings.BaseDir, file));
        Debug.Assert(election != null);

        double notelegram = election.Districts
            .SelectMany(d => d.Sections
            .SelectMany(s => s.Circuits
            .SelectMany(c => c.Stations)))
            .Where(s => s.HasTelegram != true)
            .LongCount();

        double total = election.Districts
            .SelectMany(d => d.Sections
            .SelectMany(s => s.Circuits
            .SelectMany(c => c.Stations)))
            .LongCount();

        return Result(0, $"Completado. {notelegram / total:P} sin telegrama.");
    }

    class DownloadTelegram(District district, Settings settings, ResiliencePipeline resilience, IHttpClientFactory httpFactory, ProgressContext progress)
    {
        public async Task ExecuteAsync()
        {
            using var http = httpFactory.CreateClient();
            http.BaseAddress = new Uri("https://resultados.gob.ar");

            foreach (var section in district.Sections)
            {
                string? sectionName = default;
                var districtId = 0;
                var sectionId = 0;

                double total = section.Circuits.SelectMany(c => c.Institutions).SelectMany(s => s.Stations).Count();
                var task = progress.AddTask( $"Descargando {district.Name} - {section.Name}".PadRight(45), new ProgressTaskSettings { MaxValue = total });

                foreach (var circuit in section.Circuits)
                {
                    foreach (var station in circuit.Institutions.SelectMany(i => i.Stations))
                    {
                        task.Increment(1d);
                        districtId = int.Parse(station.Code[..2]);
                        sectionId = int.Parse(station.Code[2..5]);

                        // The circuit is not part of the telegram, and we verified across the whole set that there are no 
                        // duplicate station codes within a section.
                        var path = Path.Combine(settings.BaseDir, "telegram", districtId.ToString(), sectionId.ToString());
                        Directory.CreateDirectory(path);

                        var scopeFile = Path.Combine(path, station.Code + ".scope.json.gz");
                        var metaFile = Path.Combine(path, station.Code + ".json.gz");

                        if (File.Exists(Path.Combine(path, station.Code + ".tiff")))
                        {
                            // Merge older scope.json with main json from a previous run.
                            if (File.Exists(scopeFile) && File.Exists(metaFile))
                            {
                                var meta = JObject.Parse(await GzipFile.ReadAllTextAsync(metaFile));
                                var scope = JObject.Parse(await GzipFile.ReadAllTextAsync(scopeFile));
                                meta.Add("datos", scope);
                                await GzipFile.WriteAllTextAsync(metaFile, meta.ToString());
                                File.Delete(scopeFile);
                            }
                            continue;
                        }
                        else if (File.Exists(scopeFile) && !File.Exists(metaFile))
                        {
                            // Change older scope.json into main json from a previous run, 
                            var scope = JObject.Parse(await GzipFile.ReadAllTextAsync(scopeFile));
                            await GzipFile.WriteAllTextAsync(metaFile, new JObject(new JProperty("datos", scope)).ToString());
                            File.Delete(scopeFile);
                        }

                        var tdata = await resilience.ExecuteAsync(async _ =>
                        {
                            var tiff = await http.GetStringAsync("/backend-difu/scope/data/getTiff/" + station.Code);
                            if (string.IsNullOrEmpty(tiff))
                                return null;

                            return JObject.Parse(tiff);
                        });

                        var sdata = await resilience.ExecuteAsync(async _ =>
                        {
                            var scope = await http.GetStringAsync("/backend-difu/scope/data/getScopeData/" + station.Code + "/1");
                            Debug.Assert(!string.IsNullOrEmpty(scope));
                            // NOTE: if the site is temporarily down (it has happened), it will return 
                            // error html instead of json, so we let that go through the retry pipeline.
                            return JObject.Parse(scope);
                        });

                        var location = string.Join(" - ", sdata.SelectTokens("$.fathers[*].name").Reverse().Skip(1).Select(x => x.ToString()));
                        sectionName = string.Join(" - ", sdata.SelectTokens("$.fathers[*].name").Reverse().Skip(1).Take(2).Select(x => x.ToString()));

                        var data = new JObject(new JProperty("datos", sdata));
                        var save = !File.Exists(metaFile);

                        if (tdata is not null &&
                            tdata.Value<string>("encodingBinary") is string encoded && 
                            tdata.Value<string>("fileName") is string file)
                        {
                            var img = Convert.FromBase64String(encoded);
                            await File.WriteAllBytesAsync(Path.Combine(path, file), img);

                            // Before saving the telegrama, we remove the binary data.
                            tdata.Remove("encodingBinary");
                            tdata.Add("datos", sdata);
                            data = tdata;
                            save = true;
                        }

                        if (save)
                            await JsonFile.WriteAllTextAsync(Path.Combine(path, station.Code + ".json"), data.ToString(), true);
                    }
                }

                double stations = section.Circuits.SelectMany(c => c.Institutions.SelectMany(i => i.Stations)).Count();
                double missing = section.Circuits.SelectMany(c => c.Institutions.SelectMany(i => i.Stations)).Count(x =>
                    !File.Exists(Path.Combine(settings.BaseDir, "telegram", districtId.ToString(), sectionId.ToString(), x.Code + ".tiff")));

                if (sectionName is not null)
                    Result(0, sectionName.PadRight(45) + $" {(missing / stations):P} sin telegrama");
            }
        }
    }

    class PrepareTelegram(IBrowser browser, ResiliencePipeline resilience, string baseDir, int skip, bool zip, IProgress<string> progress)
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
            var districtFilter = page.GetByLabel("Filtro por ámbito");
            var listBox = page.GetByRole(AriaRole.Listbox);

            actions.Push(() => districtFilter.ClickAsync());
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
                        await resilience.ExecuteAsync(async _ =>
                        {
                            foreach (var action in actions.Reverse())
                                await action();
                        });
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

            async Task ResetFilters()
            {
                await resilience.ExecuteAsync(async _ => await page!.GetByText("Limpiar filtros").ClickAsync());
                await resilience.ExecuteAsync(async _ =>
                {
                    // Don't reload the page and click filter again, to speed-up the cleaning.
                    foreach (var action in actions!.Reverse().Skip(2))
                        await action();
                });
            }

            var districtNames = (await page.GetByRole(AriaRole.Option).AllAsync())
                .Select(x => x.TextContentAsync().Result)
                .ToList();

            for (var di = 1; di <= districtNames.Count; di++)
            {
                if (count < skip)
                {
                    count++;
                    continue;
                }

                var district = districtNames[di - 1];
                Debug.Assert(district != null);

                using var dc = await PushExecuteAsync(async () =>
                {
                    // If we try to re-select a previously selected district, the dropdown won't select anything :(
                    var idx = di == 1 ? districtNames.Count : 1;
                    await page.ClickAsync($"[role=listbox] [role=option]:nth-child({idx})");
                    await districtButton.ClickAsync();
                    await page.ClickAsync($"[role=listbox] [role=option]:nth-child({di})");
                });

                districts.Add(new(await retry.ExecuteAsync(async _ => await districtButton.TextContentAsync())));

                using var ds = await PushExecuteAsync(() => sectionButton.ClickAsync());
                var sectionNames = (await page.GetByRole(AriaRole.Option).AllAsync())
                    .Select(x => x.TextContentAsync().Result)
                    .ToList();

                for (var si = 1; si <= sectionNames.Count; si++)
                {
                    var section = sectionNames[si - 1];
                    using (var sc = await PushExecuteAsync(() => page.ClickAsync($"[role=listbox] [role=option]:nth-child({si})")))
                    {
                        districts[^1].Sections.Add(new(section));

                        using var ss = await PushExecuteAsync(() => circuitButton.ClickAsync());
                        var circuitNames = (await page.GetByRole(AriaRole.Option).AllAsync())
                            .Select(x => x.TextContentAsync().Result)
                            .ToList();

                        for (var ci = 1; ci <= circuitNames.Count; ci++)
                        {
                            var circuit = circuitNames[ci - 1];
                            using (var cc = await PushExecuteAsync(() => page.ClickAsync($"[role=listbox] [role=option]:nth-child({ci})")))
                            {
                                districts[^1].Sections[^1].Circuits.Add(new(await retry.ExecuteAsync(async _ => await circuitButton.TextContentAsync())));

                                using var cs = await PushExecuteAsync(() => localButton.ClickAsync());
                                var localNames = (await page.GetByRole(AriaRole.Option).AllAsync())
                                    .Select(x => x.TextContentAsync().Result)
                                    .ToList();

                                for (var li = 1; li <= localNames.Count; li++)
                                {
                                    var local = localNames[li - 1];
                                    using (var lc = await PushExecuteAsync(() => page.ClickAsync($"[role=listbox] [role=option]:nth-child({li})")))
                                    {
                                        districts[^1].Sections[^1].Circuits[^1].Institutions.Add(new(local));
                                        progress.Report($"{districts[^1].Name} - {districts[^1].Sections[^1].Name} - {districts[^1].Sections[^1].Circuits[^1].Name} - {districts[^1].Sections[^1].Circuits[^1].Institutions[^1].Name}");

                                        using var ls = await PushExecuteAsync(() => stationButton.ClickAsync());
                                        var stationNames = (await page.GetByRole(AriaRole.Option).AllAsync())
                                            .Select(x => x.TextContentAsync().Result)
                                            .ToList();

                                        for (var ti = 1; ti <= stationNames.Count; ti++)
                                        {
                                            var code = stationNames[ti - 1];
                                            using var tc = await PushExecuteAsync(() => page.ClickAsync($"[role=listbox] [role=option]:nth-child({ti})"));
                                            var url = await retry.ExecuteAsync(async _ => await page.GetByText("Aplicar filtros").GetAttributeAsync("href"));

                                            Debug.Assert(code != null && url != null);
                                            districts[^1].Sections[^1].Circuits[^1].Institutions[^1].Stations.Add(new(code, url));

                                            await retry.ExecuteAsync(async _ => await stationButton.ClickAsync());

                                            var districtFile = Path.Combine(baseDir, "telegram", $"district-{code[..2]}.json{(zip ? "gz" : "")}");
                                            if (File.Exists(districtFile))
                                            {
                                                Result(0, $"Distrito {districts[^1].Name} ya esta indexado");
                                                return;
                                            }
                                        }
                                    }

                                    // Reset filters since previous dropdowns may now be stale/filtered.
                                    await ResetFilters();
                                }
                            }

                            await ResetFilters();
                        }

                    }
                    await ResetFilters();
                }

                var districtId = districts[^1].Sections[0].Circuits[0].Institutions[0].Stations[0].Code[..2];

                await JsonFile.SerializeAsync(
                    districts[^1],
                    Path.Combine(baseDir, "telegram", $"district-{districtId}.json"), 
                    zip);

                break;
            }
        }

        class Poper(Stack<Func<Task>> stack) : IDisposable
        {
            bool disposed;
            public void Dispose()
            {
                if (!disposed)
                {
                    disposed = true;
                    stack.Pop();
                }
            }
        }
    }
}
