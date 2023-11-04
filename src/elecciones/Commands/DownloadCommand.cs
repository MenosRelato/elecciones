using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage.DataMovement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using static Spectre.Console.AnsiConsole;

namespace MenosRelato.Commands;

public class DownloadCommand : AsyncCommand<DownloadCommand.Settings>
{
    [Service(ServiceLifetime.Transient)]
    public class Settings(IConfiguration configuration) : StorageSettings(configuration)
    {
        [CommandOption("-d|--district <VALUES>")]
        [Description("Distrito(s) a incluir en la descarga de telegramas")]
        public int[] Districts { get; set; } = [];

        [CommandOption("-r|--results")]
        [Description("Descargar solo los resultados, no los telegramas")]
        [DefaultValue(false)]
        public bool ResultsOnly { get; set; } = false;

        [CommandOption("-o|--open")]
        [Description("Abrir la carpeta de descarga al finalizar")]
        [DefaultValue(true)]
        public bool Open { get; set; } = false;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var blobClient = CloudStorageAccount.TryParse(settings.Storage, out var account) ?
            account.CreateCloudBlobClient() :
            new CloudBlobClient(new Uri(settings.StorageValues["BlobEndpoint"]));

        var container = blobClient.GetContainerReference("elecciones");
        var electionDir = settings.BaseDir;

        var progress = Progress()
            .AutoClear(false)
            .Columns(
            [
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn(),
            ]);

        // Always download results.

        await progress.StartAsync(async ctx =>
        {
            var tasks = new List<Task>();

            var resultsTask = ctx.AddTask($"Descargando {Constants.ResultsFile}", new ProgressTaskSettings { MaxValue = 100 });
            resultsTask.StartTask();
            var blobName = $"{settings.Year}/{settings.Election.ToLowerInvariant()}/{Constants.ResultsFile}";
            var resultsBlob = container.GetBlockBlobReference(blobName);
            await resultsBlob.FetchAttributesAsync();
            resultsTask.MaxValue = resultsBlob.Properties.Length;

            tasks.Add(Task.Run(() => TransferManager.DownloadAsync(
                resultsBlob, 
                Path.Combine(settings.BaseDir, Constants.ResultsFile),
                new DownloadOptions(),
                new SingleTransferContext
                {
                    ShouldOverwriteCallbackAsync = ShouldOverwriteAsync,
                    ProgressHandler = new Progress<TransferStatus>((x) => resultsTask.Value(x.BytesTransferred))
                }).ContinueWith(_ => resultsTask.Value = resultsTask.MaxValue)));

            if (!settings.ResultsOnly)
            {
                if (settings.Districts.Length == 0)
                {
                    var telegramsTask = ctx.AddTask("Calculando cantidad de telegramas", new ProgressTaskSettings { MaxValue = 100 });
                    var list = container.GetDirectoryReference($"{settings.Year}/{settings.Election.ToLowerInvariant()}/telegrama")
                        .ListBlobs(useFlatBlobListing: true);

                    telegramsTask.MaxValue = list.Count();
                    telegramsTask.Description = "Descargando telegramas";

                    tasks.Add(Task.Run(() => TransferManager.DownloadDirectoryAsync(
                        container.GetDirectoryReference($"{settings.Year}/{settings.Election.ToLowerInvariant()}/telegrama"),
                        Path.Combine(settings.BaseDir, "telegrama"),
                        new DownloadDirectoryOptions { Recursive = true },
                        new DirectoryTransferContext
                        {
                            ShouldOverwriteCallbackAsync = async (object source, object destination) =>
                            {
                                if (source is not ICloudBlob blob ||
                                    destination is not string destinationFile)
                                    return false;

                                var same = await blob.IsSameAsync(destinationFile);
                                if (same == false)
                                    // no files should be different.
                                    Debugger.Break();

                                return same == false;
                            },
                            ProgressHandler = new Progress<TransferStatus>(
                                (progress) => telegramsTask.Value = progress.NumberOfFilesTransferred + progress.NumberOfFilesSkipped)
                        })));
                }
            }

            await Task.WhenAll(tasks);
        });

        MarkupLine("[green]✓[/] Completado");

        return 0;
    }

    async Task<bool> ShouldOverwriteAsync(object source, object destination)
    {
        if (source is not ICloudBlob blob ||
            destination is not string destinationFile)
            return true;

        return await blob.IsSameAsync(destinationFile) == false;
    }
}
