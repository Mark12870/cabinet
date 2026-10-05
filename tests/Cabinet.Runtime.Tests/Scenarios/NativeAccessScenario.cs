using System.Text.Json.Nodes;

namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class NativeAccessScenario(NativeAccessScenario.Installed installed)
    : IClassFixture<NativeAccessScenario.Installed>
{
    private const string Id = "native-access";

    private const string Mix = "Mix";

    private const string Kontakt = "Kontakt 8.vst3";

    private const int Note = 60;

    private const string CloseWhatsNewThenLoopsTab = "727 231 329 117";

    private const string KontaktWindow = "^Kontakt 8";

    private const string KontaktApplication =
        "drive_c/Program Files/Native Instruments/Kontakt 8/Kontakt 8.exe";

    private const string KontaktFactoryPresets =
        "drive_c/Program Files/Common Files/Native Instruments/Kontakt 8/Database/Kontakt Factory Presets.kdb";

    private const string FirstAcousticDrumsLoop =
        "click 600 560; click 727 231; click 329 117; wait 3; click 104 375; wait 3; click 975 488; wait 2; "
        + "click 970 152; wait 3; double 856 323; wait 20";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "Raum.vst3",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task InstallsRaumFromARestoredSessionAndReverberates(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var audio = await installed.Harness.Render(bridge, Mix, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.True(audio.MixChanged, $"Raum's {Mix} did not hold fully wet");
        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Tail, 0.0025, 1);
        Assert.InRange(audio.Peak, 0.014, 1);
    }

    [Fact]
    public void OpensKontaktsEditorOnceItsLicenceIsActive()
    {
        var bridge = installed.Harness.Plugin("VST3", Kontakt);

        var editor = installed.Harness.VerifyEditor(
            bridge, controlsAreParameters: false, press: CloseWhatsNewThenLoopsTab);

        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
    }

    [Fact]
    public void KontaktLandsWithItsApplicationFactoryContentAndRegistryValues()
    {
        var registry = File.ReadAllText(Path.Combine(installed.Harness.Prefix, "system.reg"));

        Assert.True(
            File.Exists(Path.Combine(installed.Harness.Prefix, KontaktApplication)),
            $"Kontakt 8's installer left no {KontaktApplication}");
        Assert.True(
            File.Exists(Path.Combine(installed.Harness.Prefix, KontaktFactoryPresets)),
            $"Kontakt 8's installer left no {KontaktFactoryPresets}");
        Assert.Contains(@"""InstallDir""=""C:\\Program Files\\Native Instruments\\Kontakt 8\\""", registry);
        Assert.Contains(
            @"""ContentDir""=""C:\\Program Files\\Common Files\\Native Instruments\\Kontakt 8""", registry);
    }

    [Fact]
    public void PlaysAnAcousticDrumsLoopLoadedThroughKontaktsBrowser()
    {
        var bridge = installed.Harness.Plugin("VST3", Kontakt);

        var played = installed.Harness.PlayThroughEditor(
            bridge, Note, FirstAcousticDrumsLoop, KontaktWindow, release: 3);

        Assert.InRange(played.Before, 0, 0.00001);
        Assert.InRange(played.Held, 0.05, 1);
        Assert.InRange(played.After, 0, 0.00001);
    }

    [Fact]
    public void BringsBackAnAcousticDrumsLoopFromKontaktsSavedState()
    {
        var bridge = installed.Harness.Plugin("VST3", Kontakt);

        var restored = installed.Harness.RestoreAfterPlayingThroughEditor(
            bridge, Note, FirstAcousticDrumsLoop, KontaktWindow, release: 3);

        Assert.InRange(restored.Before, 0, 0.00001);
        Assert.InRange(restored.Held, 0.05, 1);
        Assert.InRange(restored.After, 0, 0.00001);
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        private const string Title = "Native Access";

        private const string References = "tests/Cabinet.Runtime.Tests/Scenarios/References/";

        private const string LicencePage = References + "native-access-licence.png";

        private const string LicenceHeading = "256,205 566,235";

        private const string AgreeButton = References + "native-access-agree.png";

        private const string Agree = "654,643 768,668";

        private const string SignedInPage = References + "native-access-signed-in.png";

        private const string Sidebar = "20,75 150,185";

        private const string RaumCard = References + "native-access-raum.png";

        private const string RaumName = "254,600 314,624";

        private const string RaumInstalled = References + "native-access-raum-installed.png";

        private const string Success = "252,738 462,762";

        private const string KontaktCard = References + "native-access-kontakt.png";

        private const string KontaktName = "764,434 888,458";

        private const string AcousticDrumsCard = References + "native-access-acoustic-drums.png";

        private const string AcousticDrumsName = "254,434 378,458";

        private const string InstalledPage = References + "native-access-installed.png";

        private const string FirstCardInstalled = "256,488 352,511";

        private const string KontaktOpen = References + "native-access-kontakt-open.png";

        private const string ThirdCardButton = "767,488 863,511";

        private const string AcousticDrumsLoads = References + "native-access-acoustic-drums-loads.png";

        private const string LoadsHeading = "270,242 722,272";

        private const string KontaktActivation =
            "drive_c/users/Public/Documents/Native Instruments/Native Access/ras3/"
            + "0e504595-40d8-4982-978e-a242f036912d.jwt";

        private const string AcousticDrumsLibrary =
            "drive_c/users/Public/Documents/Acoustic Drums Library/Acoustic Drums.nicnt";

        private const string FeedbackStore = "2342dc42fd705c1d1004971a4b34109453cc7bd2.json";

        private static readonly TimeSpan Opening = TimeSpan.FromMinutes(3);

        private static readonly TimeSpan Answering = TimeSpan.FromMinutes(1);

        private static readonly TimeSpan Downloading = TimeSpan.FromMinutes(10);

        private protected override async Task Install(Display display)
        {
            Assert.True(
                OtherDaemons().Count == 0,
                "Native Instruments' daemon from another prefix is running on this machine and holds the port "
                + $"every Native Access talks to, so this scenario would sign in to its account: {string.Join(", ", OtherDaemons())}. "
                + "Close Native Access and any Kontakt session first.");
            await base.Install(display);
            Harness.Restore(Credentials.Read()["NATIVE_ACCESS"]);
            DismissTheSurvey();
            await Harness.Set(display, "env", Lavapipe);

            using (var manager = Harness.Open(display))
            {
                var window = manager.Window(Title, Opening);
                manager.Until(() => manager.Shows(window, LicencePage, LicenceHeading), window, "the licence page", Opening);
                manager.Until(
                    () =>
                    {
                        manager.Click(window, 512, 500);
                        manager.Press("End");
                        manager.Press("ctrl+End");
                        return manager.Shows(window, AgreeButton, Agree);
                    },
                    window,
                    "the licence scrolled to its end",
                    Answering);
                manager.Click(window, 711, 655);
                manager.Until(() => manager.Shows(window, SignedInPage, Sidebar), window, "the signed-in home page", Opening);

                manager.Click(window, 82, 130);
                Thread.Sleep(TimeSpan.FromSeconds(10));
                manager.Click(window, 975, 77);
                Thread.Sleep(TimeSpan.FromSeconds(2));
                manager.Click(window, 516, 94);
                manager.Until(() => manager.Shows(window, RaumCard, RaumName), window, "Raum among the effects", Answering);
                manager.Click(window, 303, 665);
                manager.Until(() => manager.Shows(window, RaumInstalled, Success), window, "Raum's install", Downloading);
                manager.Until(() => Harness.Holds("VST3", "Raum.vst3"), window, "Cabinet bridging Raum.vst3", Answering);

                manager.Click(window, 975, 60);
                Thread.Sleep(TimeSpan.FromSeconds(2));
                manager.Click(window, 333, 94);
                manager.Until(() => manager.Shows(window, KontaktCard, KontaktName), window, "Kontakt 8 Player among the applications", Answering);
                manager.Click(window, 814, 499);
                manager.Until(() => Harness.Holds("VST3", Kontakt), window, "Kontakt 8 installed and bridged", Downloading);
                manager.Until(
                    () => File.Exists(Path.Combine(Harness.Prefix, KontaktFactoryPresets)),
                    window,
                    "Kontakt 8's factory content",
                    Downloading);
                manager.Until(() => File.Exists(Path.Combine(Harness.Prefix, KontaktActivation)), window, "Native Access activating Kontakt 8 Player", Opening);
                manager.Until(() => manager.Shows(window, KontaktOpen, ThirdCardButton), window, "Native Access offering to open Kontakt 8 Player", Answering);

                manager.Click(window, 681, 94);
                manager.Until(() => manager.Shows(window, AcousticDrumsCard, AcousticDrumsName), window, "Acoustic Drums among the Kontakt libraries", Answering);
                manager.Click(window, 303, 499);
                manager.Until(() => File.Exists(Path.Combine(Harness.Prefix, AcousticDrumsLibrary)), window, "Acoustic Drums on disk", Downloading);
                manager.Until(() => manager.Shows(window, AcousticDrumsLoads, LoadsHeading), window, "the note that Acoustic Drums loads in Kontakt", Downloading);
                manager.Click(window, 704, 556);
                manager.Until(() => manager.Shows(window, InstalledPage, FirstCardInstalled), window, "Acoustic Drums installed", Downloading);
                manager.Capture(window, "installed");
                await manager.Close();
            }
        }

        private static List<string> OtherDaemons() =>
            Directory.GetDirectories("/proc")
                .Where(process => File.Exists(Path.Combine(process, "comm")))
                .Where(process => File.ReadAllText(Path.Combine(process, "comm")).StartsWith("NTKDaemon", StringComparison.Ordinal))
                .Select(process => Path.GetFileName(process))
                .ToList();

        private void DismissTheSurvey()
        {
            var user = Directory.GetDirectories(Path.Combine(Harness.Prefix, "drive_c", "users"))
                .Single(directory => Path.GetFileName(directory) != "Public");
            var store = Path.Combine(user, "AppData", "Roaming", "Native Instruments", "Native Access");
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Directory.CreateDirectory(store);
            File.WriteAllText(
                Path.Combine(store, FeedbackStore),
                new JsonObject
                {
                    ["userFeedbackRecords"] = new JsonArray(
                        new JsonObject
                        {
                            ["context"] = "all installs complete",
                            ["displayedAt"] = now,
                            ["question"] = "How is your experience so far?",
                            ["action"] = "dismissed",
                            ["resolvedAt"] = now,
                        }),
                }.ToJsonString());
        }
    }
}
