namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class PaInstallationManagerScenario(PaInstallationManagerScenario.Installed installed)
    : IClassFixture<PaInstallationManagerScenario.Installed>
{
    private const string Id = "pa-installation-manager";

    private const string Mix = "Input";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Apogee Soft Limit.vst3",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task SignsInInstallsAndActivatesSoftLimitAndProcessesAudioWithAWorkingEditor(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);
        var audio = await installed.Harness.Render(bridge, Mix, installed.Display);

        Assert.True(audio.Parameters > 0, "Soft Limit exposed no parameters");
        Assert.True(audio.MixChanged, "Soft Limit's Input did not hold at its highest");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Peak, 0.06, 1);

        var editor = installed.Harness.VerifyEditor(bridge);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        private const string References = "tests/Cabinet.Runtime.Tests/Scenarios/References/";

        private const string LicencePage = References + "plugin-alliance-licence.png";

        private const string LicenceButtons = "796,643 1010,681";

        private const string LogInPage = References + "plugin-alliance-log-in.png";

        private const string LogInButton = "410,392 692,432";

        private const string CredentialFields = "400,290 700,372";

        private const string ProductsPage = References + "plugin-alliance-products.png";

        private const string ProductsHeading = "464,140 750,176";

        private const string Account = "900,15 1040,60";

        private const string FoundProduct = References + "plugin-alliance-soft-limit.png";

        private const string FoundRow = "505,330 625,360";

        private const string DownloadedPage = References + "plugin-alliance-downloaded.png";

        private const string DownloadedHeading = "430,30 660,80";

        private const string Vst2Page = References + "plugin-alliance-vst2-path.png";

        private const string Vst2Heading = "470,30 630,80";

        private const string InstalledPage = References + "plugin-alliance-installed.png";

        private const string InstalledHeading = "430,380 670,425";

        private const string Activate = "358 337";

        private static readonly TimeSpan Opening = TimeSpan.FromMinutes(3);

        private static readonly TimeSpan Answering = TimeSpan.FromMinutes(1);

        private static readonly TimeSpan Downloading = TimeSpan.FromMinutes(5);

        private protected override async Task Install(Display display)
        {
            await base.Install(display);
            using var manager = Harness.Open(display);
            var window = manager.Window($"Plugin Alliance Installation Manager V{Entry.Version}", Opening);
            manager.Until(() => manager.Shows(window, LicencePage, LicenceButtons), window, "the licence", Opening);
            manager.Until(() => Pressed(manager, window, 846, 661, LogInPage, LogInButton), window, "the log-in page", Answering);
            var credentials = Credentials.Read("EMAIL", "PASSWORD");
            manager.HoldClick(window, 550, 307);
            manager.Type(window, 550, 307, credentials["EMAIL"]);
            manager.HoldClick(window, 550, 354);
            manager.Type(window, 550, 354, credentials["PASSWORD"]);
            manager.HoldClick(window, 550, 411);
            manager.Until(
                () => manager.Shows(window, ProductsPage, ProductsHeading), window, "the product list", Answering, CredentialFields);
            manager.HoldClick(window, 600, 192);
            manager.Type(window, 600, 192, "Soft Limit");
            manager.Until(() => manager.Shows(window, FoundProduct, FoundRow), window, "Soft Limit found", Answering, Account);
            manager.HoldClick(window, 519, 344);
            manager.HoldClick(window, 36, 512);
            manager.HoldClick(window, 928, 670);
            manager.Until(
                () => manager.Shows(window, DownloadedPage, DownloadedHeading), window, "Soft Limit downloaded", Downloading, Account);
            manager.Until(() => Pressed(manager, window, 703, 255, Vst2Page, Vst2Heading), window, "the VST2 path", Answering, Account);
            manager.HoldClick(window, 714, 255);
            manager.Until(
                () => manager.Shows(window, InstalledPage, InstalledHeading)
                    && Harness.Holds("VST3", Bridges["VST3"]),
                window,
                "Soft Limit installed and bridged",
                Downloading,
                Account);
            manager.Capture(window, "installed", Account);
            await manager.Close();
            DeclineUsageTracking();
            Activation(credentials);
        }

        private static bool Pressed(Manager manager, string window, int x, int y, string reference, string region)
        {
            manager.HoldClick(window, x, y);
            return manager.Shows(window, reference, region);
        }

        private string Settings()
        {
            var user = Directory.GetDirectories(Path.Combine(Harness.Prefix, "drive_c", "users"))
                .Single(directory => Path.GetFileName(directory) != "Public");
            return Path.Combine(user, "AppData", "Roaming", "Plugin Alliance");
        }

        private void DeclineUsageTracking()
        {
            Directory.CreateDirectory(Settings());
            File.WriteAllText(Path.Combine(Settings(), "pa.analytics"), """{"AnalyticsState":"Disabled"}""");
        }

        private void Activation(IReadOnlyDictionary<string, string> credentials)
        {
            PluginAlliance.RetireStaleMachines(credentials, Environment.MachineName, Path.Combine(Harness.Artefacts, "devices"));
            Harness.VerifyEditor(
                Harness.Plugin("VST3", Bridges["VST3"]) with { Label = "activation" },
                controlsAreParameters: false,
                type: $"450 253\n37 239\n85 270\n215 164\t{credentials["EMAIL"]}\n215 192\t{credentials["PASSWORD"]}",
                click: Activate);
            Assert.True(
                File.Exists(Path.Combine(Settings(), "pa.license")),
                "Soft Limit did not activate with the Plugin Alliance account");
        }
    }
}
