using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace MenosRelato.Commands;

[Service(ServiceLifetime.Transient)]
public class ElectionSettings : CommandSettings
{
    [CommandOption("-e|--election")]
    [Description("Tipo de eleccion a cargar")]
    [DefaultValue(ElectionKind.General)]
    public ElectionKind Election { get; set; } = ElectionKind.General;

    [CommandOption("-y|--year")]
    [Description("Año de la eleccion a cargar")]
    [DefaultValue(2023)]
    public int Year { get; set; } = 2023;

    [CommandOption("--proxy")]
    [Description("Utilizar un proxy para HTTP")]
    public string? Proxy { get; set; }

    public string BaseDir => Path.Combine(Constants.DefaultCacheDir, Year.ToString(), Election.ToUserString().ToLowerInvariant());
}
