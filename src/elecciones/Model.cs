namespace MenosRelato;

public record Election(int Year, ElectionKind Kind)
{
    public List<Party> Parties { get; } = new();
    public List<Position> Positions { get; } = new();
    public List<District> Districts { get; } = new();
    public IEnumerable<Ballot> Ballots => 
        Districts.SelectMany(d => d.Provincials)
            .SelectMany(p => p.Sections)
            .SelectMany(s => s.Circuits)
            .SelectMany(c => c.Booths)
            .SelectMany(b => b.Ballots);

    public Party GetOrAddParty(int id, string name)
    {
        if (Parties.FirstOrDefault(p => p.Id == id) is { } party)
            return party;

        party = new Party(id, name);
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

public record Ballot(Booth Booth, Party Party, PartyList? List, BallotKind Kind, int Count);

public enum ElectionKind : byte { Primary, General, Ballotage }
public enum BallotKind : byte
{
    Positive,
    Blank,
    Null,
    /// <summary>
    /// Recurrido
    /// </summary>
    Appealed,
    /// <summary>
    /// Impugnado
    /// </summary>
    Contested, 
    /// <summary>
    /// Commando
    /// </summary>
    Command
}
public record District(int Id, string Name)
{
    public List<Provincial> Provincials { get; } = new();

    public Provincial GetOrAdd(int id, string name)
    {
        if (Provincials.FirstOrDefault(p => p.Id == id) is { } provincial)
            return provincial;

        provincial = new Provincial(id, name, this);
        Provincials.Add(provincial);
        return provincial;
    }
}

public record Provincial(int Id, string Name, District District)
{
    public List<Section> Sections { get; } = new();

    public Section GetOrAdd(int id, string name)
    {
        if (Sections.FirstOrDefault(s => s.Id == id) is { } section)
            return section;

        section = new Section(id, name, this);
        Sections.Add(section);
        return section;
    }
}

public record Section(int Id, string Name, Provincial Provincial)
{
    public List<Circuit> Circuits { get; } = new();

    public Circuit GetOrAdd(int id, string name)
    {
        if (Circuits.FirstOrDefault(c => c.Id == id) is { } circuit)
            return circuit;

        circuit = new Circuit(id, name, this);
        Circuits.Add(circuit);
        return circuit;
    }
}

public record Circuit(int Id, string Name, Section Section)
{
    public List<Booth> Booths { get; } = new();

    public Booth GetOrAdd(int id, int electors)
    {
        if (Booths.FirstOrDefault(b => b.Id == id) is { } booth)
        {
            return booth;
        }

        booth = new Booth(id, electors, this);
        Booths.Add(booth);
        return booth;
    }
}

public record Party(int Id, string Name)
{
    public List<PartyList> Lists { get; } = new();

    public PartyList GetOrAdd(int id, string name)
    {
        if (Lists.FirstOrDefault(l => l.Id == id) is { } list)
            return list;

        list = new PartyList(id, name);
        Lists.Add(list);
        return list;
    }
}

public record PartyList(int Id, string Name);

public record Position(int Id, string Name);

public record Booth(int Id, int Electors, Circuit Circuit)
{
    public List<Ballot> Ballots { get; } = new();

    public Ballot GetOrAdd(Party party, PartyList? list, BallotKind kind, int count)
    {
        if (Ballots.FirstOrDefault(b => b.Party == party && b.List == list && b.Kind == kind) is { } ballot)
        {
            ballot = ballot with { Count = count };
            return ballot;
        }

        ballot = new Ballot(this, party, list, kind, count);
        Ballots.Add(ballot);
        return ballot;
    }
}