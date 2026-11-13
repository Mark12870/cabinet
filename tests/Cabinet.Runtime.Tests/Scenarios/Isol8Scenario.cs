namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class Isol8Scenario(Isol8Scenario.Installed installed)
    : IClassFixture<Isol8Scenario.Installed>
{
    private const string Id = "isol8";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "TBProAudio/ISOL8nogpu.vst3",
        ["VST2"] = "TBProAudio/ISOL8nogpu.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task OpensItsEditorAndPassesAudio(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var editor = installed.Harness.VerifyEditor(bridge);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Render(bridge, "", installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0, 0.00001);
        Assert.InRange(audio.Peak, 0.2, 0.3);
    }

    public sealed class Installed() : InstalledEntry(Id);
}
