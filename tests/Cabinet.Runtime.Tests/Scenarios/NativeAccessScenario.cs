namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class NativeAccessScenario(NativeAccessScenario.Installed installed)
    : IClassFixture<NativeAccessScenario.Installed>
{
    private const string Id = "native-access";

    private const string Mix = "Mix";

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

        private static readonly TimeSpan Opening = TimeSpan.FromMinutes(3);

        private static readonly TimeSpan Answering = TimeSpan.FromMinutes(1);

        private static readonly TimeSpan Downloading = TimeSpan.FromMinutes(20);

        private protected override async Task Install(Display display)
        {
            await base.Install(display);
            Harness.Restore(Credentials.Read()["NATIVE_ACCESS"]);

            using var manager = Harness.Open(display);
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
            manager.Capture(window, "installed");
            manager.Until(() => Harness.Holds("VST3", "Raum.vst3"), window, "Cabinet bridging Raum.vst3", Answering);
            await manager.Close();
        }
    }
}
