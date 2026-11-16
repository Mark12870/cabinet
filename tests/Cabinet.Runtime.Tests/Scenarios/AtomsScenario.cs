namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class AtomsScenario(AtomsScenario.Installed installed)
    : IClassFixture<AtomsScenario.Installed>
{
    private const string Id = "atoms";

    private const int Note = 60;

    private const string ContinueTrial = "551 512";

    private const string ReleaseKnob = "5 294 635";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Atoms.vst3",
        ["VST2"] = "Atoms.so",
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

        var editor = installed.Harness.VerifyEditor(bridge, click: ContinueTrial, control: ReleaseKnob);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Play(bridge, Note, installed.Display, click: ContinueTrial);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0.0003, 1);
        Assert.InRange(audio.Peak, 0.0015, 1);
    }

    public sealed class Installed() : BabyAudioEntry(Id);
}
