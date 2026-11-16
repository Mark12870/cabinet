namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class TaipScenario(TaipScenario.Installed installed)
    : IClassFixture<TaipScenario.Installed>
{
    private const string Id = "taip";

    private const string Mix = "Mix";

    private const string ContinueTrial = "559 350";

    private const string PowerSwitch = "0 386 38";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "TAIP.vst3",
        ["VST2"] = "TAIP.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task OpensItsEditorAndSaturates(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var editor = installed.Harness.VerifyEditor(bridge, click: ContinueTrial, control: PowerSwitch);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Render(bridge, Mix, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.True(audio.MixChanged, $"{installed.Entry.Name}'s {Mix} did not hold fully wet");
        Assert.InRange(audio.Before, 0, 0.001);
        Assert.InRange(audio.Peak, 0.036, 1);
    }

    public sealed class Installed() : BabyAudioEntry(Id);
}
