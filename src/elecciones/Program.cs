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

var config = new ConfigurationManager()
    .AddUserSecrets(ThisAssembly.Project.UserSecretsId)
    .AddEnvironmentVariables()
    .Build();

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
            Headless = args.Contains("--headless")
        });
    })
    .AddSingleton(_ => new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = int.MaxValue,
            BackoffType = DelayBackoffType.Linear,
            Delay = TimeSpan.FromSeconds(5),
            UseJitter = true,
            OnRetry = x =>
            {
                MarkupLine($"[red]x[/] Reintento #{x.AttemptNumber + 1}");
                return ValueTask.CompletedTask;
            },
        })
        //.AddTimeout(TimeSpan.FromSeconds(20))
        .Build())
    .AddSingleton<IAgentService>(sp => new CachingAgentService(
        new CloudAgentService(sp.GetRequiredService<IConfiguration>())))
    .AddHttpClient().ConfigureHttpClientDefaults(c => c.ConfigureHttpClient(
        http =>
        {
            http.BaseAddress = Constants.BaseAddress;
            http.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.UserAgent);
        }));

var app = new CommandApp(new TypeRegistrar(services));
// Register the app itself so commands can execute other commands
services.AddSingleton<ICommandApp>(app);

app.Configure(config =>
{
    config.SetApplicationName(ThisAssembly.Project.ToolCommandName);
    config.AddCommand<DownloadCommand>("download");
    config.AddCommand<PrepareCommand>("prepare");
    config.AddCommand<DatabaseCommand>("db").IsHidden();
    config.AddCommand<TelegramCommand>("telegram");
    config.AddCommand<GzipCommand>("gzip").IsHidden();
    config.AddCommand<UploadCommand>("upload");

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