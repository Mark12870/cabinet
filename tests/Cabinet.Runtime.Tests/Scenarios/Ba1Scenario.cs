namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class Ba1Scenario(Ba1Scenario.Installed installed)
    : IClassFixture<Ba1Scenario.Installed>
{
    private const string Id = "ba-1";

    private const int Note = 60;

    private const string ContinueTrial = "779 385";

    private const string Osc1Wave = "0 539 137";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "BA-1.vst3",
        ["VST2"] = "BA-1.so",
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

        var editor = installed.Harness.VerifyEditor(bridge, click: ContinueTrial, control: Osc1Wave);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Play(bridge, Note, installed.Display, click: ContinueTrial);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0.0006, 1);
        Assert.InRange(audio.Peak, 0.037, 1);
    }

    public sealed class Installed() : BabyAudioEntry(Id);
}
