using System.ComponentModel;
using System.Security.Cryptography;
using Humanizer;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage.DataMovement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;
using static Spectre.Console.AnsiConsole;
using static MenosRelato.Results;
using Spectre.Console;

namespace MenosRelato.Commands;

// El usuario debe estar logoneado con azcopy login tambien.
// Asume que 7zip esta instalado tambien
[Description("Sube los datos descargados a Azure Blob storage")]
public partial class UploadCommand: AsyncCommand<UploadCommand.Settings>
{
    [Service(ServiceLifetime.Transient)]
    public class Settings(IConfiguration config) : StorageSettings(config)
    {
        public override Spectre.Console.ValidationResult Validate()
        {
            if (base.Validate() is var result && !result.Successful)
                return result;

            if (!CloudStorageAccount.TryParse(Storage, out _))
                return Spectre.Console.ValidationResult.Error("Por favor especificar una connexion de Azure Storage con permisos de escritura.");

            return Spectre.Console.ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var account = CloudStorageAccount.Parse(settings.Storage);
        var blobClient = account.CreateCloudBlobClient();
        var container = blobClient.GetContainerReference("elecciones");
        await container.CreateIfNotExistsAsync();
        await container.SetPermissionsAsync(new BlobContainerPermissions
        {
            PublicAccess = BlobContainerPublicAccessType.Container
        });

        var source = Path.Combine(settings.BaseDir);
        var size = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Where(x => Path.GetExtension(x) != ".csv")
            .Sum(x => new FileInfo(x).Length);

        var skipped = 0L;
        var electionDir = settings.BaseDir;
        var relativeDir = electionDir.Substring(Constants.DefaultCacheDir.Length + 1).Replace('\\', '/');

        await Progress()
            .AutoClear(false)
            .Columns(
            [
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),  
                new PercentageColumn(),   
                new RemainingTimeColumn(),
                new SpinnerColumn(),      
            ])
            .StartAsync(async ctx =>
        {
            var task = ctx.AddTask($"Subiendo {relativeDir} {size.Bytes()}", new ProgressTaskSettings { MaxValue = size });
            var progress = new DirectoryTransferContext
            {
                ShouldTransferCallbackAsync = (sourcePath, destinationPath) =>
                {
                    if (destinationPath is not ICloudBlob blob ||
                        sourcePath is not string sourceFile)
                        return Task.FromResult(false);

                    // Never transfer the raw unprocessed csv files from the dataset
                    if (Path.GetExtension(sourceFile) == ".csv")
                        return Task.FromResult(false);

                    // Let the overwrite callback do the hashing check
                    return Task.FromResult(true);
                },
                ShouldOverwriteCallbackAsync = async (sourcePath, destinationPath) =>
                {
                    if (destinationPath is not ICloudBlob blob || 
                        sourcePath is not string sourceFile)
                        return true;

                    await blob.FetchAttributesAsync();

                    // Quickly check if the file is the same size and exit, since hashing is more expensive
                    if (new FileInfo(sourceFile).Length == blob.Properties.Length)
                    {
                        skipped += blob.Properties.Length;
                        return false;
                    }

                    var targetHash = blob.Properties.ContentMD5;

                    // Calculate MD5 of sourceFile
                    using var stream = File.OpenRead(sourceFile);
                    var sourceHash = Convert.ToBase64String(await MD5.HashDataAsync(stream));

                    if (sourceHash == targetHash)
                        skipped += blob.Properties.Length;

                    return sourceHash != targetHash;
                },
                SetAttributesCallbackAsync = (sourcePath, destinationPath) =>
                {
                    if (destinationPath is not ICloudBlob blob ||
                        sourcePath is not string sourceFile)
                        return Task.CompletedTask;
                    
                    blob.Properties.ContentType = MimeTypes.GetMimeType(sourceFile);
                    return Task.CompletedTask;
                },
                ProgressHandler = new Progress<TransferStatus>(
                    (progress) => task.Value(progress.BytesTransferred + skipped))
            };

            await TransferManager.UploadDirectoryAsync(
                source, 
                container.GetDirectoryReference(source.Substring(Constants.DefaultCacheDir.Length + 1).Replace('\\', '/')),
                new UploadDirectoryOptions { Recursive = true }, 
                progress);
        });

        MarkupLine("[green]✓[/] Completado");

        return 0;
    }
}
