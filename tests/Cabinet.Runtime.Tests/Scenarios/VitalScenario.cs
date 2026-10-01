namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class VitalScenario(VitalScenario.Installed installed)
    : IClassFixture<VitalScenario.Installed>
{
    private const string Id = "vital";

    private const string Version = "1.0.7";

    private const string WorkOffline = "865 476";

    private const string Macro1 = "211 42 82";

    private const int Note = 60;

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Vital.vst3",
        ["VST2"] = "Vital.so",
        ["LV2"] = "http://vital.audio",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    public static TheoryData<string> Editors => ["VST2"];

    [Theory]
    [MemberData(nameof(Editors))]
    public void OpensItsEditorPastItsSignIn(string format)
    {
        installed.Harness.VerifyEditor(
            installed.Harness.Plugin(format, Bridges[format]), click: WorkOffline, control: Macro1);
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task PlaysANote(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var audio = await installed.Harness.Play(bridge, Note, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0, 1);
        Assert.InRange(audio.Peak, 0.036, 1);
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        private protected override async Task Install(Display display) =>
            await Harness.InstallFrom(await Vital.Download("Vital", "Linux (zip)", Version, Harness.Home), display);
    }
}
