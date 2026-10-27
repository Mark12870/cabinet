namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class IzotopeProductPortalScenario(IzotopeProductPortalScenario.Installed installed)
    : IClassFixture<IzotopeProductPortalScenario.Installed>
{
    private const string Id = "izotope-product-portal";

    private const string Mix = "Dust Amount";

    private static readonly Dictionary<string, string> Bridges = new()
    {
        ["VST3"] = "iZotope/Vinyl.vst3",
        ["VST2"] = "Vinyl.so",
    };

    public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task SignsInInstallsVinylAndItCracklesBeforeAnyInput(string format)
    {
        Assert.True(
            Bridges.ContainsKey(format),
            $"{Id} declares {format}, which this scenario does not say how to find");
        var bridge = installed.Harness.Plugin(format, Bridges[format]);

        var editor = installed.Harness.VerifyEditor(bridge);
        Assert.True(
            editor.Wine != "none" && editor.Wine == editor.Told,
            $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
        var audio = await installed.Harness.Render(bridge, Mix, installed.Display);

        Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
        Assert.InRange(audio.Before, 0.00025, 1);
        Assert.InRange(audio.Tail, 0.00024, 1);
        Assert.InRange(audio.Peak, 0.028, 1);
    }

    public sealed class Installed() : InstalledEntry(Id)
    {
        private const string LogIn = "iZotope Login";

        private const string Title = "iZotope Product Portal";

        private const string References = "tests/Cabinet.Runtime.Tests/Scenarios/References/";

        private const string LogInPage = References + "izotope-log-in.png";

        private const string LogInHeading = "189,262 401,294";

        private const string PasswordField = References + "izotope-password.png";

        private const string Password = "40,432 552,484";

        private const string ConsentPage = References + "izotope-consent.png";

        private const string Consent = "76,244 530,280";

        private const string ProductsPage = References + "izotope-my-products.png";

        private const string ProductsTab = "10,82 200,126";

        private const string VinylToInstall = References + "izotope-vinyl.png";

        private const string VinylRow = "38,210 218,266";

        private const string AlmostDone = References + "izotope-almost-done.png";

        private const string AlmostDoneHeading = "158,150 448,194";

        private const string ComputerChoice = References + "izotope-authorize-to.png";

        private const string ComputerChoiceHeading = "140,188 470,228";

        private const string Authorised = References + "izotope-success.png";

        private const string AuthorisedHeading = "236,132 370,166";

        private static readonly TimeSpan Opening = TimeSpan.FromMinutes(3);

        private static readonly TimeSpan Answering = TimeSpan.FromMinutes(1);

        private static readonly TimeSpan Downloading = TimeSpan.FromMinutes(10);

        private protected override async Task Install(Display display)
        {
            await base.Install(display);
            var credentials = Credentials.Read("EMAIL", "PASSWORD");

            using var manager = Harness.Open(display);
            var login = manager.Window(LogIn, Opening);
            manager.Until(() => manager.Shows(login, LogInPage, LogInHeading), login, "the log-in page", Opening);
            manager.ScrollDown(login, 300, 400, 12);
            manager.Until(() => manager.Shows(login, PasswordField, Password), login, "the password field", Answering);
            manager.Type(login, 300, 393, credentials["EMAIL"]);
            manager.Type(login, 300, 458, credentials["PASSWORD"]);
            manager.Press(login, "Return");
            var window = manager.Window(Title, Answering);

            manager.Until(() => manager.Shows(window, ConsentPage, Consent), window, "the usage consent", Opening);
            manager.Click(window, 303, 596);
            manager.Until(() => manager.Shows(window, ProductsPage, ProductsTab), window, "the product list", Answering);
            manager.Until(() => manager.Shows(window, VinylToInstall, VinylRow), window, "Vinyl in My Products", Answering);
            manager.Choose(window, 510, 277);
            manager.Until(() => Harness.Holds("VST3", Bridges["VST3"]), window, "Vinyl installed and bridged", Downloading);
            manager.Until(() => manager.Shows(window, AlmostDone, AlmostDoneHeading), window, "the authorisation", Answering);
            manager.Choose(window, 303, 527);
            manager.Until(() => manager.Shows(window, ComputerChoice, ComputerChoiceHeading), window, "the choice of computer", Answering);
            manager.Choose(window, 303, 357);
            manager.Choose(window, 303, 562);
            manager.Until(() => manager.Shows(window, Authorised, AuthorisedHeading), window, "Vinyl authorised", Answering);
            await manager.Close();
        }
    }
}
