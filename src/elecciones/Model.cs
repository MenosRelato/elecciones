using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Serialization;

namespace MenosRelato;

public static class ElectionExtensions
{
    public static IEnumerable<Ballot> GetBallots(this Election election) => election.Districts.SelectMany(d => d.GetBallots());

    public static IEnumerable<Ballot> GetBallots(this District district) => district.Provincials.SelectMany(x => x.GetBallots());

    public static IEnumerable<Ballot> GetBallots(this Provincial provincial) => provincial.Sections.SelectMany(s => s.GetBallots());

    public static IEnumerable<Ballot> GetBallots(this Section section) => section.Circuits.SelectMany(c => c.GetBallots());

    public static IEnumerable<Ballot> GetBallots(this Circuit circuit) => circuit.Stations.SelectMany(b => b.Ballots);

    public static string ToUserString(this ElectionKind kind) => kind switch 
    { 
        ElectionKind.Primary => "PASO", 
        ElectionKind.General => "GENERAL", 
        ElectionKind.Ballotage => "BALOTAJE", 
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null) 
    };

    public static string ToUserString(this BallotKind kind) => kind switch
    {
        BallotKind.Positive => "POSITIVO",
        BallotKind.Blank => "EN BLANCO",
        BallotKind.Null => "NULO",
        BallotKind.Appealed => "RECURRIDO",
        BallotKind.Contested => "IMPUGNADO",
        BallotKind.Command => "COMANDO",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}

public record Election(int Year, ElectionKind Kind)
{
    // Serialized just for reference.
    public Dictionary<byte, string> Ballots { get; } = typeof(BallotKind)
        .GetMembers(BindingFlags.Public | BindingFlags.Static)
        .OfType<FieldInfo>()
        .Where(x => x.Attributes.HasFlag(FieldAttributes.Literal))
        .Select(x => new { x.GetCustomAttribute<DescriptionAttribute>()!.Description, Value = (byte)x.GetValue(null)! })
        .ToDictionary(x => x.Value, x => x.Description);

    public List<Party> Parties { get; } = new();
    public List<Position> Positions { get; } = new();
    public List<District> Districts { get; } = new();

    public Party? GetOrAddParty(int? id, string? name)
    {
        if (id is null || name is null)
            return null;

        if (Parties.FirstOrDefault(p => p.Id == id) is { } party)
            return party;

        party = new Party(id.Value, name);
        Parties.Add(party);
        return party;
    }

    public Position GetOrAddPosition(int id, string name)
    {
        if (Positions.FirstOrDefault(p => p.Id == id) is { } position)
            return position;

        position = new Position(id, name);
        Positions.Add(position);
        return position;
    }

    public District GetOrAddDistrict(int id, string name)
    {
        if (Districts.FirstOrDefault(d => d.Id == id) is { } district)
            return district;

        district = new District(id, name);
        Districts.Add(district);
        return district;
    }
}

[TypeConverter(typeof(ElectionKindConverter))]
public enum ElectionKind : byte
{
    [Description("PASO")]
    Primary,
    [Description("GENERAL")]
    General,
    [Description("BALOTAJE")]
    Ballotage
}

public class ElectionKindConverter : EnumConverter
{
    public ElectionKindConverter() : base(typeof(ElectionKind)) { }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string kind)
        {
            return kind switch
            {
                "PASO" => ElectionKind.Primary,
                "GENERAL" => ElectionKind.General,
                "BALOTAJE" => ElectionKind.Ballotage,
                _ => base.ConvertFrom(context, culture, value)
            };
        }

        return base.ConvertFrom(context, culture, value);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (value is ElectionKind kind && destinationType == typeof(string))
            return kind.ToUserString();

        return base.ConvertTo(context, culture, value, destinationType);
    }
}

[TypeConverter(typeof(BallotKindConverter))]
public enum BallotKind : byte
{
    [Description("POSITIVO")]
    Positive,
    [Description("EN BLANCO")]
    Blank,
    [Description("NULO")]
    Null,
    [Description("RECURRIDO")]
    /// <summary>
    /// Recurrido
    /// </summary>
    Appealed,
    [Description("IMPUGNADO")]
    /// <summary>
    /// Impugnado
    /// </summary>
    Contested,
    [Description("COMANDO")]
    /// <summary>
    /// Commando
    /// </summary>
    Command
}

public class BallotKindConverter : EnumConverter
{
    public BallotKindConverter() : base(typeof(BallotKind)) { }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string kind)
        {
            return kind switch
            {
                "POSITIVO" => BallotKind.Positive,
                "EN BLANCO" => BallotKind.Blank,
                "NULO" => BallotKind.Null,
                "RECURRIDO" => BallotKind.Appealed,
                "IMPUGNADO" => BallotKind.Contested,
                "COMANDO" => BallotKind.Command,
                _ => base.ConvertFrom(context, culture, value)
            };
        }

        return base.ConvertFrom(context, culture, value);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (value is BallotKind kind && destinationType == typeof(string))
            return kind.ToUserString();

        return base.ConvertTo(context, culture, value, destinationType);
    }
}

public record District
{
    Dictionary<int, Provincial> index = new();
    ObservableCollection<Provincial> values = new();

    public District(int id, string name)
    {
        (Id, Name) = (id, name);
        values.CollectionChanged += (s, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (Provincial item in e.NewItems)
                {
                    item.District = this;
                    index.Add(item.Id, item);
                }
            }
        };
    }

    public int Id { get; init; }
    public string Name { get; init; }
    public IList<Provincial> Provincials => values;

    public Provincial GetOrAddProvincial(int id, string name)
    {
        if (index.TryGetValue(id, out var provincial))
            return provincial;

        provincial = new Provincial(id, name);
        values.Add(provincial);
        return provincial;
    }
}

public class Provincial
{
    Dictionary<int, Section> index = new();
    ObservableCollection<Section> values = new();

    public Provincial(int id, string name)
    {
        (Id, Name) = (id, name);
        values.CollectionChanged += (s, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (Section item in e.NewItems)
                {
                    item.Provincial = this;
                    index.Add(item.Id, item);
                }
            }
        };
    }

    public int Id { get; }
    public string Name { get; }
    public District? District { get; set; }
    public IList<Section> Sections => values;

    public Section GetOrAddSection(int id, string name)
    {
        if (index.TryGetValue(id, out var section))
            return section;

        section = new Section(id, name);
        values.Add(section);
        return section;
    }
}

public class Section
{
    Dictionary<string, Circuit> index = new();
    ObservableCollection<Circuit> values = new();

    public Section(int id, string name)
    {
        (Id, Name) = (id, name);
        values.CollectionChanged += (s, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (Circuit item in e.NewItems)
                {
                    item.Section = this;
                    index.Add(item.Id, item);
                }
            }
        };
    }

    public int Id { get; }
    public string Name { get; }
    public Provincial? Provincial { get; set; }
    public IList<Circuit> Circuits => values;

    public Circuit GetOrAddCircuit(string id, string? name)
    {
        if (index.TryGetValue(id, out var circuit))
            return circuit;

        circuit = new Circuit(id, name);
        values.Add(circuit);
        return circuit;
    }
}

public class Circuit
{
    Dictionary<int, Station> index = new();
    ObservableCollection<Station> values = new();

    public Circuit(string id, string? name)
    {
        (Id, Name) = (id, name);
        values.CollectionChanged += (s, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (Station item in e.NewItems)
                {
                    item.Circuit = this;
                    index.Add(item.Id, item);
                }
            }
        };
    }

    public string Id { get; }
    public string? Name { get; }
    public Section? Section { get; set; }
    public IList<Station> Stations => values;

    public Station GetOrAddStation(int id, int electors)
    {
        if (index.TryGetValue(id, out var station))
            return station;

        station = new Station(id, electors);
        values.Add(station);
        return station;
    }
}

public record Party
{
    Dictionary<int, PartyList> index = new();
    ObservableCollection<PartyList>? values;

    public Party(int id, string name)
    {
        (Id, Name) = (id, name);
    }

    public int Id { get; }
    public string Name { get; }

    public ICollection<PartyList>? Lists => values;

    public PartyList? GetOrAddList(int? id, string? name)
    {
        if (id is null || name is null)
            return null;

        if (index.TryGetValue(id.Value, out var list))
            return list;

        list = new PartyList(id.Value, name);

        if (values is null)
        {
            values = new ObservableCollection<PartyList>();
            values.CollectionChanged += (s, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
                {
                    foreach (PartyList item in e.NewItems)
                        index.Add(item.Id, item);
                }
            };
        }

        values.Add(list);
        return list;
    }
}

public record PartyList(int Id, string Name);

public record Position(int Id, string Name);

public class Station
{
    public Station(int id, int electors)
    {
        (Id, Electors) = (id, electors);
    }

    public int Id { get; }
    public int Electors { get; }
    public Circuit? Circuit { get; set; }
    public List<Ballot> Ballots { get; init; } = new();

    public Ballot GetOrAddBallot(BallotKind kind, int count, int position, int? party, int? list)
    {
        if (Ballots.FirstOrDefault(b => b.Position == position && b.Party == party && b.List == list && b.Kind == kind) is { } ballot)
        {
            ballot = ballot with { Count = count };
            return ballot;
        }

        if (kind == BallotKind.Positive && party == null)
            throw new ArgumentException("Voto positivo sin partido.");

        ballot = new Ballot(this, kind, count, position, party, list);
        Ballots.Add(ballot);
        return ballot;
    }
}

public record Ballot(
    [property: JsonIgnore, Newtonsoft.Json.JsonIgnore]
    Station Station,
    BallotKind Kind,
    int Count,
    int Position,
    int? Party, int? List);