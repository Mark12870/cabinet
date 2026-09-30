namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class KlevgrandHelperScenario(KlevgrandHelperScenario.Installed installed)
    : IClassFixture<KlevgrandHelperScenario.Installed>
{
    private const string Id = "klevgrand-helper";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "FreeAMP.vst3",
        ["VST2"] = "FreeAMP.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task SignsInInstallsFreeAmpAndProcessesAudio(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var audio = await installed.Harness.Render(
            installed.Harness.Plugin(format, Bridges[format]), "", installed.Display);

        Assert.True(audio.Parameters > 0, "FreeAMP exposed no parameters");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0, 0.00001);
        Assert.InRange(audio.Peak, 0.25, 0.35);
    }

    [Fact]
    public void OpensAndOperatesTheFreeAmpEditorOnceAsVst3()
    {
        installed.Harness.VerifyEditor(
            installed.Harness.Plugin("VST3", Bridges["VST3"]), controlsAreParameters: false);
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        private const string Title = "Klevgrand Helper";

        private const string References = "tests/Cabinet.Runtime.Tests/Scenarios/References/";

        private const string LogInPage = References + "klevgrand-log-in.png";

        private const string LogInForm = "190,259 449,408";

        private const string TermsPage = References + "klevgrand-terms.png";

        private const string TermsPanel = "260,200 560,400";

        private const string CredentialFields = "190,272 449,360";

        private const string InstalledPage = References + "klevgrand-installed.png";

        private const string InstalledCard = "29,110 400,239";

        private const string Account = "600,0 820,180";

        private static readonly TimeSpan Opening = TimeSpan.FromMinutes(3);

        private static readonly TimeSpan Answering = TimeSpan.FromMinutes(1);

        private static readonly TimeSpan Downloading = TimeSpan.FromMinutes(5);

        private protected override async Task Install(Display display)
        {
            await base.Install(display);
            var credentials = Credentials.Read();

            {
                using var login = Harness.Open(display);
                var loginWindow = login.Window(Title, Opening);
                login.Until(() => login.Shows(loginWindow, LogInPage, LogInForm), loginWindow, "the log-in page", Opening);
                await Harness.SendText(display, Title, 320, 288, credentials["EMAIL"]);
                await Harness.SendText(display, Title, 320, 343, credentials["PASSWORD"]);
                await Harness.SendText(display, Title, 229, 392, "");
                login.Until(
                    () => login.Shows(loginWindow, TermsPage, TermsPanel),
                    loginWindow,
                    "the terms",
                    Answering,
                    CredentialFields);
                await Harness.SendText(display, Title, 305, 372, "");
                await Harness.SendText(display, Title, 496, 372, "");
                await login.WaitForExit(TimeSpan.FromMinutes(2), "sign-in");
            }

            using var manager = Harness.Open(display);
            var window = manager.Window(Title, Opening);
            Thread.Sleep(TimeSpan.FromSeconds(10));
            manager.Resize(window, 1000, 700);
            Thread.Sleep(TimeSpan.FromSeconds(2));
            await Harness.SendText(display, Title, 450, 23, "");
            Thread.Sleep(TimeSpan.FromSeconds(2));
            await Harness.SendText(display, Title, 850, 69, "");
            Thread.Sleep(TimeSpan.FromSeconds(2));
            await Harness.SendText(display, Title, 625, 516, "");
            Thread.Sleep(TimeSpan.FromSeconds(2));
            await Harness.SendText(display, Title, 945, 678, "");
            manager.Until(
                () => Harness.Holds("VST3", Bridges["VST3"]) && Harness.Holds("VST2", Bridges["VST2"]),
                window,
                "FreeAMP installed and bridged",
                Downloading,
                Account);
            await Harness.SendText(display, Title, 258, 23, "");
            window = manager.Window(Title, Answering);
            manager.Until(
                () => manager.Shows(window, InstalledPage, InstalledCard),
                window,
                "FreeAMP among My Plug-ins",
                Answering,
                Account);
            manager.Capture(window, "installed", Account);
            await manager.Close();
        }
    }
}
