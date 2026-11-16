namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class TeknoScenario(TeknoScenario.Installed installed)
    : IClassFixture<TeknoScenario.Installed>
{
    private const string Id = "tekno";

    private const int Note = 36;

    private const string ContinueTrial = "710 491";

    private const string KickFrequency = "0 37 578";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Tekno.vst3",
        ["VST2"] = "Tekno.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task OpensItsEditorAndPlaysAKick(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var editor = installed.Harness.VerifyEditor(bridge, click: ContinueTrial, control: KickFrequency);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Play(bridge, Note, installed.Display, click: ContinueTrial);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0.0014, 1);
        Assert.InRange(audio.Peak, 0.081, 1);
    }

    public sealed class Installed() : BabyAudioEntry(Id);
}
