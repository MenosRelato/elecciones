using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MenosRelato.Commands;

[Service(ServiceLifetime.Transient)]
public class StorageSettings(IConfiguration configuration) : ElectionSettings
{
    [Description(@"Conexion de Azure Storage a usar (connection string)")]
    [CommandOption("-s|--storage")]
    public string? Storage { get; set; }

    public IReadOnlyDictionary<string, string> StorageValues { get; private set; } = new Dictionary<string, string>();

    public override ValidationResult Validate()
    {
        if (base.Validate() is var result && !result.Successful)
            return result;

        if (Storage is not null)
            return ValidationResult.Success();

        var storage = configuration["ELECTION_STORAGE"];
        if (storage is not null)
            Storage = storage;
        else
            Storage = Constants.AzureStorageConnection;

        StorageValues = Storage.Split(';')
            .Select(x => x.Split('='))
            .Where(x => x.Length == 2)
            .ToDictionary(x => x[0], x => x[1])
            .AsReadOnly();

        return ValidationResult.Success();
    }
}
