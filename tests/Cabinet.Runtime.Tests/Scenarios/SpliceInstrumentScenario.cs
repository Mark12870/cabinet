namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class SpliceInstrumentScenario(SpliceInstrumentScenario.Installed installed)
    : IClassFixture<SpliceInstrumentScenario.Installed>
{
    private const string Id = "splice-instrument";

    private const string Window = "Splice INSTRUMENT";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Splice/Splice INSTRUMENT.vst3",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public void OpensItsSignedOutEditor(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var editor = installed.Harness.VerifyEditor(bridge, controlsAreParameters: false, still: 12);

        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        private const string References = "tests/Cabinet.Runtime.Tests/Scenarios/References/";

        private const string WelcomePage = References + "splice-welcome.png";

        private const string WelcomeHeading = "514,380 817,400";

        private const string SignInPage = "https://auth.splice.com/activate";

        private static readonly TimeSpan Opening = TimeSpan.FromMinutes(3);

        private static readonly TimeSpan Answering = TimeSpan.FromMinutes(1);

        private protected override async Task Install(Display display)
        {
            await base.Install(display);
            var captured = Path.Combine(Harness.Home, "browser.txt");
            var capture = Path.Combine(Harness.Home, "browser.sh");
            File.WriteAllText(capture, "#!/bin/sh\npart=$(mktemp \"$HOME/browser.XXXXXX\")\nprintf '%s\\n' \"$1\" > \"$part\"\nln \"$part\" \"$HOME/browser.txt\" 2>/dev/null\nrm -f \"$part\"\n");
            File.SetUnixFileMode(capture, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Harness.Merge($"[Software\\\\Wine\\\\WineBrowser] 0\n\"Browsers\"=\"{capture}\"");

            using var manager = Harness.Open(display);
            var window = manager.Window(Window, Opening);
            manager.Until(() => manager.Shows(window, WelcomePage, WelcomeHeading), window, "Splice's welcome page", Opening);
            await OpenSignIn(display, manager, window, captured);
            Assert.StartsWith(SignInPage, File.ReadAllText(captured).Trim(), StringComparison.Ordinal);
            await manager.Close();
        }

        private async Task OpenSignIn(Display display, Manager manager, string window, string captured)
        {
            var deadline = DateTime.UtcNow + Answering;
            while (!File.Exists(captured))
            {
                if (DateTime.UtcNow >= deadline)
                {
                    manager.Capture(window, "gave-up");
                    throw new TimeoutException($"Splice's browser sign-in link did not happen within {Answering}");
                }

                if (manager.Shows(window, WelcomePage, WelcomeHeading))
                {
                    await Harness.SendText(display, Window, 665, 555, "");
                }

                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
    }
}
