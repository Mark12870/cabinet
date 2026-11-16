namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class ParallelAggressorScenario(ParallelAggressorScenario.Installed installed)
    : IClassFixture<ParallelAggressorScenario.Installed>
{
    private const string Id = "parallel-aggressor";

    private const string Mix = "Spank Level";

    private const string ContinueTrial = "600 323";

    private const string SpankKnob = "1 220 208";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Parallel Aggressor.vst3",
        ["VST2"] = "Parallel Aggressor.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task OpensItsEditorAndCompresses(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var editor = installed.Harness.VerifyEditor(bridge, click: ContinueTrial, control: SpankKnob);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Render(bridge, Mix, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.True(audio.MixChanged, $"{installed.Entry.Name}'s {Mix} did not hold fully wet");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Peak, 0.051, 1);
    }

    public sealed class Installed() : BabyAudioEntry(Id);
}
