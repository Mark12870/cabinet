namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class Kick3Scenario(Kick3Scenario.Installed installed)
    : IClassFixture<Kick3Scenario.Installed>
{
    private const string Id = "kick-3";

    private const string Installer = "Sonic Academy/Sonic+Academy+KICK+3+Demo+-+V1.1.6.zip";

    private const string InstallerSha256 = "53d0c0bd06923265fe7e26bd59bd0f08f3dbc6c38d7027ce896e60839e3b2496";

    private const int Note = 36;

    private const string EvaluateTheDemo = "655 400";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Kick 3.vst3",
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

        var editor = installed.Harness.VerifyEditor(bridge, click: EvaluateTheDemo);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Play(bridge, Note, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0.0025, 1);
        Assert.InRange(audio.Peak, 0.1, 1);
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        private protected override async Task Install(Display display) =>
            await Harness.InstallFrom(await Dropbox.Download(Installer, InstallerSha256, Harness.Home), display);

        protected override void Settle(string home)
        {
            var user = Directory.GetDirectories(Path.Combine(Harness.Prefix, "drive_c", "users"))
                .Single(path => Directory.Exists(Path.Combine(path, "AppData")) && Path.GetFileName(path) != "Public");
            var settings = Path.Combine(user, "AppData", "Roaming", "Sonic Academy", "Kick 3");
            Directory.CreateDirectory(settings);
            File.WriteAllText(
                Path.Combine(settings, "Kick 3.settings"),
                """
                <?xml version="1.0" encoding="UTF-8"?>

                <PROPERTIES>
                  <VALUE name="settings">
                    <settings lastUpdateCheck="4102444800000"/>
                  </VALUE>
                </PROPERTIES>
                """);
        }
    }
}
