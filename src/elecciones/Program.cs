using MenosRelato.Agent;
using MenosRelato.Commands;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly.Retry;
using Polly;
using Spectre.Console.Cli;
using static Spectre.Console.AnsiConsole;
using MenosRelato;
using Spectre.Console;
using Microsoft.Playwright;
using NuGet.Common;
using System.Net;
using Mono.Options;

var config = new ConfigurationManager()
    .AddUserSecrets(ThisAssembly.Project.UserSecretsId)
    .AddEnvironmentVariables()
    .Build();

string? proxy = default;
bool advanced = false;

new OptionSet
{
    { "proxy=", x => proxy = x },
    { "advanced", x => advanced = true },
}.Parse(args);

var services = new ServiceCollection()
    .AddSingleton<IConfiguration>(config)
    .AddAsyncLazy()
    .AddSingleton(_ => Playwright.CreateAsync())
    .AddSingleton(async sp =>
    {
        var playwright = await sp.GetRequiredService<AsyncLazy<IPlaywright>>();
        return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            ExecutablePath = Chromium.Path,
            // Headless doesn't work for some reason.
            Headless = false,
            Args = new[] { "--start-maximized" }
        });
    })
    .AddSingleton(_ => new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = int.MaxValue,
            BackoffType = DelayBackoffType.Linear,
            Delay = TimeSpan.FromSeconds(2),
            UseJitter = true,
            OnRetry = x =>
            {
                MarkupLine($"[red]x[/] Reintento #{x.AttemptNumber + 1}");
                return ValueTask.CompletedTask;
            },
        })
        .Build())
    .AddSingleton<IAgentService>(sp => new CachingAgentService(
        new CloudAgentService(sp.GetRequiredService<IConfiguration>())))
    .AddHttpClient()
        .ConfigureHttpClientDefaults(c => c
        .ConfigureHttpClient(http => http.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.UserAgent))
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            Proxy = proxy is not null ? new WebProxy(proxy) : null,
        }))
    .AddServices();

var app = new CommandApp(new TypeRegistrar(services));
// Register the app itself so commands can execute other commands
services.AddSingleton<ICommandApp>(app);

app.Configure(config =>
{
    config.SetApplicationName(ThisAssembly.Project.ToolCommandName);
    config.AddCommand<DatasetCommand>("dataset");
    config.AddCommand<PrepareCommand>("prepare").Advanced(advanced);
    config.AddCommand<DatabaseCommand>("db").IsHidden();
    config.AddCommand<TelegramCommand>("telegram").Advanced(advanced);
    config.AddCommand<UploadCommand>("upload").Advanced(advanced);
    config.AddCommand<DownloadCommand>("download").WithExample(["download --district 21"]); ;
    config.AddCommand<SliceCommand>("slice").WithExample(["slice --format csv --district 2 --district 21"]);

#if DEBUG
    config.PropagateExceptions();
#endif
});

#if DEBUG
if (args.Length == 0)
{
    var command = Prompt(
        new SelectionPrompt<string>()
            .Title("Command to run:")
            .AddChoices([
                "prepare",
                "download",
                "db",
                "help"
            ]));

    args = new[] { command };
}
#endif

return app.Run(args);