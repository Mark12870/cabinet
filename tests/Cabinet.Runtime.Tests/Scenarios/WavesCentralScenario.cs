using System.Text.Json.Nodes;

namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class WavesCentralScenario(WavesCentralScenario.Installed installed)
    : IClassFixture<WavesCentralScenario.Installed>
{
    private const string Id = "waves-central";

    private const string Drive = "Drive";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = Installed.Shell,
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task RestoresItsSessionInstallsTheFreePluginPackAndDrivesLilTube(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var editor = installed.Harness.VerifyEditor(bridge);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Render(bridge, Drive, installed.Display);

        Assert.True(audio.Parameters > 0, "Lil Tube exposed no parameters");
        Assert.True(audio.MixChanged, $"Lil Tube's {Drive} did not hold at its maximum");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0.0004, 1);
        Assert.InRange(audio.Peak, 0.02, 1);
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        public const string Shell = "WaveShell1-VST3 17.1_x64.vst3";

        private const string References = "tests/Cabinet.Runtime.Tests/Scenarios/References/";

        private const string AgreeButton = References + "waves-agree.png";

        private const string Agree = "615,444 727,478";

        private const string SignedInPage = References + "waves-signed-in.png";

        private const string Welcome = "130,100 555,200";

        private const string InstallPage = References + "waves-install-products.png";

        private const string PageTitle = "100,10 340,40";

        private const string FreePluginPack = References + "waves-free-plugin-pack.png";

        private const string ProductName = "210,264 380,289";

        private const string SelectionPanel = References + "waves-selected-products.png";

        private const string SelectedProducts = "1018,14 1148,36";

        private const string OkButton = References + "waves-ok.png";

        private const string Ok = "620,504 680,534";

        private const string Account = "600,0 1000,50";

        private static readonly TimeSpan Opening = TimeSpan.FromMinutes(3);

        private static readonly TimeSpan Answering = TimeSpan.FromMinutes(1);

        private static readonly TimeSpan Downloading = TimeSpan.FromMinutes(20);

        private static readonly TimeSpan Beat = TimeSpan.FromSeconds(1);

        private protected override async Task Install(Display display)
        {
            await base.Install(display);
            RestoreTheSession();

            using var manager = Harness.Open(display);
            var licence = manager.Window(Entry.Name, Opening);
            manager.Until(
                () =>
                {
                    manager.HoldClick(licence, 100, 450);
                    Thread.Sleep(Beat);
                    return manager.Shows(licence, AgreeButton, Agree);
                },
                licence,
                "the licence accepted",
                Answering);
            manager.HoldClick(licence, 670, 460);

            var window = manager.VersionedWindow(Entry.Name, Opening);
            manager.Resize(window, 1300, 900);
            manager.Until(
                () => manager.Shows(window, SignedInPage, Welcome), window, "the signed-in welcome page", Opening, Account);
            manager.Until(
                () =>
                {
                    manager.HoldClick(window, 40, 150);
                    Thread.Sleep(Beat);
                    return manager.Shows(window, InstallPage, PageTitle);
                },
                window,
                "Install Products",
                Answering,
                Account);
            manager.Until(
                () => manager.Shows(window, FreePluginPack, ProductName), window, "the Free Plugin Pack", Answering, Account);
            manager.HoldClick(window, 132, 288);
            manager.Until(
                () => manager.Shows(window, SelectionPanel, SelectedProducts),
                window,
                "the Free Plugin Pack selected",
                Answering,
                Account);
            manager.HoldClick(window, 1150, 740);
            manager.Until(
                () => manager.Shows(window, OkButton, Ok), window, "the Free Plugin Pack installed", Downloading, Account);
            manager.Capture(window, "installed", Account);
            manager.HoldClick(window, 650, 519);
            Assert.True(Harness.Holds("VST3", Shell), $"Waves Central installed no bridged {Shell}");
            await manager.Close();
        }

        private void RestoreTheSession()
        {
            var user = Directory.GetDirectories(Path.Combine(Harness.Prefix, "drive_c", "users"))
                .Single(directory => Path.GetFileName(directory) != "Public");
            var preferences = Path.Combine(user, "AppData", "Roaming", "Waves Audio", "Preferences");
            Directory.CreateDirectory(preferences);
            var settings = JsonNode.Parse(Convert.FromBase64String(Credentials.Read()["WAVES"]))!.AsObject();
            settings["UserSettings"]!["tours"] = new JsonObject { ["welcomeVersion"] = 1 };
            File.WriteAllText(Path.Combine(preferences, "Waves Central.json"), settings.ToJsonString());
        }
    }
}
