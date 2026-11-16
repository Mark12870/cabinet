namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class Ihny2Scenario(Ihny2Scenario.Installed installed)
    : IClassFixture<Ihny2Scenario.Installed>
{
    private const string Id = "ihny-2";

    private const string Mix = "wet-dry";

    private const string ContinueTrial = "395 397";

    private const string OutputSlider = "13 339 572";

    private const string Tweak = "245 577";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "IHNY-2.vst3",
        ["VST2"] = "IHNY-2.so",
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

        var editor = installed.Harness.VerifyEditor(bridge, click: ContinueTrial, press: Tweak, control: OutputSlider);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Render(bridge, Mix, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.True(audio.MixChanged, $"{installed.Entry.Name}'s {Mix} did not hold fully wet");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Peak, 0.077, 1);
    }

    public sealed class Installed() : BabyAudioEntry(Id);
}
