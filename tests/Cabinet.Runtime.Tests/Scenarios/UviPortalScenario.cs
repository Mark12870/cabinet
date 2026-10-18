namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class UviPortalScenario(UviPortalScenario.Installed installed)
    : IClassFixture<UviPortalScenario.Installed>
{
    private const string Id = "uvi-portal";

    private const int Note = 60;

    private const string LoadANewWorld = "click 598 150; wait 1; double 598 150; wait 30";

    private const string Window = "^UVI Player";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "UVI Player.vst3",
        ["VST2"] = "UVI Player.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public void OpensAndOperatesUviPlayersEditor(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var editor = installed.Harness.VerifyEditor(bridge, controlsAreParameters: false);

        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void LoadsAStarterPresetAndPlaysIt(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var audio = installed.Harness.PlayThroughEditor(bridge, Note, LoadANewWorld, Window);

        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Held, 0.06, 1);
        Assert.InRange(audio.After, 0.04, 1);
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        private const string Title = "UVI Portal";

        private const string References = "tests/Cabinet.Runtime.Tests/Scenarios/References/";

        private const string LogInPage = References + "uvi-log-in.png";

        private const string Logo = "608,360 992,435";

        private const string SignedIn = References + "uvi-signed-in.png";

        private const string Sidebar = "16,110 250,335";

        private const string PlayerToInstall = References + "uvi-player-install.png";

        private const string InstallButton = "750,331 895,361";

        private const string PlayerInstalled = References + "uvi-player-installed.png";

        private const string InstalledButton = "750,328 895,358";

        private const string CredentialFields = "544,483 1056,589";

        private static readonly TimeSpan Opening = TimeSpan.FromMinutes(3);

        private static readonly TimeSpan Answering = TimeSpan.FromMinutes(1);

        private static readonly TimeSpan Downloading = TimeSpan.FromMinutes(5);

        private protected override async Task Install(Display display)
        {
            await base.Install(display);
            using var manager = Harness.Open(display);
            var window = manager.Window(Title, Opening);
            manager.Until(() => manager.Shows(window, LogInPage, Logo), window, "the sign-in page", Opening);

            var credentials = Credentials.Read();
            await Harness.SendText(display, Title, 800, 505, credentials["EMAIL"]);
            await Harness.SendText(display, Title, 800, 566, credentials["PASSWORD"]);
            await Harness.SendText(display, Title, 800, 627, "");
            manager.Until(
                () => manager.Shows(window, SignedIn, Sidebar), window, "the signed-in Portal", Answering, CredentialFields);

            await Harness.SendText(display, Title, 100, 313, "");
            manager.Until(
                () => manager.Shows(window, PlayerToInstall, InstallButton), window, "UVI Player to install", Answering);
            await Harness.SendText(display, Title, 847, 346, "");
            manager.Until(
                () => Harness.Holds("VST3", Bridges["VST3"]) && manager.Shows(window, PlayerInstalled, InstalledButton),
                window, "UVI Player installed and bridged", Downloading);
            await manager.Close();
        }
    }
}
