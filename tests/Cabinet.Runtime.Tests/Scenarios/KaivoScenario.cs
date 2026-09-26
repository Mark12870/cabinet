namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class KaivoScenario(KaivoScenario.Installed installed)
    : IClassFixture<KaivoScenario.Installed>
{
    private const string Id = "kaivo";

    private const int Note = 60;

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST2"] = "Kaivo_x64.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task OpensItsEditorAndPlaysANote(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var editor = installed.Harness.VerifyEditor(bridge);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Play(bridge, Note, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0, 1);
        Assert.InRange(audio.Peak, 0.0177, 1);
    }

    public sealed class Installed() : InstalledEntry(Id);
}
