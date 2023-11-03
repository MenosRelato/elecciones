﻿using System.IO.Compression;
using System.Text.Json.Serialization;
using System.Text.Json;
using MathNet.Numerics.Statistics;
using MenosRelato.Commands;
using Newtonsoft.Json;
using MathNet.Numerics.Distributions;
using NuGet.Packaging;
using static System.Collections.Specialized.BitVector32;

namespace MenosRelato;

public class StatsTests : IClassFixture<ElectionFixture>
{
    readonly ElectionFixture fixture;
    readonly ITestOutputHelper output;

    public StatsTests(ElectionFixture fixture) => this.fixture = fixture;

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

    [Fact]
    public void Fraude()
    {
    }

    [Fact]
    public async Task Run()
    {
        Assert.NotEmpty(fixture.Election.Districts);

        foreach (var district in fixture.Election.Districts)
        {
            var stats = CalculateStats(district.GetBallots());
        }
    }

    public static Stats CalculateStats(IEnumerable<Ballot> ballots)
    {
        var stats = new Stats();
        foreach (var group in ballots.GroupBy(x => x.Party ?? x.Kind))
        {
            var values = group.Select(x => (double)x.Count).ToArray();
            stats.Mean[group.Key] = Statistics.Mean(values);
            stats.LowerQuartile[group.Key] = Statistics.LowerQuartile(values);
            stats.UpperQuartile[group.Key] = Statistics.UpperQuartile(values);
            stats.StandardDeviation[group.Key] = Statistics.StandardDeviation(values);
            stats.Variance[group.Key] = Statistics.Variance(values);
            stats.Median[group.Key] = Statistics.Median(values);
            stats.InterquartileRange[group.Key] = Statistics.InterquartileRange(values);
        }
        return stats;
    }
}