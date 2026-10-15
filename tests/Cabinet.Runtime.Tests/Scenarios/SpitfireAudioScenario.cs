namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class SpitfireAudioScenario(SpitfireAudioScenario.Installed installed)
    : IClassFixture<SpitfireAudioScenario.Installed>
{
    private const string Id = "spitfire-audio";

    private const int Note = 60;

    private const string Window = "^BBC Symphony Orchestra";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "BBC Symphony Orchestra (64 Bit).vst3",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public void OpensAndOperatesDiscoversEditor(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var editor = installed.Harness.VerifyEditor(bridge);

        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void PlaysDiscoversViolins(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var audio = installed.Harness.PlayThroughEditor(bridge, Note, "wait 15", Window);

        Assert.InRange(audio.Before, 0, 0.00001);
        Assert.InRange(audio.Held, 0.025, 1);
        Assert.InRange(audio.After, 0.02, 1);
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        private const string Title = "Spitfire Audio";

        private const string References = "tests/Cabinet.Runtime.Tests/Scenarios/References/";

        private const string ConsentPage = References + "spitfire-consent.png";

        private const string Accept = "604,505 730,535";

        private const string SignInPage = References + "spitfire-sign-in.png";

        private const string SignInHeading = "350,226 412,248";

        private const string ContentPathPage = References + "spitfire-content-path.png";

        private const string ContentPathHeading = "420,258 605,280";

        private const string Vst2x86PathPage = References + "spitfire-vst2-32-bit.png";

        private const string Vst2x64PathPage = References + "spitfire-vst2-64-bit.png";

        private const string Vst2PathHeading = "400,257 620,281";

        private const string ProductsPage = References + "spitfire-discover.png";

        private const string Discover = "18,155 250,412";

        private const string DownloadPage = References + "spitfire-download.png";

        private const string DownloadHeading = "435,168 594,196";

        private const string DownloadsPage = References + "spitfire-downloads-complete.png";

        private const string NoDownloads = "420,386 620,412";

        private const string ProductPage = References + "spitfire-product.png";

        private const string ProductTitle = "15,570 232,594";

        private const string GearMenu = References + "spitfire-gear.png";

        private const string GearItems = "860,575 980,715";

        private const string ResetDialog = References + "spitfire-reset-dialog.png";

        private const string ResetQuestion = "360,272 664,298";

        private const string ResetPage = References + "spitfire-reset.png";

        private const string InstallButton = "773,608 881,634";

        private const string Account = "700,0 1024,180";

        private const string SignInFields = "300,285 800,410 rectangle " + Account;

        private static readonly TimeSpan Opening = TimeSpan.FromMinutes(3);

        private static readonly TimeSpan Answering = TimeSpan.FromMinutes(1);

        private static readonly TimeSpan Downloading = TimeSpan.FromMinutes(10);

        private static readonly TimeSpan Settling = TimeSpan.FromSeconds(3);

        private protected override async Task Install(Display display)
        {
            await base.Install(display);
            using var manager = Harness.Open(display);
            var window = manager.Window(Title, Opening);
            manager.Until(() => manager.Shows(window, ConsentPage, Accept), window, "the consent screen", Opening);
            manager.Choose(window, 667, 520);

            manager.Until(() => manager.Shows(window, SignInPage, SignInHeading), window, "the Sign In page", Answering);
            var credentials = Credentials.Read();
            await Harness.SendText(display, Title, 510, 300, credentials["EMAIL"]);
            Thread.Sleep(Settling);
            await Harness.SendText(display, Title, 510, 353, credentials["PASSWORD"]);
            manager.Choose(window, 620, 440);
            manager.Press(window, "Return");

            manager.Until(
                () => manager.Shows(window, ContentPathPage, ContentPathHeading),
                window, "the content path", Answering, SignInFields);
            manager.Choose(window, 759, 585);
            manager.Until(
                () => manager.Shows(window, Vst2x86PathPage, Vst2PathHeading),
                window, "the 32-bit VST2 path", Answering, Account);
            manager.Choose(window, 759, 585);
            manager.Until(
                () => manager.Shows(window, Vst2x64PathPage, Vst2PathHeading),
                window, "the 64-bit VST2 path", Answering, Account);
            manager.Choose(window, 759, 585);

            manager.Until(
                () => manager.Shows(window, ProductsPage, Discover),
                window, "BBC Symphony Orchestra Discover to install", Answering, Account);
            manager.Choose(window, 193, 398);
            manager.Until(
                () => manager.Shows(window, DownloadPage, DownloadHeading),
                window, "the BBCSO Discover download", Answering, Account);
            manager.Choose(window, 673, 577);
            manager.Until(
                () => Directory.Exists(Plugin) && manager.Shows(window, DownloadsPage, NoDownloads),
                window, "BBCSO Discover downloaded", Downloading, Account);

            manager.Choose(window, 66, 35);
            Thread.Sleep(Settling);
            manager.Choose(window, 134, 265);
            manager.Until(
                () => manager.Shows(window, ProductPage, ProductTitle),
                window, "BBCSO Discover's product page", Answering, Account);
            manager.Choose(window, 991, 620);
            manager.Until(
                () => manager.Shows(window, GearMenu, GearItems),
                window, "the installed product's menu with Reset", Answering, Account);
            manager.Choose(window, 901, 695);
            manager.Until(
                () => manager.Shows(window, ResetDialog, ResetQuestion),
                window, "the reset dialog", Answering, Account);
            manager.Choose(window, 504, 488);
            manager.Until(
                () => manager.Shows(window, ResetPage, InstallButton),
                window, "the account's BBCSO Discover reset to install again", Answering, Account);

            manager.Until(
                () => Harness.Holds("VST3", Bridges["VST3"]),
                window, "BBCSO Discover bridged", Answering, Account);
            await manager.Close();
        }

        private string Plugin =>
            Path.Combine(Harness.Prefix, "drive_c", "Program Files", "Common Files", "VST3", Bridges["VST3"]);
    }
}
