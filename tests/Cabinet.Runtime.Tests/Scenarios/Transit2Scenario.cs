namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class Transit2Scenario(Transit2Scenario.Installed installed)
    : IClassFixture<Transit2Scenario.Installed>
{
    private const string Id = "transit-2";

    private const string Mix = "2. GLOBAL MIX";

    private const string ContinueTrial = "672 493";

    private const string MacroKnob = "0 144 195";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Transit 2.vst3",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task OpensItsEditorAndTransforms(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var editor = installed.Harness.VerifyEditor(bridge, click: ContinueTrial, control: MacroKnob);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Render(bridge, Mix, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.True(audio.MixChanged, $"{installed.Entry.Name}'s {Mix} did not hold fully wet");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Peak, 0.025, 1);
    }

    public sealed class Installed() : BabyAudioEntry(Id);
}
