namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class MtPowerDrumKit2Scenario(MtPowerDrumKit2Scenario.Installed installed)
    : IClassFixture<MtPowerDrumKit2Scenario.Installed>
{
    private const string Id = "mt-power-drum-kit-2";

    private const int Note = 36;

    private const string StartThenMixer = "879 540 391 553";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "MT-PowerDrumKit.vst3",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task OpensItsEditorAndPlaysAKickOnceStarted(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        installed.Harness.VerifyEditor(bridge, click: StartThenMixer);
        var audio = await installed.Harness.Play(bridge, Note, installed.Display, click: StartThenMixer);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0, 1);
        Assert.InRange(audio.Peak, 0.04, 1);
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        protected override void Settle(string home)
        {
            var settings = Path.Combine(home, ".config", "MANDA_AUDIO", "MT-PowerDrumKit", "Settings");
            Directory.CreateDirectory(settings);
            File.WriteAllText(Path.Combine(settings, "language.txt"), "en");
            File.WriteAllText(Path.Combine(settings, "start_screen.txt"), "1");
        }
    }
}
