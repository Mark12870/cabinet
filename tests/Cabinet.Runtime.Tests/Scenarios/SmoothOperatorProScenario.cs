namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class SmoothOperatorProScenario(SmoothOperatorProScenario.Installed installed)
    : IClassFixture<SmoothOperatorProScenario.Installed>
{
    private const string Id = "smooth-operator-pro";

    private const string Mix = "Global Mix";

    private const string ContinueTrial = "768 430";

    private const string GlobalThreshold = "4 146 129";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Smooth Operator Pro.vst3",
        ["VST2"] = "Smooth Operator Pro.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task OpensItsEditorAndSmooths(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var editor = installed.Harness.VerifyEditor(bridge, click: ContinueTrial, control: GlobalThreshold);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Render(bridge, Mix, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.True(audio.MixChanged, $"{installed.Entry.Name}'s {Mix} did not hold fully wet");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Peak, 0.024, 1);
    }

    public sealed class Installed() : BabyAudioEntry(Id);
}
