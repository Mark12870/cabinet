namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class SolarisScenario(SolarisScenario.Installed installed)
    : IClassFixture<SolarisScenario.Installed>
{
    private const string Id = "solaris";

    private const string Archive = "Adam Szabo/adam_szabo_solaris_v1-0.1746624908.zip";

    private const string ArchiveSha256 = "2843659416dae0afc1df6a18cec4d7c0566217ed2974d9ca888b8634be3d12f1";

    private const string Mix = "Dry / Wet";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Solaris.vst3",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task ProcessesAudioWithAWorkingEditor(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);
        var audio = await installed.Harness.Render(bridge, Mix, installed.Display);

        Assert.True(audio.Parameters > 0, "Solaris exposed no parameters");
        Assert.True(audio.MixChanged, "Solaris's Dry / Wet did not hold fully wet");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0.0045, 1);
        Assert.InRange(audio.Peak, 0.02, 1);

        var editor = installed.Harness.VerifyEditor(bridge);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        private protected override async Task Install(Display display) =>
            await Harness.InstallFrom(await Dropbox.Download(Archive, ArchiveSha256, Harness.Home), display);
    }
}
