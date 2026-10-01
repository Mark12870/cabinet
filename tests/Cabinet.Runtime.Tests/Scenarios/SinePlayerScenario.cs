namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class SinePlayerScenario(SinePlayerScenario.Installed installed)
    : IClassFixture<SinePlayerScenario.Installed>
{
    private const string Id = "sine-player";

    private const int Note = 72;

    private const string LoadLucent = "double 260 220; double 220 433; wait 20";

    private const string Window = "^SINE Player";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "SINE Player.vst3",
        ["VST2"] = "SINE Player.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public void OpensItsLibraryThoughItsControlsAreNotParameters(string format)
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
    public void LoadsLucentAndPlaysItsFlute(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var audio = installed.Harness.PlayThroughEditor(bridge, Note, LoadLucent, Window);

        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Held, 0.004, 1);
        Assert.InRange(audio.After, 0.002, 1);
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        private const string Title = "SINE Player";

        private const string References = "tests/Cabinet.Runtime.Tests/Scenarios/References/";

        private const string LogInPage = References + "sine-log-in.png";

        private const string LogInHeading = "612,315 769,341";

        private const string GettingStartedPage = References + "sine-getting-started.png";

        private const string GettingStarted = "490,395 865,455";

        private const string LicencesPage = References + "sine-berlin-free-orchestra.png";

        private const string Orchestra = "280,329 470,353";

        private const string LucentPage = References + "sine-lucent.png";

        private const string Lucent = "966,471 1062,497";

        private const string Account = "1080,0 1380,140";

        private static readonly TimeSpan Opening = TimeSpan.FromMinutes(3);

        private static readonly TimeSpan Answering = TimeSpan.FromMinutes(1);

        private static readonly TimeSpan Downloading = TimeSpan.FromMinutes(10);

        private protected override async Task Install(Display display)
        {
            await base.Install(display);
            Harness.Merge("[Software\\\\Wine\\\\Drivers] 0\n\"Audio\"=\"\"");
            var library = Path.Combine(Harness.Prefix, "drive_c", "SINE");
            Directory.CreateDirectory(library);
            var credentials = Credentials.Read();

            using var manager = Harness.Open(display);
            var window = manager.Window(Title, Opening);
            manager.Until(() => manager.Shows(window, LogInPage, LogInHeading), window, "the log-in page", Opening);
            manager.Type(window, 690, 394, credentials["EMAIL"]);
            manager.Type(window, 690, 454, credentials["PASSWORD"]);
            manager.Click(window, 690, 552);
            manager.Until(
                () => manager.Shows(window, GettingStartedPage, GettingStarted),
                window, "the signed-in tutorial", Answering, "550,374 830,474");
            await Harness.SendText(display, Title, 1040, 246, "");
            manager.Until(
                () => manager.Shows(window, LicencesPage, Orchestra),
                window, "the licensed instruments", Answering, Account);
            manager.ScrollDown(window, 1200, 700, 3);
            manager.Until(() => manager.Shows(window, LucentPage, Lucent), window, "Lucent in SINEfactory", Answering, Account);
            await Harness.SendText(display, Title, 1070, 727, "");

            var folder = manager.Window("Please select a destination for your product downloads...", Answering);
            manager.Type(folder, 410, 446, @"C:\SINE");
            manager.Click(folder, 562, 474);
            manager.Until(
                () => Directory.EnumerateDirectories(library).Any(instrument =>
                    File.Exists(Path.Combine(instrument, "dry", "CWF - DRY.otmeta"))
                    && File.Exists(Path.Combine(instrument, "wet", "CWF - WET.otmeta"))),
                window, "Lucent installed", Downloading, Account);
            manager.Capture(window, "installed", Account);
            await manager.Close();
        }
    }
}
