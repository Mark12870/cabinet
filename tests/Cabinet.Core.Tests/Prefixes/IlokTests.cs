using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class IlokTests : IDisposable
{
    private readonly string root = TestRoot.Create("ilok");

    public void Dispose() => Directory.Delete(root, recursive: true);

    private Layout Layout => new(root, Path.Combine(root, "run"), Path.Combine(root, "data"));

    private Ilok Subject => new(Layout, new UnusedRunner());

    [Fact]
    public void APrefixReportsNoIlokUntilItIsInstalled()
    {
        Booted("gadget");

        Assert.Null(Subject.InstalledIn("gadget"));
    }

    [Fact]
    public void TheMarkerNamesTheVersionThatWasInstalled()
    {
        Booted("gadget");
        File.WriteAllText(Layout.PrefixIlokFile("gadget"), "5.10.5\n");

        Assert.Equal("5.10.5", Subject.InstalledIn("gadget"));
    }

    [Fact]
    public void APrefixThatWasNeverBootedIsRefused()
    {
        Directory.CreateDirectory(Layout.PrefixPath("gadget"));

        Assert.Throws<DirectoryNotFoundException>(() => Subject.Install("gadget"));
        Assert.Throws<DirectoryNotFoundException>(() => Subject.Remove("gadget"));
    }

    [Fact]
    public void ThePackageFromTheSetupIsInstalledQuietlyWithoutItsUsbDriver()
    {
        Booted("gadget");
        var recorder = new RecordingRunner(IlokDownload.Serving, outputs: IlokDownload.Answering());

        var version = new Ilok(Layout, recorder).Install("gadget");

        var msiexec = Assert.Single(recorder.Ran, call => call.Arguments is ["msiexec", ..]);
        Assert.Equal(["msiexec", "/i", "/qn", "NODRIVER=1"], msiexec.Arguments.Where((_, at) => at != 2));
        Assert.StartsWith("Z:" + Layout.TempDir.Replace('/', '\\'), msiexec.Arguments[2]);
        Assert.EndsWith(@"\" + Ilok.Package, msiexec.Arguments[2]);
        Assert.Equal(Layout.PrefixPath("gadget"), msiexec.Environment["WINEPREFIX"]);
        Assert.Equal(IlokDownload.Version, version);
        Assert.Equal(IlokDownload.Version, Subject.InstalledIn("gadget"));
    }

    [Fact]
    public void TheVersionIsTakenFromTheFolderTheSetupShipsIn()
    {
        Booted("gadget");
        var said = new List<string>();

        new Ilok(Layout, new RecordingRunner(IlokDownload.Serving, outputs: IlokDownload.Answering())).Install("gadget", said.Add);

        Assert.Equal($"gadget now licenses through iLok {IlokDownload.Version}.", said[^1]);
    }

    [Fact]
    public void TheDownloadIsTheOnePaceAlwaysPublishesTheLatestBuildAt()
    {
        Booted("gadget");
        var recorder = new RecordingRunner(IlokDownload.Serving, outputs: IlokDownload.Answering());

        new Ilok(Layout, recorder).Install("gadget");

        var curl = Assert.Single(recorder.Calls, call => call.File == "curl");
        Assert.Equal(Ilok.Url, curl.Arguments[^1]);
    }

    [Fact]
    public void TheUnverifiableDownloadIsReportedBeforeItStarts()
    {
        Booted("gadget");
        var said = new List<string>();
        var beforeProcesses = new List<string>();
        var recorder = new RecordingRunner(
            arguments =>
            {
                beforeProcesses.AddRange(said);
                IlokDownload.Serving(arguments);
            },
            outputs: IlokDownload.Answering());

        new Ilok(Layout, recorder).Install("gadget", said.Add);

        Assert.Equal(Library.Unverifiable(Ilok.Url), beforeProcesses[0]);
    }

    [Fact]
    public void TheWineTheInstallStartedIsEndedWhileThePrefixIsStillClaimed()
    {
        Booted("gadget");
        var recorder = new RecordingRunner(IlokDownload.Serving, outputs: IlokDownload.Answering());

        new Ilok(Layout, recorder).Install("gadget");

        Assert.Equal(["-k"], recorder.Ran[^1].Arguments);
        Assert.EndsWith("wineserver", recorder.Ran[^1].File);
    }

    [Fact]
    public void APackageWindowsInstallerRefusedLeavesThePrefixWithoutIlok()
    {
        Booted("gadget");
        var failing = new RecordingRunner(
            IlokDownload.Serving,
            exits: args => args is ["msiexec", ..] ? 1603 : 0,
            outputs: IlokDownload.Answering());

        var refused = Assert.Throws<InvalidOperationException>(() => new Ilok(Layout, failing).Install("gadget"));

        Assert.Contains("1603", refused.Message);
        Assert.Null(Subject.InstalledIn("gadget"));
    }

    [Fact]
    public void AnInstallWhoseLicenceServiceStoppedIsAFailureThatCanStillBeTakenOut()
    {
        Booted("gadget");
        var recorder = new RecordingRunner(IlokDownload.Serving, outputs: IlokDownload.Answering("STOPPED"));

        var refused = Assert.Throws<InvalidOperationException>(() => new Ilok(Layout, recorder).Install("gadget"));

        Assert.Contains($"{Ilok.Service} is not running", refused.Message);
        Assert.Equal(IlokDownload.Version, Subject.InstalledIn("gadget"));
    }

    [Fact]
    public void TheLicenceServiceIsAskedAboutBeforeTheInstallsWineIsEnded()
    {
        Booted("gadget");
        var recorder = new RecordingRunner(IlokDownload.Serving, outputs: IlokDownload.Answering());

        new Ilok(Layout, recorder).Install("gadget");

        Assert.Equal(["sc", "query", Ilok.Service], recorder.Ran[^2].Arguments);
    }

    [Fact]
    public void TakingIlokOutUninstallsThePackageByItsProductCode()
    {
        Installed("gadget");
        var recorder = new RecordingRunner();

        new Ilok(Layout, recorder).Remove("gadget");

        Assert.Single(
            recorder.Ran,
            call => call.Arguments.SequenceEqual(["msiexec", "/x", "{218BEA78-4687-418e-A876-96B307B3133D}", "/qn"]));
        Assert.Null(Subject.InstalledIn("gadget"));
    }

    [Fact]
    public void AnUninstallWindowsInstallerRefusedKeepsTheMarker()
    {
        Installed("gadget");

        Assert.Throws<InvalidOperationException>(
            () => new Ilok(Layout, new StubRunner(new ProcessResult(1603, "", ""))).Remove("gadget"));

        Assert.Equal(IlokDownload.Version, Subject.InstalledIn("gadget"));
    }

    [Fact]
    public void TakingIlokOutOfAPrefixThatNeverHadItIsRefused()
    {
        Booted("gadget");

        Assert.Throws<InvalidOperationException>(() => Subject.Remove("gadget"));
    }

    private void Booted(string prefix) =>
        Directory.CreateDirectory(Path.Combine(Layout.PrefixPath(prefix), "dosdevices"));

    private void Installed(string prefix)
    {
        Booted(prefix);
        File.WriteAllText(Layout.PrefixIlokFile(prefix), IlokDownload.Version + "\n");
    }
}
