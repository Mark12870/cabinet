namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class IkProductManagerScenario(IkProductManagerScenario.Installed installed)
    : IClassFixture<IkProductManagerScenario.Installed>
{
    private const string Id = "ik-product-manager";

    private const int Note = 40;

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "MODO BASS 2.vst3",
        ["VST2"] = "MODO BASS 2.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task SignsInInstallsModoBass2CsAndPlaysItThoughItsControlsAreNotParameters(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var editor = installed.Harness.VerifyEditor(bridge, controlsAreParameters: false);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Play(bridge, Note, installed.Display);

        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0.0000003, 1);
        Assert.InRange(audio.Peak, 0.045, 1);
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        private const string Title = "IK Multimedia Product Manager";

        private const string Setup = "Setup - MODO BASS 2";

        private const string References = "tests/Cabinet.Runtime.Tests/Scenarios/References/";

        private const string LogInPage = References + "ik-log-in.png";

        private const string LogInHeading = "266,136 526,162";

        private const string SoftwarePage = References + "ik-software.png";

        private const string SoftwareTab = "20,69 130,95";

        private const string ModoBass = References + "ik-modo-bass-2.png";

        private const string ModoBassName = "82,187 227,209";

        private const string LicencePage = References + "ik-setup-licence.png";

        private const string ReadyPage = References + "ik-setup-ready.png";

        private const string InstallingPage = References + "ik-setup-installing.png";

        private const string Heading = "20,8 135,24";

        private const string CompletingPage = References + "ik-setup-completing.png";

        private const string Completing = "175,15 425,37";

        private const string Authorised = References + "ik-authorized.png";

        private const string Authorisation = "133,244 215,262";

        private const string Account = "381,0 690,45";

        private static readonly TimeSpan Opening = TimeSpan.FromMinutes(3);

        private static readonly TimeSpan Answering = TimeSpan.FromMinutes(1);

        private static readonly TimeSpan Downloading = TimeSpan.FromMinutes(20);

        private protected override async Task Install(Display display)
        {
            await base.Install(display);
            var credentials = Credentials.Read();

            using var manager = Harness.Open(display);
            var window = manager.Window(Title, Opening);
            manager.Until(() => manager.Shows(window, LogInPage, LogInHeading), window, "the log-in page", Opening);
            manager.Click(window, 396, 308);
            Thread.Sleep(TimeSpan.FromSeconds(1));
            manager.Type(window, 396, 308, credentials["USERNAME"]);
            manager.Type(window, 396, 370, credentials["PASSWORD"]);
            manager.Click(window, 310, 439);
            manager.Until(
                () => !manager.Shows(window, LogInPage, LogInHeading), window, "the sign-in", Answering, hidden: null);

            manager.Until(() => manager.Shows(window, SoftwarePage, SoftwareTab), window, "the Software list", Answering, Account);
            manager.Until(() => manager.Shows(window, ModoBass, ModoBassName), window, "MODO BASS 2 CS in the list", Answering, Account);
            manager.Click(window, 753, 208);

            var setup = manager.Window(Setup, Downloading);
            manager.Click(setup, 363, 339);
            manager.Until(() => manager.Shows(setup, LicencePage, Heading), setup, "the licence page", Answering);
            manager.Click(setup, 46, 276);
            foreach (var page in Enumerable.Range(0, 6))
            {
                Thread.Sleep(TimeSpan.FromSeconds(2));
                manager.Click(setup, 363, 339);
            }

            manager.Until(() => manager.Shows(setup, ReadyPage, Heading), setup, "the Ready to Install page", Answering);
            manager.Click(setup, 363, 339);
            manager.Until(() => manager.Shows(setup, InstallingPage, Heading), setup, "the install", Answering);
            manager.Until(() => manager.Shows(setup, CompletingPage, Completing), setup, "the Completing page", Downloading);
            manager.Click(setup, 363, 339);

            manager.Until(() => manager.Shows(window, Authorised, Authorisation), window, "MODO BASS 2 CS's authorisation", Answering, Account);
            manager.Capture(window, "authorised", Account);
            await manager.Close();
        }
    }
}
