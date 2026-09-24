namespace Cabinet.Runtime.Tests;

public sealed class ValhallaSupermassiveScenario(ValhallaSupermassiveScenario.Installed installed)
    : IClassFixture<ValhallaSupermassiveScenario.Installed>
{
    private const string Id = "valhalla-supermassive";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "ValhallaSupermassive.vst3",
        ["VST2"] = "ValhallaSupermassive_x64.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task OpensItsEditorAndReverberates(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Bridged(format, Bridges[format]);

        installed.Harness.VerifyEditor(bridge);
        var audio = await installed.Harness.Render(bridge, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.True(audio.MixChanged, $"{installed.Entry.Name}'s Mix did not hold fully wet");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0.0025, 1);
        Assert.InRange(audio.Peak, 0.014, 1);
    }

    public sealed class Installed() : InstalledEntry(Id);
}
