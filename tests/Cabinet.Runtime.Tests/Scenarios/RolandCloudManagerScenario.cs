namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class RolandCloudManagerScenario(RolandCloudManagerScenario.Installed installed)
    : IClassFixture<RolandCloudManagerScenario.Installed>
{
    private const string Id = "roland-cloud-manager";

    private const string Zenology = "Roland/ZENOLOGY/ZENOLOGY.vst3";

    private const int Note = 60;

    [Fact]
    public async Task SignsInInstallsZenologyLiteAndPlaysIt()
    {
        var audio = await installed.Harness.Play(installed.Harness.Plugin("VST3", Zenology), Note, installed.Display);

        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0.00009, 1);
        Assert.InRange(audio.Peak, 0.0005, 1);
    }

    [Fact]
    public void OpensAndOperatesTheZenologyLiteEditorWhoseKnobsAreNotHostParameters()
    {
        var editor = installed.Harness.VerifyEditor(
            installed.Harness.Plugin("VST3", Zenology), controlsAreParameters: false);

        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        private const string LogIn = "Roland Cloud Manager - Login";

        private const string Signed = "Roland Cloud Manager";

        private const string Scheme = "rolandcloudmanager://";

        private const string Device = "00000000-0000-4000-8000-00000000cab1";

        private static readonly TimeSpan Opening = TimeSpan.FromMinutes(3);

        private static readonly TimeSpan Answering = TimeSpan.FromMinutes(1);

        private static readonly TimeSpan Downloading = TimeSpan.FromMinutes(10);

        private static readonly TimeSpan Step = TimeSpan.FromSeconds(8);

        private protected override async Task Install(Display display)
        {
            await base.Install(display);
            await Harness.Set(display, "env", Lavapipe);
            var captured = Path.Combine(Harness.Home, "browser.txt");
            var capture = Path.Combine(Harness.Home, "browser.sh");
            File.WriteAllText(capture, "#!/bin/sh\nprintf '%s\\n' \"$1\" > \"$HOME/browser.txt\"\n");
            File.SetUnixFileMode(capture, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Harness.Merge($"[Software\\\\Wine\\\\WineBrowser] 0\n\"Browsers\"=\"{capture}\"");
            Harness.Merge($"[Software\\\\Microsoft\\\\Cryptography] 0\n\"MachineGuid\"=\"{Device}\"", "system.reg");

            using var manager = Harness.Open(display);
            var login = manager.Window(LogIn, Opening);
            Thread.Sleep(Step);
            manager.Until(
                () =>
                {
                    manager.Click(login, 224, 439);
                    Thread.Sleep(Step);
                    return File.Exists(captured);
                },
                login,
                "the sign-in page",
                Answering);

            await Harness.OpenLink(SignIn(File.ReadAllText(captured).Trim()), display);
            var window = manager.Window(Signed, Opening);
            Thread.Sleep(Step);
            manager.Click(window, 640, 254);
            Thread.Sleep(Step);
            manager.Click(window, 258, 25);
            Thread.Sleep(Step);
            manager.Click(window, 112, 77);
            Thread.Sleep(Step);
            manager.Click(window, 373, 459);
            manager.Until(() => Harness.Holds("VST3", Zenology), window, "ZENOLOGY Lite installed and bridged", Downloading);
            manager.Capture(window, "installed");
            await manager.Close();
        }

        private static string SignIn(string url)
        {
            var credentials = Credentials.Read();
            using var browser = new Browser();
            browser.Go(url);
            Browser.Until(() => browser.Shows("input[type=email]"), Answering, "the email page");
            browser.Script("[...document.querySelectorAll('a')].find(a => /Sign in here/.test(a.innerText)).click()");
            Browser.Until(
                () => browser.Shows("input[type=email]") && browser.Shows("input[type=password]"),
                Answering,
                "the password page");
            browser.Type("input[type=email]", credentials["EMAIL"]);
            browser.Type("input[type=password]", credentials["PASSWORD"]);
            browser.Press("LOG IN");
            Browser.Until(() => browser.Path().EndsWith("/verify", StringComparison.Ordinal), Answering, "the account's verification");
            browser.Press("START APP");
            return Browser.Until(
                () => browser.Requests().FirstOrDefault(request => request.StartsWith(Scheme, StringComparison.Ordinal)),
                Answering,
                "the link back to the manager");
        }
    }
}
