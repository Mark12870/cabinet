namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class HumanoidScenario(HumanoidScenario.Installed installed)
    : IClassFixture<HumanoidScenario.Installed>
{
    private const string Id = "humanoid";

    private const string Mix = "Dry/Wet Mix";

    private const string ContinueTrial = "718 470";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Humanoid.vst3",
        ["VST2"] = "Humanoid.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task OpensItsEditorAndResynthesises(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var editor = installed.Harness.VerifyEditor(bridge, click: ContinueTrial);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Render(bridge, Mix, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.True(audio.MixChanged, $"{installed.Entry.Name}'s {Mix} did not hold fully wet");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Peak, 0.2, 0.3);
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        private const string QuickTourSeen = "1";

        private protected override async Task Install(Display display)
        {
            await base.Install(display);
            File.WriteAllText(
                Path.Combine(Harness.Prefix, "drive_c", "ProgramData", "BABY Audio", "settings", "Humanoid", "quick.baby"),
                QuickTourSeen);
        }
    }
}
