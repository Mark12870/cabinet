namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class InmusicSoftwareCenterScenario(InmusicSoftwareCenterScenario.Installed installed)
    : IClassFixture<InmusicSoftwareCenterScenario.Installed>
{
    private const string Id = "inmusic-software-center";

    private const int Note = 48;

    private const string TryUnlicensed = "300 447 300 447";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Bassline.vst3",
        ["VST2"] = "Bassline_x64.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public void OpensAndOperatesUnlicensedBasslinesEditor(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var editor = installed.Harness.VerifyEditor(bridge, click: TryUnlicensed);

        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task PlaysUnlicensedBassline(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var audio = await installed.Harness.Play(bridge, Note, installed.Display, click: TryUnlicensed);

        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0.0000012, 1);
        Assert.InRange(audio.Peak, 0.04, 1);
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        private const string Title = "inMusic Software Center";

        private const string SignedIn = "device.inmusicbrands.com/";

        private static readonly TimeSpan Opening = TimeSpan.FromMinutes(3);

        private static readonly TimeSpan Answering = TimeSpan.FromMinutes(1);

        private static readonly TimeSpan Installing = TimeSpan.FromMinutes(3);

        private static readonly TimeSpan Step = TimeSpan.FromSeconds(8);

        private protected override async Task Install(Display display)
        {
            await base.Install(display);
            var captured = Path.Combine(Harness.Home, "browser.txt");
            var capture = Path.Combine(Harness.Home, "browser.sh");
            File.WriteAllText(capture, "#!/bin/sh\nprintf '%s\\n' \"$1\" > \"$HOME/browser.txt\"\n");
            File.SetUnixFileMode(capture, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Harness.Merge($"[Software\\\\Wine\\\\WineBrowser] 0\n\"Browsers\"=\"{capture}\"");

            using var manager = Harness.Open(display);
            var window = manager.Window(Title, Opening);
            Thread.Sleep(Step);
            manager.Until(
                () =>
                {
                    manager.HoverClick(window, 328, 483);
                    Thread.Sleep(Step);
                    return File.Exists(captured);
                },
                window,
                "the sign-in page",
                Answering);

            SignIn(File.ReadAllText(captured).Trim(), Harness.Artefacts);
            Thread.Sleep(Step * 4);
            manager.HoverClick(window, 600, 471);
            Thread.Sleep(Step * 2);
            manager.HoverClick(window, 1020, 732);
            manager.HoverClick(window, 100, 405);
            Thread.Sleep(Step * 2);
            manager.Type(window, 570, 44, "Bassline");
            Thread.Sleep(Step * 2);
            manager.HoverClick(window, 1070, 297);
            Thread.Sleep(Step * 6);
            manager.HoverClick(window, 1072, 297);
            manager.Until(
                () =>
                {
                    manager.Press("Return");
                    Thread.Sleep(Step);
                    return Harness.Holds("VST3", Bridges["VST3"]) && Harness.Holds("VST2", Bridges["VST2"]);
                },
                window,
                "Bassline installed and bridged",
                Installing);
            manager.Capture(window, "installed");
            await manager.Close();
        }

        private static void SignIn(string url, string evidence)
        {
            var credentials = Credentials.Read("EMAIL", "PASSWORD");
            using var browser = new Browser(evidence);
            browser.Go(url);
            browser.Await(
                () => browser.Shows("input[type=email]") && browser.Shows("input[type=password]"),
                Answering,
                "the sign-in page");
            browser.Type("input[type=email]", credentials["EMAIL"]);
            browser.Type("input[type=password]", credentials["PASSWORD"]);
            browser.Press("Log In");
            browser.Await(() => browser.Path() == SignedIn, Answering, "the manager's sign-in");
        }
    }
}
