namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class MinimalAudioHubScenario(MinimalAudioHubScenario.Installed installed)
    : IClassFixture<MinimalAudioHubScenario.Installed>
{
    private const string Id = "minimal-audio-hub";

    private const string Mix = "Mix";

    private const string Depth = "0 200 230";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Squash.vst3",
        ["VST2"] = "Squash.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task SignsInInstallsSquashAndProcessesAudioWithAWorkingEditor(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);
        var audio = await installed.Harness.Render(bridge, Mix, installed.Display);

        Assert.True(audio.Parameters > 0, "Squash exposed no parameters");
        Assert.True(audio.MixChanged, "Squash's Mix did not hold fully wet");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Peak, 0.06, 1);

        var editor = installed.Harness.VerifyEditor(bridge, control: Depth);
        Assert.Equal("control-0", editor.Aimed);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        private const string Title = "Minimal Hub";

        private const string References = "tests/Cabinet.Runtime.Tests/Scenarios/References/";

        private const string LogInPage = References + "minimal-log-in.png";

        private const string LogInHeading = "70,188 320,238";

        private const string WelcomePage = References + "minimal-welcome.png";

        private const string WelcomeHeading = "40,84 340,134";

        private const string SoftwarePage = References + "minimal-software.png";

        private const string SoftwareTab = "22,94 152,124";

        private const string SquashPage = References + "minimal-squash.png";

        private const string SquashRow = "74,202 159,226";

        private const string InstallMenu = References + "minimal-install-menu.png";

        private const string InstallItem = "824,248 924,275";

        private const string InstallPage = References + "minimal-install-squash.png";

        private const string SquashHeading = "38,200 248,234";

        private const string CompletePage = References + "minimal-install-complete.png";

        private const string CompleteHeading = "38,242 468,277";

        private const string CredentialFields = "60,250 420,440 rectangle 180,0 1000,65";

        private const string Account = "180,0 1000,65";

        private const string LogInThenLetsGo = "200 298 200 297";

        private static readonly TimeSpan Opening = TimeSpan.FromMinutes(3);

        private static readonly TimeSpan Answering = TimeSpan.FromMinutes(1);

        private static readonly TimeSpan Downloading = TimeSpan.FromMinutes(5);

        private protected override async Task Install(Display display)
        {
            await base.Install(display);
            using var manager = Harness.Open(display);
            var window = manager.Window(Title, Opening);
            manager.Until(() => manager.Shows(window, LogInPage, LogInHeading), window, "the log-in page", Opening);
            var credentials = Credentials.Read("EMAIL", "PASSWORD");
            await Harness.SendText(display, Title, 240, 310, credentials["EMAIL"]);
            await Harness.SendText(display, Title, 240, 395, credentials["PASSWORD"]);
            await Harness.SendText(display, Title, 240, 460, "");
            manager.Until(
                () => manager.Shows(window, WelcomePage, WelcomeHeading),
                window, "the welcome page", Answering, CredentialFields);
            manager.Choose(window, 426, 88);
            manager.Until(() => manager.Shows(window, SoftwarePage, SoftwareTab), window, "My Software", Answering, Account);
            manager.Resize(window, 1000, 750);
            manager.Until(() => manager.Shows(window, SquashPage, SquashRow), window, "Squash ready to install", Answering, Account);
            manager.Choose(window, 948, 216);
            manager.Until(() => manager.Shows(window, InstallMenu, InstallItem), window, "the install menu", Answering, Account);
            manager.Choose(window, 860, 260);
            manager.Until(() => manager.Shows(window, InstallPage, SquashHeading), window, "the Squash installer", Answering, Account);
            manager.Choose(window, 924, 588);
            manager.Until(
                () => Harness.Holds("VST3", Bridges["VST3"]) && Harness.Holds("VST2", Bridges["VST2"])
                    && manager.Shows(window, CompletePage, CompleteHeading),
                window, "Squash installed and bridged", Downloading, Account);
            manager.Capture(window, "installed", Account);
            manager.Choose(window, 926, 520);
            await manager.Close();
            Activate();
        }

        private void Activate()
        {
            var credentials = Credentials.Read("EMAIL", "PASSWORD");
            Harness.VerifyEditor(
                Harness.Plugin("VST3", Bridges["VST3"]) with { Label = "activation" },
                controlsAreParameters: false,
                type: $"200 196\n200 180\t{credentials["EMAIL"]}\n200 225\t{credentials["PASSWORD"]}",
                click: LogInThenLetsGo);
            Assert.True(
                File.Exists(Path.Combine(Harness.Prefix, "drive_c", "ProgramData", "Minimal", "Squash", "minimal.license")),
                "Squash did not activate with the Minimal Audio account");
        }
    }
}
