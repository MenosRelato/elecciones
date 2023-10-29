using System.Text.Json;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace MenosRelato;

public class Misc(ITestOutputHelper output)
{
    [Fact]
    public void SerializeModel()
    {
        var election = new Election(2023, ElectionKind.General);
        var party = election.GetOrAddParty(1, "La Libertad Avanza");
        var position = election.GetOrAddPosition(1, "Presidente");
        var booth = election.GetOrAddDistrict(1, "CABA")
            .GetOrAddProvincial(1, "San Telmo")
            .GetOrAddSection(1, "Plaza")
            .GetOrAddCircuit("1", "Escuela N1")
            .GetOrAddStation(1015, 350);

        var ballot = booth.GetOrAddBallot(BallotKind.Positive, 100, position.Id, party?.Id, null);
        
        Assert.Single(election.GetBallots());

        var options = new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.Preserve,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        var json = System.Text.Json.JsonSerializer.Serialize(election, options);

        output.WriteLine(json);
        //var data = MessagePackSerializer.Serialize(election, StandardResolver.Options);

        // JsonSerializer.Deserialize<Election>(json, options);
        var election2 = JsonConvert.DeserializeObject<Election>(json);

        Assert.NotNull(election2);

        var saved = election2.GetBallots().FirstOrDefault();

        Assert.NotNull(saved);

        Assert.Equal(ballot.Position, saved.Position);
        Assert.Equal(ballot.Kind, saved.Kind);
        Assert.Equal(ballot.Party, saved.Party);
    }
}