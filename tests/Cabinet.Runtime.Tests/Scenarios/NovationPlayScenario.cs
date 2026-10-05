namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class NovationPlayScenario(NovationPlayScenario.Installed installed)
    : IClassFixture<NovationPlayScenario.Installed>
{
    private const string Id = "novation-play";

    private const string Installer = "Novation/Play_1_1_2.zip";

    private const string DontShare = "496 344";

    private const string ActivationCode = "495 280\tABCD";

    private const string InstallerSha256 = "3ea47c9c08d40271beb84eb72acd4e73747b5364a76dd84321134f30fc2ab013";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Play.vst3",
        ["VST2"] = "Play.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public void InstallsAndBridgesItsPlugin(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        Assert.True(installed.Harness.Holds(format, Bridges[format]));
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void OpensItsActivationPrompt(string format)
    {
        var editor = installed.Harness.VerifyEditor(
            installed.Harness.Plugin(format, Bridges[format]), controlsAreParameters: false, press: DontShare, type: ActivationCode);

        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
    }

    [Fact]
    public void InstallsItsFactoryInstrumentsPresetsAndAssets()
    {
        var content = Path.Combine(installed.Harness.Prefix, "drive_c", "ProgramData", "Novation", "Play");

        Assert.NotEmpty(Directory.GetFiles(Path.Combine(content, "instruments"), "*.instr"));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(content, "Factory Presets"), "*.sampreset", SearchOption.AllDirectories));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(content, "drums"), "*.drum"));
        Assert.True(File.Exists(Path.Combine(content, "appdata", "Assets.kga")));
        Assert.True(File.Exists(Path.Combine(content, "appdata", "factory.kga")));
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        private protected override async Task Install(Display display) =>
            await Harness.InstallFrom(await Dropbox.Download(Installer, InstallerSha256, Harness.Home), display);
    }
}
