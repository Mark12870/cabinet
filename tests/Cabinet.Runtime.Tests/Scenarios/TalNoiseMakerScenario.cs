namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class TalNoiseMakerScenario(TalNoiseMakerScenario.Installed installed)
    : IClassFixture<TalNoiseMakerScenario.Installed>
{
    private const string Id = "tal-noisemaker";

    private const int Note = 60;

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "TAL-NoiseMaker.vst3",
        ["VST2"] = "libTAL-NoiseMaker.so",
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
        Assert.InRange(audio.Peak, 0.028, 1);
    }

    public sealed class Installed() : InstalledEntry(Id);
}
