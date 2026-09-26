namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class FabFilterTotalBundleScenario(FabFilterTotalBundleScenario.Installed installed)
    : IClassFixture<FabFilterTotalBundleScenario.Installed>
{
    private const string Id = "fabfilter-total-bundle";

    private const string Evaluate = "474 282";

    private const string ProQGain = "Output Level";

    private const string ProLGain = "Gain";

    private static readonly Dictionary<string, string> ProQ = new()
    {
        ["VST3"] = "FabFilter Pro-Q 4.vst3",
    };

    private static readonly Dictionary<string, string> ProL = new()
    {
        ["VST3"] = "FabFilter Pro-L 2.vst3",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task ProQOpensItsEditorPastTheTrialAndRaisesItsOutput(string format)
    {
        Assert.True(
            ProQ.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, ProQ[format]) with { Label = $"{format}-pro-q" };

        var editor = installed.Harness.VerifyEditor(bridge, click: Evaluate);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Render(bridge, ProQGain, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.True(audio.MixChanged, $"Pro-Q's {ProQGain} did not hold at its maximum");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0, 0.00001);
        Assert.InRange(audio.Peak, 1.5, 100);
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task ProLOpensItsEditorPastTheTrialAndLimits(string format)
    {
        Assert.True(
            ProL.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, ProL[format]) with { Label = $"{format}-pro-l" };

        var editor = installed.Harness.VerifyEditor(bridge, click: Evaluate);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Render(bridge, ProLGain, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.True(audio.MixChanged, $"Pro-L's {ProLGain} did not hold at its maximum");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0, 0.00001);
        Assert.InRange(audio.Peak, 0.5, 1);
    }

    public sealed class Installed() : InstalledEntry(Id);
}
