namespace MenosRelato;

public static class Constants
{
    /// <summary>
    /// Anonymous access connection string to Azure Storage. For authorized access, provide the -s|--storage option or 
    /// ELECTION_STORAGE envvar.
    /// </summary>
    public const string AzureStorage = "DefaultEndpointsProtocol=https;AccountName=menosrelato;BlobEndpoint=https://menosrelato.blob.core.windows.net/;QueueEndpoint=https://menosrelato.queue.core.windows.net/;TableEndpoint=https://menosrelato.table.core.windows.net/;FileEndpoint=https://menosrelato.file.core.windows.net/;";
    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/117.0.0.0 Safari/537.36 Edg/117.0.2045.41";
    public static string DefaultCacheDir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MenosRelato", "elecciones");

    static Constants() => Directory.CreateDirectory(DefaultCacheDir);
}
