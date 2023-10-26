namespace MenosRelato;

public record Ballot(int Year, Election Election, 
    int District, int Provincial, int Section, string Circuit, int Booth, int Electors,
    int Position, int Party, int? List, Kind Kind, int Count);

public enum Election : byte { Primary, General, Ballotage }
public enum Kind : byte
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
public record District(int Id, string Name);
public record Provincial(int Id, string Name);
public record Section(int Id, string Name);
public record Circuit(int Id, string Name);
public record Party(int Id, string Name);
public record PartyList(int Id, string Name);
public record Position(int Id, string Name);