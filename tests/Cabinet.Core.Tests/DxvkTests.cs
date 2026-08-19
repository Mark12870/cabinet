using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class DxvkTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("cabinet").FullName;

    private Layout Layout => new(root, "/run/user/1000", Path.Combine(root, "data"));

    private Dxvk Subject => new(Layout, new UnusedRunner());

    [Fact]
    public void APrefixReportsNoDxvkUntilItIsInstalled()
    {
        Directory.CreateDirectory(Layout.PrefixPath("serum"));

        Assert.Null(Subject.InstalledIn("serum"));
    }

    [Fact]
    public void TheMarkerNamesTheVersionThatWasUnpacked()
    {
        Directory.CreateDirectory(Layout.PrefixPath("serum"));
        File.WriteAllText(Layout.PrefixDxvkFile("serum"), "2.7.1\n");

        Assert.Equal("2.7.1", Subject.InstalledIn("serum"));
    }

    [Fact]
    public void AnEmptyMarkerCountsAsNoDxvk()
    {
        Directory.CreateDirectory(Layout.PrefixPath("serum"));
        File.WriteAllText(Layout.PrefixDxvkFile("serum"), "  \n");

        Assert.Null(Subject.InstalledIn("serum"));
    }

    [Fact]
    public void APrefixThatWasNeverBootedIsRefused()
    {
        Directory.CreateDirectory(Layout.PrefixPath("serum"));

        Assert.Throws<DirectoryNotFoundException>(() => Subject.Install("serum"));
    }

    [Fact]
    public void TheDownloadIsPinnedToTheVersionItChecksums()
    {
        Assert.Contains($"v{Dxvk.Version}/", Dxvk.Url);
        Assert.Equal(64, Dxvk.Sha256.Length);
    }

    [Fact]
    public void TakingDxvkOutPutsBackWhatItReplaced()
    {
        Booted("serum");
        var system32 = Path.Combine(Layout.PrefixSystem32("serum"), "d3d11.dll");
        var backup = Path.Combine(Layout.PrefixDxvkBackup("serum", "system32"), "d3d11.dll");
        Write(backup, "wine");
        Write(system32, "dxvk");

        Installed("serum").Remove("serum");

        Assert.Equal("wine", File.ReadAllText(system32));
        Assert.False(Directory.Exists(Layout.PrefixDxvkBackupDir("serum")));
        Assert.Null(Subject.InstalledIn("serum"));
    }

    [Fact]
    public void APrefixDxvkdBeforeBackupsExistedIsHandedBackToWineRatherThanLeftEmpty()
    {
        Booted("serum");
        var system32 = Path.Combine(Layout.PrefixSystem32("serum"), "dxgi.dll");
        Write(system32, "dxvk");

        var recorder = new RecordingRunner();
        File.WriteAllText(Layout.PrefixDxvkFile("serum"), Dxvk.Version);
        new Dxvk(Layout, recorder).Remove("serum");

        Assert.False(File.Exists(system32));
        Assert.Equal(["-u"], recorder.LastArguments);
        Assert.Contains("wineboot", recorder.LastFile);
    }

    [Fact]
    public void TakingDxvkOutOfAPrefixThatNeverHadItKeepsWinesOwnDirect3D()
    {
        Booted("serum");
        var system32 = Path.Combine(Layout.PrefixSystem32("serum"), "d3d11.dll");
        Write(system32, "wine");

        Assert.Throws<InvalidOperationException>(() => Subject.Remove("serum"));

        Assert.Equal("wine", File.ReadAllText(system32));
    }

    [Fact]
    public void EveryOverrideDxvkAddedIsTakenBackOut()
    {
        Backed("serum");

        var recorder = new RecordingRunner();
        new Dxvk(Layout, recorder).Remove("serum");

        var deleted = recorder.Calls
            .Where(call => call.Arguments.Contains("delete"))
            .Select(call => call.Arguments[^2])
            .ToList();

        Assert.Equal(Dxvk.Libraries, deleted);
    }

    [Fact]
    public void AnOverrideWineHasAlreadyForgottenIsNotAFailure()
    {
        Backed("serum");

        var refusing = new StubRunner(new ProcessResult(1, "", "reg: Unable to find"));
        new Dxvk(Layout, refusing).Remove("serum");

        Assert.Null(Subject.InstalledIn("serum"));
    }

    [Fact]
    public void APrefixWithEveryBackupIsNeverHandedToWineboot()
    {
        Backed("serum");

        var recorder = new RecordingRunner();
        new Dxvk(Layout, recorder).Remove("serum");

        Assert.DoesNotContain(recorder.Calls, call => call.File.Contains("wineboot"));
    }

    [Fact]
    public void APrefixWineCouldNotRecoverStillReadsAsDxvkd()
    {
        Booted("serum");
        Write(Path.Combine(Layout.PrefixSystem32("serum"), "dxgi.dll"), "dxvk");
        File.WriteAllText(Layout.PrefixDxvkFile("serum"), Dxvk.Version);

        var failing = new StubRunner(new ProcessResult(1, "", ""));

        Assert.Throws<InvalidOperationException>(() => new Dxvk(Layout, failing).Remove("serum"));

        Assert.Equal(Dxvk.Version, Subject.InstalledIn("serum"));
    }

    [Fact]
    public void TakingDxvkOutOfAPrefixThatWasNeverBootedIsRefused()
    {
        Directory.CreateDirectory(Layout.PrefixPath("serum"));

        Assert.Throws<DirectoryNotFoundException>(() => Subject.Remove("serum"));
    }

    [Fact]
    public void EveryLibraryDxvkReplacesIsListed()
    {
        Assert.Equal(["d3d8", "d3d9", "d3d10core", "d3d11", "dxgi"], Dxvk.Libraries);
    }

    private void Booted(string prefix) =>
        Directory.CreateDirectory(Path.Combine(Layout.PrefixPath(prefix), "dosdevices"));

    private Dxvk Installed(string prefix)
    {
        File.WriteAllText(Layout.PrefixDxvkFile(prefix), Dxvk.Version + "\n");
        return new Dxvk(Layout, new StubRunner(new ProcessResult(0, "", "")));
    }

    private void Backed(string prefix)
    {
        Booted(prefix);
        Write(Path.Combine(Layout.PrefixSystem32(prefix), "d3d11.dll"), "dxvk");
        Write(Path.Combine(Layout.PrefixDxvkBackup(prefix, "system32"), "d3d11.dll"), "wine");
        File.WriteAllText(Layout.PrefixDxvkFile(prefix), Dxvk.Version);
    }

    private static void Write(string path, string body)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, body);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
