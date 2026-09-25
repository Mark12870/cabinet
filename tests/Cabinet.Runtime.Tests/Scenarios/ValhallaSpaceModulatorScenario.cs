namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class ValhallaSpaceModulatorScenario(ValhallaSpaceModulatorScenario.Installed installed)
    : IClassFixture<ValhallaSpaceModulatorScenario.Installed>
{
    private const string Id = "valhalla-space-modulator";

    private const string Mix = "wetDry";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "ValhallaSpaceModulator.vst3",
        ["VST2"] = "ValhallaSpaceModulator_x64.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task OpensItsEditorAndModulates(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var editor = installed.Harness.VerifyEditor(bridge);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Render(bridge, Mix, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.True(audio.MixChanged, $"{installed.Entry.Name}'s {Mix} did not hold fully wet");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0.0000002, 1);
        Assert.InRange(audio.Peak, 0.028, 1);
    }

    public sealed class Installed() : InstalledEntry(Id);
}
