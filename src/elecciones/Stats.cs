using System.Diagnostics;
using MathNet.Numerics.Statistics;

namespace MenosRelato;

/// <summary>
/// Useful statistics by party and stats type.
/// </summary>
public class Stats
{
    /// <summary>
    /// This is the sum of all values divided by the number of values. 
    /// It gives a measure of the central location of the data.
    /// </summary>
    public Dictionary<string, double> Mean { get; init; } = new();
    /// <summary>
    /// This is the middle value in a sorted list of values. 
    /// It divides the data into two halves and is less affected by outliers than the mean.
    /// </summary>
    public Dictionary<string, double> Median { get; init; } = new();
    /// <summary>
    /// First quartile (25th percentile)
    /// </summary>
    public Dictionary<string, double> LowerQuartile { get; init; } = new();
    /// <summary>
    /// Third quartile (75th percentile)
    /// </summary>
    public Dictionary<string, double> UpperQuartile { get; init; } = new();
    /// <summary>
    /// The IQR is the range between the first quartile (25th percentile) and the 
    /// third quartile (75th percentile), and is used to measure statistical dispersion.
    /// </summary>
    public Dictionary<string, double> InterquartileRange { get; init; } = new();
    /// <summary>
    /// This measures the amount of variation or dispersion in the set of values. 
    /// A low standard deviation indicates that values are close to the mean, while 
    /// a high standard deviation indicates that the values are spread out over a wider range.
    /// </summary>
    public Dictionary<string, double> StandardDeviation { get; init; } = new();
    /// <summary>
    ///  This is the square of the standard deviation. It gives a measure of how the data is spread out around the mean.
    /// </summary>
    public Dictionary<string, double> Variance { get; init; } = new();

    /// <summary>
    /// Calculates stats for the given telegrams.
    /// </summary>
    public static Stats Calculate(IEnumerable<Telegram> telegrams)
    {
        var stats = new Stats();

        foreach (var group in telegrams.SelectMany(x => x.Parties).GroupBy(x => x.Name))
        {
            var values = group.Select(x => x.Percentage).ToArray();
            stats.Mean[group.Key] = Truncate(Statistics.Mean(values));
            stats.LowerQuartile[group.Key] = Truncate(Statistics.LowerQuartile(values));
            stats.UpperQuartile[group.Key] = Truncate(Statistics.UpperQuartile(values));
            stats.StandardDeviation[group.Key] = Truncate(Statistics.StandardDeviation(values));
            stats.Variance[group.Key] = Truncate(Statistics.Variance(values));
            stats.Median[group.Key] = Truncate(Statistics.Median(values));
            stats.InterquartileRange[group.Key] = Truncate(Statistics.InterquartileRange(values));
        }

        void KindStats(string kind, double[] values)
        {
            Debug.Assert(stats != null);
            stats.Mean[kind] = Truncate(Statistics.Mean(values));
            stats.LowerQuartile[kind] = Truncate(Statistics.LowerQuartile(values));
            stats.UpperQuartile[kind] = Truncate(Statistics.UpperQuartile(values));
            stats.StandardDeviation[kind] = Truncate(Statistics.StandardDeviation(values));
            stats.Variance[kind] = Truncate(Statistics.Variance(values));
            stats.Median[kind] = Truncate(Statistics.Median(values));
            stats.InterquartileRange[kind] = Truncate(Statistics.InterquartileRange(values));
        }

        KindStats(BallotKind.Blank, telegrams.Select(x => x.Station.BlankPercentage).ToArray());
        KindStats(BallotKind.Null, telegrams.Select(x => x.Station.NullPercentage).ToArray());
        KindStats(BallotKind.Appealed, telegrams.Select(x => x.Station.AppealedPercentage).ToArray());
        KindStats(BallotKind.Contested, telegrams.Select(x => x.Station.ContestedPercentage).ToArray());
        KindStats(BallotKind.Command, telegrams.Select(x => x.Station.CommandPercentage).ToArray());

        return stats;
    }

    static double Truncate(double value) => Math.Truncate(100 * value) / 100;
}

public static class StatsExtensions
{
    public static IEnumerable<Telegram> FindAnomalies(this Stats stats, IEnumerable<Telegram> telegrams, string anomaly)
    {
        foreach (var party in telegrams.SelectMany(x => x.Parties.Select(p => new { Telegram = x, Party = p })).GroupBy(x => x.Party.Name))
        {
            var values = party.Select(x => x.Party.Percentage).ToArray();
            var lowerBoundary = stats.LowerQuartile[party.Key] - (1.5 * stats.InterquartileRange[party.Key]);
            var upperBoundary = stats.UpperQuartile[party.Key] + (1.5 * stats.InterquartileRange[party.Key]);

            foreach (var station in party)
            {
                if (station.Party.Percentage < lowerBoundary || station.Party.Percentage > upperBoundary)
                {
                    yield return station.Telegram with { Stats = stats, Anomaly = anomaly };
                }
            }
        }
    }
}