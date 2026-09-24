namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class TalReverb4Scenario(TalReverb4Scenario.Installed installed)
    : IClassFixture<TalReverb4Scenario.Installed>
{
    private const string Id = "tal-reverb-4";

    private const string Mix = "Wet";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "TAL-Reverb-4.vst3",
        ["VST2"] = "libTAL-Reverb-4.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task OpensItsEditorAndReverberates(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        installed.Harness.VerifyEditor(bridge);
        var audio = await installed.Harness.Render(bridge, Mix, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.True(audio.MixChanged, $"{installed.Entry.Name}'s {Mix} did not hold fully wet");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0.012, 1);
        Assert.InRange(audio.Peak, 0.077, 1);
    }

    public sealed class Installed() : InstalledEntry(Id);
}
