using System.Text.Json;
using System.Text.Json.Serialization;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using Superpower;

namespace MenosRelato;

public class StatsTests : IClassFixture<ElectionFixture>
{
    readonly ElectionFixture fixture;
    readonly ITestOutputHelper output;

    public StatsTests(ElectionFixture fixture, ITestOutputHelper output)
        => (this.fixture, this.output)
        = (fixture, output);

    /// <summary>
    /// Verifies our aggregates with the official results from https://resultados.gob.ar/elecciones/1/0/1/-1/-1#agrupaciones
    /// </summary>
    [Theory]
    [InlineData("PRESIDENTE Y VICE", "UNION POR LA PATRIA", 9_645_983)]
    [InlineData("PRESIDENTE Y VICE", "LA LIBERTAD AVANZA", 7_884_336)]
    [InlineData("PRESIDENTE Y VICE", "JUNTOS POR EL CAMBIO", 6_267_152)]
    [InlineData("PRESIDENTE Y VICE", "HACEMOS POR NUESTRO PAIS", 1_784_315)]
    [InlineData("PRESIDENTE Y VICE", "FRENTE DE IZQUIERDA Y DE TRABAJADORES - UNIDAD", 709_932)]
    public void VerifyResults(string position, string party, int expected)
    {
        var ballots = fixture.Election.GetBallots()
            .Where(b => b.Position == position && b.Party == party)
            .Sum(x => x.Count);

        Assert.Equal(expected, ballots);
    }

    [LocalFact]
    public void VerifyTelegrams()
    {
        // Requires all telegrams to be downloaded previously.

        // Values from: https://resultados.gob.ar/elecciones/1/0/1/-1/-1#agrupaciones
        var mesas = 104_520;
        var escrutadas = 102_976;
        var missing = mesas - escrutadas;

        var baseDir = Path.Combine(fixture.Settings.BaseDir, "telegram");
        var telegrams = Directory
            .EnumerateFiles(baseDir, "*.json.gz", SearchOption.AllDirectories)
            // Skip district files
            .Where(x => Path.GetDirectoryName(x[(baseDir.Length + 1)..])!.Length > 0)
            //.Select(x => (dynamic)JObject.Parse(GzipFile.ReadAllTextAsync(x).Result))
            .Select(x => Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(x)))
            //.Select(x => new { District = int.Parse(x[0..2]), Section = int.Parse(x[2..5]), Station = x })
            .ToHashSet();

        Assert.Equal(mesas, telegrams.Count);

        var stations = fixture.Election.Districts
            .SelectMany(d => d.Sections
            .SelectMany(s => s.Circuits
            .SelectMany(c => c.Stations)))
            .ToList();

        var wrong = stations
            .Where(s => !telegrams.Contains(s.Code!))
            .ToList();


        output.WriteLine(stations.Count.ToString());
        Assert.Empty(wrong);

        var notelegram = stations
            .Where(s => s.HasTelegram != true)
            .ToList();

        Assert.Equal(missing, notelegram.Count);
    }

    [LocalFact]
    public async Task VerifyAllStats()
    {
        var statsDir = Path.Combine(fixture.Settings.BaseDir, "stats");
        var options = new JsonSerializerOptions(ModelSerializer.Options)
        {
            PropertyNameCaseInsensitive = true,
            // Reusable string MUST be short-lived.
            Converters =
            {
                //new System.Text.Json.Serialization
                new ModelSerializer.ReusableStringConverter(),
                new ModelSerializer.DateTimeConverter(),
            },
        };

        foreach (var file in Directory
            .EnumerateFiles(statsDir, "*.json.gz", SearchOption.AllDirectories))
        {
            var data = await TextFile.ReadAllTextAsync(file);
            var telegram = JsonSerializer.Deserialize<Telegram>(data, options);

            Assert.NotNull(telegram);
            Assert.NotNull(telegram.District);
            Assert.NotNull(telegram.Section);
            Assert.NotNull(telegram.Circuit);
            Assert.NotNull(telegram.Local);
        }
    }

    class LocalIdConverter : JsonConverter<LocalId>
    {
        public override LocalId? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var name = reader.GetString();
            if (name == null)
                return null;

            return new LocalId("", name);
        }

        public override void Write(Utf8JsonWriter writer, LocalId value, JsonSerializerOptions options)
            => throw new NotImplementedException();
    }

    [LocalFact]
    public async Task UpgradeAllStats()
    {
        var statsDir = Path.Combine(fixture.Settings.BaseDir, "stats.1");
        var telegramsDir = Path.Combine(fixture.Settings.BaseDir, "telegram");
        var options = new JsonSerializerOptions(ModelSerializer.Options)
        {
            PropertyNameCaseInsensitive = true,
            // Reusable string MUST be short-lived.
            Converters =
            {
                //new System.Text.Json.Serialization
                new ModelSerializer.ReusableStringConverter(),
                new ModelSerializer.DateTimeConverter(),
                new LocalIdConverter(),
            },
        };

        foreach (var file in Directory
            .EnumerateFiles(statsDir, "*.json.gz", SearchOption.AllDirectories))
        {
            var telegram = JsonSerializer.Deserialize<Telegram>(await TextFile.ReadAllTextAsync(file), options);
            Assert.NotNull(telegram);

            if (!string.IsNullOrEmpty(telegram.Local.Id))
                continue;

            var tf = Path.Combine(telegramsDir, telegram.District.Id.ToString(), telegram.Section.Id.ToString(), $"{telegram.Id}.json.gz");

            Assert.True(File.Exists(tf));

            var data = await TextFile.ReadAllTextAsync(tf);
            var json = JObject.Parse(data);

            Assert.NotNull(json);

            var codigo = json.SelectToken("$.datos.fathers[0].codigo")?.ToString();

            Assert.NotNull(codigo);

            await ModelSerializer.SerializeAsync(telegram with { Local = new LocalId(codigo, telegram.Local.Name) }, file);
        }

    }

    [LocalFact]
    public async Task FromTelegrams()
    {
        var statsDir = Path.Combine(fixture.Settings.BaseDir, "stats");
        Assert.True(File.Exists(Path.Combine(fixture.Settings.BaseDir, "telegrams.json.gz")));

        var telegrams = await ModelSerializer.DeserializeAsync<List<Telegram>>(Path.Combine(fixture.Settings.BaseDir, "telegrams.json.gz"));
        Assert.NotNull(telegrams);

        var anomalies = new HashSet<Telegram>(EqualityComparer<Telegram>.Create((x, y) => x?.Id == y?.Id, x => x.Id.GetHashCode()));
        void AddAnomalies(IEnumerable<Telegram> telegrams)
        {
            foreach (var telegram in telegrams)
                anomalies?.Add(telegram);
        }

        foreach (var district in telegrams.Where(x => x.TelegramUrl != null).GroupBy(x => x.District))
        {
            foreach (var section in district.GroupBy(x => x.Section))
            {
                foreach (var circuit in section.GroupBy(x => x.Circuit))
                {
                    foreach (var local in circuit.GroupBy(x => x.Local))
                    {
                        AddAnomalies(Stats.Calculate(local).FindAnomalies(local, "Establecimiento"));
                    }
                    AddAnomalies(Stats.Calculate(circuit).FindAnomalies(circuit, "Circuito"));
                }
                AddAnomalies(Stats.Calculate(section).FindAnomalies(section, "Seccion"));
            }
            AddAnomalies(Stats.Calculate(district).FindAnomalies(district, "Distrito"));
        }

        Assert.NotEmpty(anomalies);

        var summary = anomalies.Select(x => new { x.Anomaly, x.Id, x.Local, x.Circuit, Section = x.Section?.Name, District = x.District?.Name })
            .GroupBy(x => x.Anomaly)
            .ToList();
    }
}
