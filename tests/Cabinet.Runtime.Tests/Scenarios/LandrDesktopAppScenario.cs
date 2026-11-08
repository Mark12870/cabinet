namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class LandrDesktopAppScenario(LandrDesktopAppScenario.Installed installed)
    : IClassFixture<LandrDesktopAppScenario.Installed>
{
    private const string Id = "landr-desktop-app";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "LANDR Mastering Plugin.vst3",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public void InstallsTheMasteringPlugin(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");

        Assert.True(
            installed.Harness.Holds(format, Bridges[format]),
            $"{Id} installed but left no bridged {Bridges[format]} for {format}");
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        private const string Title = "LANDR";

        private static readonly TimeSpan Opening = TimeSpan.FromMinutes(3);

        private static readonly TimeSpan Answering = TimeSpan.FromMinutes(1);

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
            manager.Window(Title, Opening);
            Thread.Sleep(Step * 3);
            var window = manager.Window(Title, Opening);
            manager.Capture(window, "opened");
            manager.HoverClick(window, 595, 503);
            Thread.Sleep(Step);
            manager.Capture(window, "started");
            manager.Until(
                () =>
                {
                    manager.HoverClick(window, 595, 524);
                    Thread.Sleep(Step);
                    return File.Exists(captured);
                },
                window,
                "the device code",
                Answering);

            ReachTheLogIn(File.ReadAllText(captured).Trim(), Harness.Artefacts);
            await manager.Close();
        }

        private static void ReachTheLogIn(string url, string evidence)
        {
            using var browser = new Browser(evidence);
            browser.Go(url);
            browser.Await(() => browser.Shows("button"), Answering, "the device confirmation page");
            browser.Press("Confirm and continue");
            browser.Await(
                () => browser.Shows("input[type=email]") && browser.Shows("input[type=password]"),
                Answering,
                "the log-in page");
        }
    }
}
