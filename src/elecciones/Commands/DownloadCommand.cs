using System.ComponentModel;
using Azure.Storage.DataMovement;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage.DataMovement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Azure.Storage.Blobs;
using Spectre.Console;
using Spectre.Console.Cli;
using static Spectre.Console.AnsiConsole;
using Azure.Storage.DataMovement.Blobs;
using Azure.Storage.DataMovement.Models;

namespace MenosRelato.Commands;

[Description(@"Descargar los resultados en formato JSON y telegramas con su metadata.
Opcionalmente filtrar por distrito(s).")]
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
        [DefaultValue(false)]
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
                new TaskDescriptionColumn { Alignment = Justify.Left },
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn(),
            ]);

        // Always download results.

        await progress.StartAsync(async ctx =>
        {
            var baseDir = $"{settings.Year}/{settings.Election.ToLowerInvariant()}";
            var tasks = new List<Task>
            {
                Task.Run(() => DownloadFile(ctx, container, $"{baseDir}/{Constants.ResultsFile}")),
            };

            if (!settings.ResultsOnly)
            {
                if (settings.Districts.Length == 0)
                {
                    tasks.Add(Task.Run(() => DownloadDistrict(ctx, container, settings)));

                    var blobs = container.ListBlobs($"{baseDir}/telegram/district-");
                    tasks.AddRange(blobs.OfType<ICloudBlob>().Select(x => Task.Run(() =>
                        DownloadFile(ctx, container, x.Name))));
                }
                else
                {
                    tasks.AddRange(settings.Districts.Select(x => Task.Run(() 
                        => DownloadDistrict(ctx, container, settings, x))));

                    tasks.AddRange(settings.Districts.Select(x => Task.Run(() =>
                        DownloadFile(ctx, container, $"{baseDir}/telegram/district-{x:D2}.json.gz"))));
                }
            }

            await Task.WhenAll(tasks);
        });

        MarkupLine("[green]✓[/] Completado");

        return 0;
    }

    static async Task DownloadFile(ProgressContext ctx, CloudBlobContainer container, string relativePath)
    {
        var name = Path.GetFileName(relativePath);
        var task = ctx.AddTask($"Chequeando {name}", new ProgressTaskSettings { MaxValue = 100 });
        task.StartTask();

        var blob = container.GetBlockBlobReference(relativePath);
        await blob.FetchAttributesAsync();
        task.MaxValue = blob.Properties.Length;

        var target = Path.GetFullPath(Path.Combine(Constants.DefaultCacheDir, relativePath));
        if (await blob.IsSameAsync(target) == true)
        {
            task.Value = task.MaxValue;
            task.StopTask();
            return;
        }

        task.Description = $"Descargando {name}";

        await Microsoft.Azure.Storage.DataMovement.TransferManager.DownloadAsync(
            blob,
            target,
            new DownloadOptions(),
            new SingleTransferContext
            {
                ProgressHandler = new Progress<TransferStatus>((x) => task.Value(x.BytesTransferred))
            });

        task.Value = task.MaxValue;
        task.StopTask();
    }

    static async Task DownloadDistrict(ProgressContext ctx, CloudBlobContainer blobContainer, StorageSettings settings, int? id = null)
    {
        await Task.CompletedTask;

        var suffix = id is null ? "" : $"del distrito {id}";
        var dir = $"telegram";
        if (id is not null)
            dir += $"/{id}";

        var task = ctx.AddTask($"Calculando cantidad de telegramas {suffix}", new ProgressTaskSettings { MaxValue = 100 });
        task.StartTask();

        var blobClient = new BlobServiceClient(blobContainer.ServiceClient.BaseUri);
        var container = blobClient.GetBlobContainerClient(blobContainer.Name);

        var list = container
            .GetBlobs(prefix: $"{settings.Year}/{settings.Election.ToLowerInvariant()}/{dir}");

        task.MaxValue = list.Count();
        task.Description = $"Descargando {task.MaxValue} telegramas {suffix}";

        var manager = new Azure.Storage.DataMovement.TransferManager(new()
        {
            ErrorHandling = ErrorHandlingBehavior.StopOnAllFailures,
        });

        var options = new TransferOptions
        {
            CreateMode = StorageResourceCreateMode.Skip,
            ProgressHandler = new Progress<StorageTransferProgress>(progress =>
                task.Value = progress.CompletedCount + progress.SkippedCount)
        };

        //options.TransferSkipped += args =>
        //{
        //    return Task.CompletedTask;
        //};
        //options.TransferFailed += args =>
        //{
        //    return Task.CompletedTask;
        //};

        var localDir = Path.GetFullPath(Path.Combine(settings.BaseDir, dir));

        var transfer = await manager.StartTransferAsync(
            new BlobStorageResourceContainer(
                container,
                new BlobStorageResourceContainerOptions
                {
                    DirectoryPrefix = $"{settings.Year}/{settings.Election.ToLowerInvariant()}/{dir}",
                }),
            new LocalDirectoryResource(localDir),
            options);

        await transfer.AwaitCompletion();

        task.Value = task.MaxValue;
        task.StopTask();
    }

    static async Task<bool> ShouldOverwriteAsync(object source, object destination)
    {
        if (source is not ICloudBlob blob ||
            destination is not string destinationFile)
            return true;

        return await blob.IsSameAsync(destinationFile) == false;
    }

    class LocalDirectoryResource(string path) : LocalDirectoryStorageResourceContainer(path.Replace('\\', '/'))
    {
        public override StorageResourceSingle GetChildStorageResource(string childPath)
        {
            // If it contains a path dir, combine
            //if (childPath.Split('/').Length > 1)
                return new LocalFileResource(System.IO.Path.Combine(Path, childPath.TrimStart('/', '\\')));

            // Otherwise, consider it a root file. This only applies to our telegram structure though.
            //return new LocalFileResource(Path + "." + childPath);
        }

        class LocalFileResource(string path) : LocalFileStorageResource(path.Replace('\\', '/'))
        {
        }
    }
}
