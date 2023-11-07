using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MenosRelato;

public static class ElectionExtensions
{
    public static void AddRange<T>(this ICollection<T> collection, IEnumerable<T> values)
    {
        foreach (var value in values)
            collection.Add(value);
    }

    public static IEnumerable<Ballot> GetBallots(this Election election) => election.Districts.SelectMany(d => d.GetBallots());

    public static IEnumerable<Ballot> GetBallots(this District district) => district.Sections.SelectMany(x => x.GetBallots());

    public static IEnumerable<Ballot> GetBallots(this Section section) => section.Circuits.SelectMany(c => c.GetBallots());

    public static IEnumerable<Ballot> GetBallots(this Circuit circuit) => circuit.Stations.SelectMany(b => b.Ballots);
}

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
}

public record Election(int Year, string Kind)
{
    readonly IndexedCollection<string, Party> parties = new(x => x.Name);
    readonly IndexedCollection<int, Position> positions = new(x => x.Id);
    IndexedCollection<int, District>? districts;

    public string BaseUrl => "https://resultados.gob.ar";
    public string StorageUrl => Constants.AzureStorageUrl + "elecciones";

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public IList<Party> Parties => parties;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public IList<Position> Positions => positions;

    public IList<District> Districts
    {
        get => districts ??= new(x => x.Id, x => x.Election = this);
        set => Districts.AddRange(value);
    }
    
    public Stats? Stats { get; set; }
    public List<District>? Outliers { get; set; }

    public Party? GetOrAddParty(string? name)
    {
        if (name is null)
            return null;

        if (parties.TryGetValue(name, out var party))
            return party;

        party = new Party(name);
        parties.Add(party);
        return party;
    }

    public Position GetOrAddPosition(int id, string name)
    {
        if (positions.TryGetValue(id, out var position))
            return position;

        position = new Position(id, name);
        positions.Add(position);
        return position;
    }

    public District GetOrAddDistrict(int id, string name)
    {
        districts ??= new(x => x.Id, x => x.Election = this);
        if (districts.TryGetValue(id, out var district))
            return district;

        district = new District(id, name);
        districts.Add(district);
        return district;
    }
}

public static class ElectionKind
{
    public const string Primary = "PASO";
    public const string General = "GENERAL";
    public const string Ballotage = "BALOTAJE";
}

public static class BallotKind
{
    public const string Positive = "POSITIVO";
    public const string Blank = "EN BLANCO";
    public const string Null = "NULO";
    public const string Appealed = "RECURRIDO";
    public const string Contested = "IMPUGNADO";
    public const string Command = "COMANDO";
}

public record District(int Id, string Name)
{
    IndexedCollection<int, Section>? sections;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public Election? Election { get; set; }
    public Stats? Stats { get; set; }

    public IList<Section> Sections
    {
        get => sections ??= new(x => x.Id, x => x.District = this);
        set => (sections ??= new(x => x.Id, x => x.District = this)).AddRange(value);
    }

    public Section GetOrAddSection(int id, string name)
    {
        sections ??= new(x => x.Id, x => x.District = this);
        if (sections.TryGetValue(id, out var section))
            return section;

        section = new Section(id, name);
        sections.Add(section);
        return section;
    }
}

public record Section(int Id, string Name)
{
    IndexedCollection<string, Circuit>? circuits;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public District? District { get; set; }
    public Stats? Stats { get; set; }

    public IList<Circuit> Circuits
    {
        get => circuits ??= new(x => x.Id, x => x.Section = this);
        set => (circuits ??= new(x => x.Id, x => x.Section = this)).AddRange(value);
    }

    public Circuit GetOrAddCircuit(string id, string? name)
    {
        circuits ??= new(x => x.Id, x => x.Section = this);
        if (circuits.TryGetValue(id, out var circuit))
            return circuit;

        circuit = new Circuit(id, name);
        circuits.Add(circuit);
        return circuit;
    }
}

public record Circuit(string Id, string? Name)
{
    IndexedCollection<int, Station>? stations;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public Section? Section { get; set; }
    public Stats? Stats { get; set; }
    public List<Station>? Outliers { get; set; }

    public IList<Station> Stations
    {
        get => stations ??= new(x => x.Id, x => x.Circuit = this);
        set => (stations ??= new(x => x.Id, x => x.Circuit = this)).AddRange(value);
    }

    public Station GetOrAddStation(int id, int electors)
    {
        stations ??= new(x => x.Id, x => x.Circuit = this);
        if (stations.TryGetValue(id, out var station))
            return station;

        station = new Station(id, electors);
        stations.Add(station);
        return station;
    }
}

public record Party(string Name)
{
    public HashSet<string> Lists { get; } = new();

    public string? AddList(string? list)
    {
        if (list is null)
            return null;

        Lists.Add(list);
        return list;
    }
}

public record Position(int Id, string Name);

public record Station
{
    Lazy<bool> hasTelegram;
    ActionCollection<Ballot>? ballots;

    public Station(int id, int electors)
    {
        (Id, Electors) = (id, electors);
        hasTelegram = new(() => File.Exists(TelegramFile));
    }

    public int Id { get; }
    public int Electors { get; }

    /// <summary>
    /// Station code
    /// </summary>
    /// <remarks>
    /// Example: 0100100001X
    /// 01: District
    /// 001: Section
    /// 00001: Station
    /// X: fixed suffix
    /// </remarks>
    public string? Code => 
        Circuit?.Section?.District is null ? null : 
        $"{Circuit!.Section!.District!.Id:D2}{Circuit!.Section!.Id:D3}{Id:D5}X";

    public bool? HasTelegram => hasTelegram.Value;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string? TelegramFile =>
        Circuit?.Section?.District?.Election is null ? null :
        Path.Combine(
            Constants.DefaultCacheDir,
            Circuit!.Section!.District!.Election.Year.ToString(),
            Circuit!.Section!.District!.Election.Kind.ToLowerInvariant(),
            "telegram",
            Circuit!.Section!.District!.Id.ToString(),
            Circuit!.Section!.Id.ToString(),
            $"{Code}.tiff");

    public string? WebUrl { get; set; }
    public string? TelegramUrl { get; set; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public Circuit? Circuit { get; set; }

    public IList<Ballot> Ballots
    {
        get => ballots ??= new(x => x.Station = this);
        set => (ballots ??= new(x => x.Station = this)).AddRange(value);
    }
    
    public double Participation => Ballots.Sum(x => x.Count) / (double)Electors;

    public Ballot GetOrAddBallot(string kind, int count, string position, string? party, string? list)
    {
        if (Ballots.FirstOrDefault(b => b.Position == position && b.Party == party && b.List == list && b.Kind == kind) is { } ballot)
        {
            ballot = ballot with { Count = count };
            return ballot;
        }
        
        if (kind == BallotKind.Positive && party == null)
            throw new ArgumentException("Voto positivo sin partido.");

        ballot = new Ballot(kind, count, position, party, list);
        Debug.Assert(ballots != null);
        ballots.Add(ballot);
        return ballot;
    }
}

public record Ballot(string Kind, int Count, string Position, string? Party, string? List)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public Station? Station { get; set; }
}

public record Telegram(string Id, DateTime Date, string Url, 
    DistrictId? District, SectionId? Section, string? Circuit, string Local,
    StationInfo Station, PartyInfo[] Parties, UserId User)
{
    public string? Anomaly { get; set; }
    public string? WebUrl { get; set; }
    public string? TelegramUrl { get; set; }
}

public record DistrictId(int Id, string Name);
public record SectionId(int Id, string Name);
public record StationInfo(int Census, int Electors, int Envelopes, 
    int TotalVotes, int Valid, int Affirmative, 
    int Blank, int Null, int Appealed, int Contested, int Command,
    [property: JsonPropertyName("percAbstention")] double Abstention, 
    [property: JsonPropertyName("percBlank")] double BlankPercentage,
    [property: JsonPropertyName("percNull")] double NullPercentage,
    [property: JsonPropertyName("percAppealed")] double AppealedPercentage,
    [property: JsonPropertyName("percContested")] double ContestedPercentage,
    [property: JsonPropertyName("percCommand")] double CommandPercentage)
{
    public int SumVotes => Affirmative + Blank + Null + Appealed + Contested + Command;
}
public record PartyInfo(string Name, int Votes, 
    [property: JsonPropertyName("perc")] double Percentage);
public record UserId(string Id, string Name);

class ActionCollection<T> : ObservableCollection<T>
{
    public ActionCollection(Action<T> action)
    {
        CollectionChanged += (s, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (T item in e.NewItems)
                    if (item is not null)
                        action.Invoke(item);
            }
        };
    }
}

class IndexedCollection<TKey, TValue> : ObservableCollection<TValue>, IDictionary<TKey, TValue> where TKey : notnull
{
    readonly Dictionary<TKey, TValue> index = new();

    public IndexedCollection(Func<TValue, TKey> key, Action<TValue>? add = default)
    {
        CollectionChanged += (s, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (TValue item in e.NewItems)
                    if (item is not null)
                        if (index.TryAdd(key(item), item))
                            add?.Invoke(item);
            }
        };
    }

    public TValue this[TKey key] { get => ((IDictionary<TKey, TValue>)index)[key]; set => ((IDictionary<TKey, TValue>)index)[key] = value; }

    public ICollection<TKey> Keys => ((IDictionary<TKey, TValue>)index).Keys;

    public ICollection<TValue> Values => ((IDictionary<TKey, TValue>)index).Values;

    public bool IsReadOnly => ((ICollection<KeyValuePair<TKey, TValue>>)index).IsReadOnly;

    public void Add(TKey key, TValue value)
    {
        ((IDictionary<TKey, TValue>)index).Add(key, value);
    }

    public void Add(KeyValuePair<TKey, TValue> item)
    {
        ((ICollection<KeyValuePair<TKey, TValue>>)index).Add(item);
    }

    public bool Contains(KeyValuePair<TKey, TValue> item)
    {
        return ((ICollection<KeyValuePair<TKey, TValue>>)index).Contains(item);
    }

    public bool ContainsKey(TKey key)
    {
        return ((IDictionary<TKey, TValue>)index).ContainsKey(key);
    }

    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        ((ICollection<KeyValuePair<TKey, TValue>>)index).CopyTo(array, arrayIndex);
    }

    public bool Remove(TKey key)
    {
        return ((IDictionary<TKey, TValue>)index).Remove(key);
    }

    public bool Remove(KeyValuePair<TKey, TValue> item)
    {
        return ((ICollection<KeyValuePair<TKey, TValue>>)index).Remove(item);
    }

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        return ((IDictionary<TKey, TValue>)index).TryGetValue(key, out value);
    }

    IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
    {
        return ((IEnumerable<KeyValuePair<TKey, TValue>>)index).GetEnumerator();
    }
}