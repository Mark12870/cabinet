namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class LandrMasteringProScenario(LandrMasteringProScenario.Installed installed)
    : IClassFixture<LandrMasteringProScenario.Installed>
{
    private const string Id = "landr-mastering-pro";

    private const string LicenceKeyField = "497 392\tABCD";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "LANDR Mastering Pro.vst3",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task OpensItsWebEditorAndPassesAudio(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var editor = installed.Harness.VerifyEditor(bridge, controlsAreParameters: false, type: LicenceKeyField);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Render(bridge, "", installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0, 0.00001);
        Assert.InRange(audio.Peak, 0.2, 0.3);
    }

    public sealed class Installed() : InstalledEntry(Id);
}
