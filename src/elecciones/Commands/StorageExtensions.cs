using System.Security.Cryptography;
using Microsoft.Azure.Storage.Blob;

namespace MenosRelato.Commands;

public static class StorageExtensions
{
    public static async Task<bool> IsSameAsync(this ICloudBlob blob, string file)
    {
        if (!File.Exists(file))
            return false;

        await blob.FetchAttributesAsync();

        // Quickly check if the file is not the same size and exit, since hashing is more expensive
        if (new FileInfo(file).Length != blob.Properties.Length)
        {
            return false;
        }

        var targetHash = blob.Properties.ContentMD5;

        // Calculate MD5 of sourceFile
        using var stream = File.OpenRead(file);
        var sourceHash = Convert.ToBase64String(await MD5.HashDataAsync(stream));

        return sourceHash == targetHash;
    }
}
