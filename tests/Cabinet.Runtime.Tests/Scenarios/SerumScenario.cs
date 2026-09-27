namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class SerumScenario(SerumScenario.Installed installed)
    : IClassFixture<SerumScenario.Installed>
{
    private const string Id = "serum";

    private const string Installer = "Xfer Records/Install_Xfer_Serum2_Demo_2.1.5.exe";

    private const string InstallerSha256 = "3964f2957748076d7d85a9bfe51e5a3ab9a2fc50efd3586debe5e7e792d6ad84";

    private const int Note = 60;

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Serum2.vst3",
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

        var editor = installed.Harness.VerifyEditor(bridge);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Play(bridge, Note, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0, 1);
        Assert.InRange(audio.Peak, 0.028, 1);
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        private protected override async Task Install(Display display) =>
            await Harness.InstallFrom(await Dropbox.Download(Installer, InstallerSha256, Harness.Home), display);
    }
}
