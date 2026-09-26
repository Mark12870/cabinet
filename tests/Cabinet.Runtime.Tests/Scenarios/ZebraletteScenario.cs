namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class ZebraletteScenario(ZebraletteScenario.Installed installed)
    : IClassFixture<ZebraletteScenario.Installed>
{
    private const string Id = "zebralette";

    private const int Note = 60;

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Zebralette3.vst3",
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

        installed.Harness.VerifyEditor(bridge);
        var audio = await installed.Harness.Play(bridge, Note, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0, 1);
        Assert.InRange(audio.Peak, 0.0292, 1);
    }

    public sealed class Installed() : InstalledEntry(Id);
}
