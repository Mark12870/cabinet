using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class PrefixOwnershipTests : IDisposable
{
    private readonly string root = TestRoot.Create("prefix-ownership");

    private Layout Layout => new(root, Path.Combine(root, "run"), Path.Combine(root, "data"));

    private Prefixes Subject => new(Layout, new RecordingRunner());

    public PrefixOwnershipTests()
    {
        Directory.CreateDirectory(Path.Combine(Layout.PrefixPath("gadget"), "dosdevices"));
        Directory.CreateDirectory(Layout.SocketDir);
    }

    [Fact]
    public void ADawsPluginsKeepTheirPrefixFromBeingDeleted()
    {
        using var plugin = SessionFiles.HeldByAPlugin(Busy);

        var refused = Assert.Throws<PrefixInUseException>(() => Subject.Delete("gadget"));

        Assert.Contains("A DAW is using plugins from gadget", refused.Message);
        Assert.Contains("delete gadget", refused.Message);
        Assert.True(Directory.Exists(Layout.PrefixPath("gadget")));
    }

    [Fact]
    public void ADawsPluginsKeepTheirPrefixOnTheWineItIsRunning()
    {
        GiveRunner("soda-11.0-5");
        using var plugin = SessionFiles.HeldByAPlugin(Busy);

        Assert.Throws<PrefixInUseException>(() => Subject.SetRunner("gadget", "soda-11.0-5"));
        Assert.False(File.Exists(Layout.PrefixRunnerFile("gadget")));
    }

    [Fact]
    public void ADawsPluginsKeepTheirPrefixOnTheSynchronisationItStartedWith()
    {
        using var plugin = SessionFiles.HeldByAPlugin(Busy);

        Assert.Throws<PrefixInUseException>(() => Subject.SetSync("gadget", SyncMode.Fsync));
        Assert.Equal(SyncMode.System, new PrefixSettings(Layout).Sync("gadget"));
    }

    [Fact]
    public void ADawsPluginsKeepTheirPrefixsWindowsWhereTheyAre()
    {
        var runner = new RecordingRunner();
        using var plugin = SessionFiles.HeldByAPlugin(Busy);

        Assert.Throws<PrefixInUseException>(
            () => new VirtualDesktop(Layout, runner).Set("gadget", null));
        Assert.Empty(runner.Ran);
    }

    [Fact]
    public void ADawsPluginsKeepDxvkInTheirPrefix()
    {
        var runner = new RecordingRunner();
        File.WriteAllText(Layout.PrefixDxvkFile("gadget"), Dxvk.Version + "\n");
        using var plugin = SessionFiles.HeldByAPlugin(Busy);

        Assert.Throws<PrefixInUseException>(() => new Dxvk(Layout, runner).Remove("gadget"));
        Assert.Equal(Dxvk.Version, new Dxvk(Layout, runner).InstalledIn("gadget"));
    }

    [Fact]
    public void ADawsPluginsKeepAnInstallerOutOfTheirPrefix()
    {
        var installer = Path.Combine(root, "setup.exe");
        File.WriteAllText(installer, "");
        var runner = new RecordingRunner();
        using var plugin = SessionFiles.HeldByAPlugin(Busy);

        Assert.Throws<PrefixInUseException>(
            () => new Prefixes(Layout, runner).Install("gadget", installer));
        Assert.Empty(runner.Ran);
    }

    [Fact]
    public void AnAppCabinetOpenedKeepsTheirPrefixAsItIs()
    {
        using var open = Subject.OpenApp("gadget", "open Thing");

        var refused = Assert.Throws<PrefixInUseException>(() => Subject.Delete("gadget"));

        Assert.Contains("An app Cabinet opened is still running in gadget", refused.Message);
    }

    [Fact]
    public void AChangeUnderWayKeepsAnAppFromOpeningInThatPrefix()
    {
        using var claim = Subject.Claim("gadget", "delete gadget");

        var refused = Assert.Throws<PrefixInUseException>(
            () => Subject.OpenApp("gadget", "open Thing"));

        Assert.Contains("Cabinet is changing gadget right now", refused.Message);
    }

    [Fact]
    public void AWineSessionThatHasNotRetiredKeepsAChangeWaiting()
    {
        var prefixes = new Prefixes(Layout, new RecordingRunner(dawSession: true));
        File.WriteAllText(SessionFiles.Of(Layout, "gadget").Socket, "");

        var refused = Assert.Throws<PrefixInUseException>(
            () => prefixes.Claim("gadget", "delete gadget", TimeSpan.FromMilliseconds(1)));

        Assert.Contains("gadget's Wine session is still finishing", refused.Message);
    }

    [Fact]
    public void ASocketACrashedSessionLeftBehindDoesNotHoldUpAChange()
    {
        File.WriteAllText(SessionFiles.Of(Layout, "gadget").Socket, "");

        using var claim = Subject.Claim("gadget", "delete gadget", TimeSpan.FromMilliseconds(1));

        Assert.True(File.Exists(SessionFiles.Of(Layout, "gadget").Change));
    }

    [Fact]
    public void APrefixWithNoSessionAtAllIsClaimedAtOnce()
    {
        using var claim = Subject.Claim("gadget", "delete gadget");

        Assert.True(File.Exists(SessionFiles.Of(Layout, "gadget").Change));
    }

    [Fact]
    public void ARunnerALiveSessionIsUsingIsNotRemoved()
    {
        GiveRunner("soda-11.0-5");
        var session = SessionFiles.Of(Layout, "gadget");
        File.WriteAllText(session.Socket, "");
        File.WriteAllText(
            session.Record,
            $"prefix {Layout.PrefixPath("gadget")}\n"
            + $"runner {Path.Combine(Layout.RunnerPath("soda-11.0-5"), "bin", "wine")}\n");

        var refused = Assert.Throws<PrefixInUseException>(
            () => new Runners(Layout, new RecordingRunner()).Remove("soda-11.0-5"));

        Assert.Contains("soda-11.0-5 is still running Wine for gadget", refused.Message);
        Assert.True(Directory.Exists(Layout.RunnerPath("soda-11.0-5")));
    }

    [Fact]
    public void ARunnerWhoseSessionHasRetiredIsRemoved()
    {
        GiveRunner("soda-11.0-5");
        var session = SessionFiles.Of(Layout, "gadget");
        File.WriteAllText(
            session.Record,
            $"prefix {Layout.PrefixPath("gadget")}\n"
            + $"runner {Path.Combine(Layout.RunnerPath("soda-11.0-5"), "bin", "wine")}\n");

        new Runners(Layout, new RecordingRunner()).Remove("soda-11.0-5");

        Assert.False(Directory.Exists(Layout.RunnerPath("soda-11.0-5")));
    }

    private string Busy => SessionFiles.Of(Layout, "gadget").Busy;

    private void GiveRunner(string name)
    {
        var wine = Path.Combine(Layout.RunnerPath(name), "bin", "wine");
        Directory.CreateDirectory(Path.GetDirectoryName(wine)!);
        File.WriteAllText(wine, "");
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
