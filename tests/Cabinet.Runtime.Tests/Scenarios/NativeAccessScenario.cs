namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class NativeAccessScenario(NativeAccessScenario.Installed installed)
    : IClassFixture<NativeAccessScenario.Installed>
{
    private const string Id = "native-access";

    private const string Title = "Native Access";

    private const string SignInLink = "native-access://authorize?code=cabinet-scenario&state=cabinet-scenario";

    private const string References = "tests/Cabinet.Runtime.Tests/Scenarios/References/";

    private const string LicencePage = References + "native-access-licence.png";

    private const string LicenceHeading = "256,205 566,235";

    private const string AgreeButton = References + "native-access-agree.png";

    private const string Agree = "654,643 768,668";

    private const string LogInPage = References + "native-access-log-in.png";

    private const string LogInPrompt = "430,395 594,465";

    private const string RefusedPage = References + "native-access-unable.png";

    private const string Refusal = "270,385 750,460";

    private static readonly TimeSpan Opening = TimeSpan.FromMinutes(3);

    private static readonly TimeSpan Answering = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task HandsItsSignInLinkToNativeAccess()
    {
        using var manager = installed.Harness.Open(installed.Display);
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
        manager.Until(() => manager.Shows(window, LogInPage, LogInPrompt), window, "the log-in page", Answering);

        await installed.Harness.Hand(SignInLink, installed.Display);

        manager.Until(() => manager.Shows(window, RefusedPage, Refusal), window, "Native Access acting on the link", Answering);
        manager.Capture(window, "refused");
        await manager.Close();
    }

    public sealed class Installed() : InstalledEntry(Id);
}
