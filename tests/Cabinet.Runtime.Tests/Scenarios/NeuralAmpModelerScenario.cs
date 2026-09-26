namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class NeuralAmpModelerScenario(NeuralAmpModelerScenario.Installed installed)
    : IClassFixture<NeuralAmpModelerScenario.Installed>
{
    private const string Id = "neural-amp-modeler";

    private const string Mix = "Output Lvl";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["LV2"] = "http://github.com/mikeoliphant/neural-amp-modeler-lv2",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task ProcessesAudioWithoutAnEditor(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var audio = await installed.Harness.Render(bridge, Mix, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.True(audio.MixChanged, $"{installed.Entry.Name}'s {Mix} did not hold at its maximum");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0, 0.00001);
        Assert.InRange(audio.Peak, 1, 10);
    }

    public sealed class Installed() : InstalledEntry(Id);
}
