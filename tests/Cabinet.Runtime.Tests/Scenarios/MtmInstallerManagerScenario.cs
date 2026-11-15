namespace Cabinet.Runtime.Tests.Scenarios;

public sealed class MtmInstallerManagerScenario(MtmInstallerManagerScenario.Installed installed)
    : IClassFixture<MtmInstallerManagerScenario.Installed>
{
    private const string Id = "mtm-installer-manager";

    private const string Program = "installer manager.exe";

    private const string LicencePage = "tests/Cabinet.Runtime.Tests/Scenarios/References/mastering-the-mix-licence.png";

    private const string LicenceHeading = "296,146 592,176";

    private static readonly TimeSpan Opening = TimeSpan.FromMinutes(3);

    [Fact]
    public async Task OpensAndDrawsItsLicenceAgreement()
    {
        using var manager = installed.Harness.Open(installed.Display);
        var window = manager.UntitledWindow(Program, Opening);
        manager.Until(() => manager.Shows(window, LicencePage, LicenceHeading), window, "the licence agreement", Opening);
        manager.Capture(window, "licence");
        await manager.Close();
    }

    public sealed class Installed() : InstalledEntry(Id);
}
