namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class SpliceInstrumentScenario(SpliceInstrumentScenario.Installed installed)
    : IClassFixture<SpliceInstrumentScenario.Installed>
{
    private const string Id = "splice-instrument";

    private const int Note = 60;

    private const string Volume = "1 1230 42";

    private const string Window = "Splice INSTRUMENT";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Splice/Splice INSTRUMENT.vst3",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public void OpensPianoGranularMovesItsVolumeAndBrowsesPresets(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var editor = installed.Harness.VerifyEditor(bridge, control: Volume, press: "827 42", still: 12);

        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void PlaysPianoGranular(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var audio = installed.Harness.PlayThroughEditor(bridge, Note, "wait 15", Window);

        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Held, 0.004, 1);
        Assert.InRange(audio.After, 0.003, 1);
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        private const string References = "tests/Cabinet.Runtime.Tests/Scenarios/References/";

        private const string WelcomePage = References + "splice-welcome.png";

        private const string WelcomeHeading = "514,380 817,400";

        private const string PresetPage = References + "splice-select-preset.png";

        private const string Selection = "569,353 760,375";

        private const string UsagePage = References + "splice-usage-statistics.png";

        private const string UsageHeading = "592,408 737,433";

        private const string LoadedPage = References + "splice-piano-granular.png";

        private const string PresetName = "940,160 1070,217";

        private static readonly TimeSpan Opening = TimeSpan.FromMinutes(3);

        private static readonly TimeSpan Answering = TimeSpan.FromMinutes(1);

        private static readonly TimeSpan Downloading = TimeSpan.FromMinutes(10);

        private protected override async Task Install(Display display)
        {
            await base.Install(display);
            var captured = Path.Combine(Harness.Home, "browser.txt");
            var capture = Path.Combine(Harness.Home, "browser.sh");
            File.WriteAllText(capture, "#!/bin/sh\nprintf '%s\\n' \"$1\" > \"$HOME/browser.part\"\nmv \"$HOME/browser.part\" \"$HOME/browser.txt\"\n");
            File.SetUnixFileMode(capture, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Harness.Merge($"[Software\\\\Wine\\\\WineBrowser] 0\n\"Browsers\"=\"{capture}\"");

            using var manager = Harness.Open(display);
            var window = manager.Window(Window, Opening);
            manager.Until(() => manager.Shows(window, WelcomePage, WelcomeHeading), window, "Splice's welcome page", Opening);
            await Harness.SendText(display, Window, 665, 555, "");
            manager.Until(() => File.Exists(captured), window, "Splice's browser sign-in link", Answering);
            SignIn(File.ReadAllText(captured).Trim());
            File.Delete(captured);
            manager.Until(() => manager.Shows(window, PresetPage, Selection), window, "the signed-in preset selection", Opening);

            await Harness.SendText(display, Window, 480, 624, "");
            manager.Until(() => manager.Shows(window, UsagePage, UsageHeading), window, "the usage statistics question", Downloading);
            await Harness.SendText(display, Window, 560, 563, "");
            manager.Until(() => manager.Shows(window, LoadedPage, PresetName), window, "Piano Granular downloaded and loaded", Downloading);
            manager.Capture(window, "loaded");
            await manager.Close();
        }

        private static void SignIn(string url)
        {
            var credentials = Credentials.Read();
            using var browser = new Browser();
            browser.Go(url);
            Browser.Until(() => browser.Shows("button[name=action]"), Answering, "Splice's device confirmation");
            browser.Press("Confirm");
            Browser.Until(() => browser.Shows("#username") && browser.Shows("#password"), Answering, "Splice's password page");
            browser.Type("#username", credentials["EMAIL"]);
            browser.Type("#password", credentials["PASSWORD"]);
            browser.Press("Continue");
            Browser.Until(() => browser.Path() == "auth.splice.com/device/success", Answering, "Splice authorising INSTRUMENT");
        }
    }
}
