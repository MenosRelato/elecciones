using MenosRelato.Commands;

namespace MenosRelato;

public class ElectionFixture : IDisposable
{
    public ElectionFixture()
    {
        Settings.Validate();
        var baseDir = Settings.BaseDir;
        var path = Path.Combine(baseDir, "election.json.gz");

        Election = ModelSerializer.DeserializeAsync(path).Result ?? 
            throw new ArgumentException();
    }

    public ElectionSettings Settings { get; } = new();

    public Election Election { get; }

    public void Dispose() { }
}
