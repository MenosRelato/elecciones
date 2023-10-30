﻿using System.ComponentModel;
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

        await Status().StartAsync($"Subiendo archivos", async ctx =>
        {
            var source = Path.Combine(settings.BaseDir, "telegrama");
            var size = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Sum(x => new FileInfo(x).Length);
            var progress = new DirectoryTransferContext
            {
                ShouldOverwriteCallbackAsync = async (sourcePath, destinationPath) =>
                {
                    if (destinationPath is not ICloudBlob blob || 
                        sourcePath is not string sourceFile)
                        return true;

                    await blob.FetchAttributesAsync();

                    // Quickly check if the file is the same size and exit, since hashing is more expensive
                    if (new FileInfo(sourceFile).Length == blob.Properties.Length)
                        return false;

                    var targetHash = blob.Properties.ContentMD5;

                    // Calculate MD5 of sourceFile
                    using var stream = File.OpenRead(sourceFile);
                    var sourceHash = Convert.ToBase64String(await MD5.HashDataAsync(stream));

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
                    (progress) => ctx.Status = $"Subiendo {source} ({progress.BytesTransferred.Bytes()} of {size.Bytes()})")
            };

            await TransferManager.UploadDirectoryAsync(
                source, 
                container.GetDirectoryReference(new DirectoryInfo(source).Name),
                new UploadDirectoryOptions {  Recursive = true }, 
                progress);

            foreach (var gz in Directory.EnumerateFiles(Constants.DefaultCacheDir, "*.gz"))
            {
                var blob = container.GetBlockBlobReference(Path.GetFileName(gz));
                await TransferManager.UploadAsync(gz, blob, 
                    new UploadOptions 
                    {  
                        DestinationAccessCondition = AccessCondition.GenerateIfNotExistsCondition()
                    }, 
                    new SingleTransferContext
                    {
                        ProgressHandler = progress.ProgressHandler,
                        ShouldOverwriteCallbackAsync = progress.ShouldOverwriteCallbackAsync,
                        SetAttributesCallbackAsync = progress.SetAttributesCallbackAsync,
                    });
            }
        });

        MarkupLine("[green]✓[/] Completado");

        return 0;
    }
}