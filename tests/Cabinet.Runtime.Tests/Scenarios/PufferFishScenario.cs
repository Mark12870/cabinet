namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class PufferFishScenario(PufferFishScenario.Installed installed)
    : IClassFixture<PufferFishScenario.Installed>
{
    private const string Id = "puffer-fish";

    private const string Mix = "Puffiness";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "pufferfish.vst3",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task OpensItsEditorAndSaturates(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var editor = installed.Harness.VerifyEditor(bridge);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Render(bridge, Mix, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.True(audio.MixChanged, $"{installed.Entry.Name}'s {Mix} did not hold at its maximum");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0, 1);
        Assert.InRange(audio.Peak, 0.07, 10);
    }

    public sealed class Installed() : SonibleEntry(Id, "pufferfish");
}
