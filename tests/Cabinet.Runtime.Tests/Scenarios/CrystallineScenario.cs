namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class CrystallineScenario(CrystallineScenario.Installed installed)
    : IClassFixture<CrystallineScenario.Installed>
{
    private const string Id = "crystalline";

    private const string Mix = "Dry Wet Mix";

    private const string ContinueTrial = "664 299";

    private const string EcoMode = "2 950 16";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Crystalline.vst3",
        ["VST2"] = "Crystalline.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task OpensItsEditorAndReverberates(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var editor = installed.Harness.VerifyEditor(bridge, click: ContinueTrial, control: EcoMode);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Render(bridge, Mix, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.True(audio.MixChanged, $"{installed.Entry.Name}'s {Mix} did not hold fully wet");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Peak, 0.034, 1);
    }

    public sealed class Installed() : BabyAudioEntry(Id);
}
