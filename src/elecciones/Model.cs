namespace MenosRelato;

public record Election(int Year, ElectionKind Kind)
{
    public List<Party> Parties { get; } = new();
    public List<Position> Positions { get; } = new();
    public List<District> Districts { get; } = new();
    public List<Ballot> Ballots { get; } = new();
}

public record Ballot(int Year, Election Election, 
    int District, int Provincial, int Section, string Circuit, int Booth, int Electors,
    int Position, int Party, int? List, BallotKind Kind, int Count);

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
}
public record Provincial(int Id, string Name)
{
    public List<Section> Sections { get; } = new();
}
public record Section(int Id, string Name)
{
    public List<Circuit> Circuits { get; } = new();
}
public record Circuit(int Id, string Name);
public record Party(int Id, string Name)
{
    public List<PartyList> Lists { get; } = new();
}
public record PartyList(int Id, string Name);
public record Position(int Id, string Name);