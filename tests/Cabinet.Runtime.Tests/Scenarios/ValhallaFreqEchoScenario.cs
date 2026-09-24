namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class ValhallaFreqEchoScenario(ValhallaFreqEchoScenario.Installed installed)
    : IClassFixture<ValhallaFreqEchoScenario.Installed>
{
    private const string Id = "valhalla-freq-echo";

    private const string Mix = "wetDry";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "ValhallaFreqEcho.vst3",
        ["VST2"] = "ValhallaFreqEcho_x64.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task OpensItsEditorAndEchoes(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Bridged(format, Bridges[format]);

        installed.Harness.VerifyEditor(bridge);
        var audio = await installed.Harness.Render(bridge, Mix, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.True(audio.MixChanged, $"{installed.Entry.Name}'s {Mix} did not hold fully wet");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0.0000005, 1);
        Assert.InRange(audio.Peak, 0.015, 1);
    }

    public sealed class Installed() : InstalledEntry(Id);
}
