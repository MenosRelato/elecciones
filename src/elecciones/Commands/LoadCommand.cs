﻿using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Dapper;
using Humanizer;
using Microsoft.Data.Sqlite;
using Spectre.Console.Cli;
using Superpower;
using Superpower.Parsers;
using static MenosRelato.Results;
using static Spectre.Console.AnsiConsole;

namespace MenosRelato.Commands;

[Description("Cargar el dataset completo de resultados a una base de datos SQLite.")]
internal class LoadCommand : AsyncCommand<LoadCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("-r|--reset")]
        [Description("Recrear la base de datos completa")]
        public bool Reset { get; set; }
    }

    readonly string csvdir = Path.Combine(Constants.DefaultCacheDir, "datos", "csv");

    //"año","distrito_id","distrito_nombre","seccionprovincial_id","seccionprovincial_nombre","seccion_id","seccion_nombre"
    record ElectoralSection(
        [property: JsonPropertyName("año")] int Year,
        [property: JsonPropertyName("distrito_id")] int DistrictId,
        [property: JsonPropertyName("distrito_nombre")] string DistrictName,
        [property: JsonPropertyName("seccionprovincial_id")] int ProvincialId,
        [property: JsonPropertyName("seccionprovincial_nombre")] string ProvincialName,
        [property: JsonPropertyName("seccion_id")] int SectionId,
        [property: JsonPropertyName("seccion_nombre")] string SectionName);

    // "año","eleccion_tipo","recuento_tipo","padron_tipo",
    // "distrito_id","distrito_nombre","seccionprovincial_id","seccionprovincial_nombre","seccion_id","seccion_nombre",
    // "circuito_id","circuito_nombre","mesa_id","mesa_tipo","mesa_electores","cargo_id","cargo_nombre","agrupacion_id","agrupacion_nombre","lista_numero","lista_nombre",
    // "votos_tipo","votos_cantidad"
    record Ballot(
        [property: JsonPropertyName("año")] int Year,
        [property: JsonPropertyName("eleccion_tipo")] string Election,
        [property: JsonPropertyName("distrito_id")] int DistrictId,
        [property: JsonPropertyName("distrito_nombre")] string DistrictName,
        [property: JsonPropertyName("seccionprovincial_id")] int ProvincialId,
        [property: JsonPropertyName("seccionprovincial_nombre")] string ProvincialName,
        [property: JsonPropertyName("seccion_id")] int SectionId,
        [property: JsonPropertyName("seccion_nombre")] string SectionName,
        [property: JsonPropertyName("circuito_id")] string CircuitId,
        [property: JsonPropertyName("circuito_nombre")] string? CircuitName,
        [property: JsonPropertyName("mesa_id")] int Booth,
        [property: JsonPropertyName("mesa_electores")] int Electors,
        [property: JsonPropertyName("cargo_id")] int PositionId,
        [property: JsonPropertyName("cargo_nombre")] string PositionName,
        [property: JsonPropertyName("agrupacion_id")] int? PartyId,
        [property: JsonPropertyName("agrupacion_nombre")] string? PartyName,
        [property: JsonPropertyName("lista_numero")] int? ListId,
        [property: JsonPropertyName("lista_nombre")] string? ListName,
        [property: JsonPropertyName("votos_tipo")] string Kind,
        [property: JsonPropertyName("votos_cantidad")] int Count);

    // "2023","1","Ciudad Autónoma de Buenos Aires","0","","1","Comuna 01"
    TextParser<string[]> lineParser =
        from value in QuotedString.CStyle.ManyDelimitedBy(Character.EqualTo(','))
        select value;

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (!Directory.Exists(csvdir))
            return Error("No hay dataset descargado a procesar.");

        var ambitos = Directory.EnumerateFiles(csvdir, "*.csv").FirstOrDefault(x => Path.GetFileName(x).StartsWith("AmbitosElectorales"));
        if (ambitos == null)
            return Error("No se encontraron ambitos electorales a cargar.");

        var results = Directory.EnumerateFiles(csvdir, "*.csv").FirstOrDefault(x => Path.GetFileName(x).StartsWith("ResultadosElectorales"));
        if (results == null)
            return Error("No se encontraron resultados electorales a cargar.");

        var es = CultureInfo.GetCultureInfo("es-AR");

        var dbFile = Path.Combine(Constants.DefaultCacheDir, "elecciones.db");
        if (settings.Reset && File.Exists(dbFile))
        {
            File.Delete(dbFile);
        }

        await using var db = new SqliteConnection($"Data Source={dbFile}");

        if (File.Exists(dbFile))
        {
            await db.OpenAsync();
        }
        else
        {
            // Will automatically create from scratch if it doesn't exist.
            await db.OpenAsync();
            foreach (var sql in EmbeddedResource.GetContent("db.sql").Split(';'))
            {
                db.Execute(sql.Trim().TrimEnd(';'));
            }
        }

        var first = true;
        var header = Array.Empty<string>();
        await foreach (var line in File.ReadLinesAsync(ambitos, Encoding.UTF8))
        {
            if (first)
            {
                first = false;
                header = lineParser.Parse(line).Select(x => x.Trim('"')).ToArray();
                continue;
            }

            var values = lineParser.Parse(line);
            var value = CsvSerializer.Deserialize<ElectoralSection>(header, line);
            Debug.Assert(value != null);

            if (await db.QueryFirstOrDefaultAsync<int?>(
                    "SELECT rowid FROM Section WHERE DistrictId = @DistrictId AND ProvincialId = @ProvincialId and SectionId = @SectionId",
                    value) is not { })
            {
                await db.ExecuteAsync(
                    "INSERT INTO Section (DistrictId, ProvincialId, SectionId, DistrictName, ProvincialName, SectionName) VALUES (@DistrictId, @ProvincialId, @SectionId, @DistrictName, @ProvincialName, @SectionName)",
                    value);
            }
        }

        await Status().StartAsync("Cargando votos", async ctx =>
        {
            first = true;
            long count = 0;
            long total = -1; // discount header
            await foreach (var line in File.ReadLinesAsync(results, Encoding.UTF8))
            {
                total++;
                ctx.Status = $"Contando votos {total:N0}";
            }

            var watch = Stopwatch.StartNew();
            await foreach (var line in File.ReadLinesAsync(results, Encoding.UTF8))
            {
                if (first)
                {
                    first = false;
                    header = lineParser.Parse(line).Select(x => x.Trim('"')).ToArray();
                    continue;
                }

                count++;
                var remaining = TimeSpan.FromTicks(watch.Elapsed.Ticks * (total - count) / count);

                ctx.Status = $"Cargando votos {count:N0} de {total:N0} (faltan {remaining.Humanize(culture: es)})";

                var values = lineParser.Parse(line);
                var value = CsvSerializer.Deserialize<Ballot>(header, line);
                Debug.Assert(value != null);

                if (await db.QueryFirstOrDefaultAsync<int?>(
                                    "SELECT Id FROM Section WHERE DistrictId = @DistrictId AND ProvincialId = @ProvincialId and SectionId = @SectionId",
                                    value) is not { } section)
                    throw new ArgumentException($"No se encontro ambito electoral correspondiente a {value.DistrictName} {value.ProvincialName} {value.SectionName}");

                if (await db.QueryFirstOrDefaultAsync<int?>(
                        "SELECT Id FROM Circuit WHERE CircuitId = @CircuitId AND SectionId = @section",
                        new { value.CircuitId, section }) is not { } circuit)
                {
                    await db.ExecuteAsync(
                        "INSERT INTO Circuit (CircuitId, CircuitName, SectionId) VALUES (@CircuitId, @CircuitName, @section)",
                        new { value.CircuitId, value.CircuitName, section });

                    circuit = await db.ExecuteScalarAsync<int>("select last_insert_rowid()");
                }

                if (await db.QueryFirstOrDefaultAsync<int?>(
                        "SELECT Id FROM Party WHERE PartyId = @PartyId AND ListId = @ListId",
                        value) is not { } party)
                {
                    await db.ExecuteAsync(
                        "INSERT INTO Party (PartyId, PartyName, ListId, ListName) VALUES (@PartyId, @PartyName, @ListId, @ListName)",
                        value);

                    party = await db.ExecuteScalarAsync<int>("select last_insert_rowid()");
                }

                if (await db.QueryFirstOrDefaultAsync<int?>(
                        "SELECT Id FROM Position WHERE Id = @PositionId",
                        value) is not { })
                {
                    await db.ExecuteAsync(
                        "INSERT INTO Position (Id, Name) VALUES (@PositionId, @PositionName)",
                        value);
                }

                // switch expression to assign Kind (enum) from value.Kind (string)
                var kind = value.Kind switch
                {
                    "POSITIVO" => Kind.Positive,
                    "EN BLANCO" => Kind.Blank,
                    "NULO" => Kind.Null,
                    "IMPUGNADO" => Kind.Contested,
                    "RECURRIDO" => Kind.Appealed,
                    "COMANDO" => Kind.Command,
                    _ => throw new ArgumentException($"Unexpected ballot kind {value.Kind}"),
                };

                var election = value.Election switch
                {
                    "PASO" => Election.Primary,
                    "GENERAL" => Election.General,
                    _ => throw new ArgumentException($"Unexpected election value {value.Election}"),
                };

                if (await db.QueryFirstOrDefaultAsync<int?>(
                        "SELECT Id FROM Ballot WHERE Year = @Year AND Election = @election AND Circuit = @circuit AND Booth = @Booth AND Position = @PositionId AND Party = @party AND Kind = @kind",
                        new { value.Year, election, circuit, value.Booth, value.PositionId, party, kind }) is not { } ballot)
                {
                    await db.ExecuteAsync(
                        "INSERT INTO Ballot (Year, Election, Circuit, Booth, Electors, Position, Party, Kind, Count) VALUES (@Year, @election, @circuit, @Booth, @Electors, @PositionId, @party, @kind, @Count)",
                        new { value.Year, election, circuit, value.Booth, value.Electors, value.PositionId, party, kind, value.Count });

                    ballot = await db.ExecuteScalarAsync<int>("select last_insert_rowid()");
                }
            }
        });

        return Result(0, "Done");
    }
}
