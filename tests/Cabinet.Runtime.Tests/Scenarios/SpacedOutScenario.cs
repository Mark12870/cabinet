namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class SpacedOutScenario(SpacedOutScenario.Installed installed)
    : IClassFixture<SpacedOutScenario.Installed>
{
    private const string Id = "spaced-out";

    private const string Mix = "Dry/Wet Mix";

    private const string ContinueTrial = "648 335";

    private const string SpaceProgram = "2 768 61";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Spaced Out.vst3",
        ["VST2"] = "Spaced Out.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task OpensItsEditorAndEchoes(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var editor = installed.Harness.VerifyEditor(bridge, click: ContinueTrial, control: SpaceProgram);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Render(bridge, Mix, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.True(audio.MixChanged, $"{installed.Entry.Name}'s {Mix} did not hold fully wet");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Peak, 0.05, 1);
    }

    public sealed class Installed() : BabyAudioEntry(Id);
}
