namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class ArturiaSoftwareCenterScenario(ArturiaSoftwareCenterScenario.Installed installed)
    : IClassFixture<ArturiaSoftwareCenterScenario.Installed>
{
    private const string Id = "arturia-software-center";

    private const int Note = 60;

    private const string Demo = "511 604";

    private const string MasterVolume = "20 1131 660";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Piano V3.vst3",
        ["VST2"] = "Piano V3.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task SignsInInstallsThePianoV3DemoAndPlaysIt(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");

        var audio = await installed.Harness.Play(installed.Harness.Plugin(format, Bridges[format]), Note, installed.Display);

        Assert.True(audio.Parameters > 0, "Piano V3 exposed no parameters");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0.000035, 1);
        Assert.InRange(audio.Peak, 0.02, 1);
    }

    [Fact]
    public void OpensThePianoV3DemoEditorOnceAsVst3()
    {
        var editor = installed.Harness.VerifyEditor(
            installed.Harness.Plugin("VST3", Bridges["VST3"]), click: Demo, control: MasterVolume);

        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        private const string Title = "Arturia Software Center";

        private const string References = "tests/Cabinet.Runtime.Tests/Scenarios/References/";

        private const string EmailPage = References + "arturia-email.png";

        private const string EmailButton = "425,422 535,453";

        private const string PasswordPage = References + "arturia-password.png";

        private const string LogInButton = "490,422 600,453";

        private const string Blue = References + "arturia-blue.png";

        private const string LightBlue = References + "arturia-light-blue.png";

        private const string FirstStep = "394,52 406,60";

        private const string SecondStep = "474,52 486,60";

        private const string NextButton = "812,570 824,578";

        private const string ConsentPage = References + "arturia-consent.png";

        private const string ConsentButton = "425,526 535,548";

        private const string MyProductsPage = References + "arturia-my-products.png";

        private const string MyProductsHeading = "259,24 390,50";

        private const string ExplorePage = References + "arturia-explore.png";

        private const string ExploreHeading = "259,22 439,50";

        private const string PianoV3 = References + "arturia-piano-v3.png";

        private const string ProductInstalled = References + "arturia-installed.png";

        private const string CannotInstall = References + "arturia-cannot-install.png";

        private const string ProductName = "369,93 500,112";

        private const string ProductLink = "369,142 512,158";

        private const string Refusal = "345,234 585,254";

        private const string Account = "760,10 945,62";

        private static readonly TimeSpan Opening = TimeSpan.FromMinutes(3);

        private static readonly TimeSpan Answering = TimeSpan.FromMinutes(1);

        private static readonly TimeSpan Downloading = TimeSpan.FromMinutes(20);

        private protected override async Task Install(Display display)
        {
            await base.Install(display);
            var credentials = Credentials.Read();

            using var manager = Harness.Open(display);
            var window = manager.Window(Title, Opening);
            manager.Until(() => manager.Shows(window, EmailPage, EmailButton), window, "the online email page", Opening);
            manager.Type(window, 480, 242, credentials["EMAIL"]);
            manager.Press("Return");
            manager.Until(() => manager.Shows(window, PasswordPage, LogInButton), window, "the password page", Answering);
            manager.Type(window, 480, 301, credentials["PASSWORD"]);
            manager.Click(window, 545, 437);
            manager.Until(
                () => !manager.Shows(window, PasswordPage, LogInButton), window, "the sign-in", Answering, hidden: null);

            manager.Until(
                () => manager.Shows(window, Blue, FirstStep) && manager.Shows(window, Blue, NextButton),
                window,
                "the welcome page",
                Answering);
            manager.Click(window, 864, 579);
            manager.Until(
                () => manager.Shows(window, Blue, SecondStep) && manager.Shows(window, LightBlue, NextButton),
                window,
                "the install locations page",
                Answering);
            manager.Click(window, 864, 579);
            manager.Until(() => manager.Shows(window, ConsentPage, ConsentButton), window, "the usage data question", Answering);
            manager.Click(window, 480, 571);

            manager.Until(
                () => manager.Shows(window, MyProductsPage, MyProductsHeading), window, "My Products", Answering, Account);
            manager.Until(
                () =>
                {
                    manager.Click(window, 82, 201);
                    return manager.Shows(window, ExplorePage, ExploreHeading);
                },
                window,
                "Explore Products",
                Answering,
                Account);
            manager.Type(window, 600, 36, "Piano V3");
            manager.Until(() => manager.Shows(window, PianoV3, ProductName), window, "Piano V3 in the search", Answering, Account);
            manager.Click(window, 880, 135);
            manager.Until(
                () => manager.Shows(window, ProductInstalled, ProductLink) || manager.Shows(window, CannotInstall, Refusal),
                window,
                "Piano V3's install",
                Downloading,
                Account);
            manager.Capture(window, "installed", Account);
            Assert.False(manager.Shows(window, CannotInstall, Refusal), "Arturia Software Center could not start the install");
            await manager.Close();
        }

        protected override void Settle(string home)
        {
            var settings = Path.Combine(Harness.Prefix, "drive_c", "ProgramData", "Arturia", "Piano V3", "tmp");
            Directory.CreateDirectory(settings);
            File.WriteAllText(
                Path.Combine(settings, "plugin.pref.xml"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <rootnode>
                	<param name="CalibrationDone" value="1.000000"/>
                	<gui name="StartTutoOnLaunch" value="0.000000"/>
                </rootnode>
                """);
        }
    }
}
