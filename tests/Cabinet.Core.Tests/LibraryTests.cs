using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class LibraryTests : IDisposable
{
    private const string SurgeXt = """
        Name: Surge XT
        Kind: windows
        Category: Synth
        Summary: Hybrid synthesizer, free and open source.
        Homepage: https://surge-synthesizer.github.io
        Source: download
        Url: https://example.invalid/surge-xt-setup.exe
        Sha256: 6e221e05f29254508142b9e0ed76a85f22fa1b512501bebde571951e7eefecca
        Prefix: surge
        Runner: 9.21
        Dxvk: true
        Sync: fsync
        """;

    private readonly string root = Directory.CreateTempSubdirectory("cabinet").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void EveryFieldOfAnEntryIsRead()
    {
        var entry = LibraryEntry.Parse("surge-xt", SurgeXt);

        Assert.Equal("surge-xt", entry.Id);
        Assert.Equal("Surge XT", entry.Name);
        Assert.Equal(PluginKind.Windows, entry.Kind);
        Assert.Equal("Synth", entry.Category);
        Assert.Equal("https://surge-synthesizer.github.io", entry.Homepage);
        Assert.Equal(PluginSource.Download, entry.Source);
        Assert.Equal("https://example.invalid/surge-xt-setup.exe", entry.Url);
        Assert.Equal("surge", entry.Prefix);
        Assert.Equal("9.21", entry.Runner);
        Assert.True(entry.Dxvk);
        Assert.Equal(SyncMode.Fsync, entry.Sync);
    }

    [Fact]
    public void AnEntryWithNoPrefixIsNamedAfterItsFile()
    {
        var entry = LibraryEntry.Parse("serum", "Name: Serum\nKind: windows\nSource: byo\n");

        Assert.Equal("serum", entry.Prefix);
        Assert.Equal(SyncMode.System, entry.Sync);
        Assert.False(entry.Dxvk);
        Assert.Null(entry.Runner);
    }

    [Fact]
    public void AKindThatIsNeitherWindowsNorNativeIsRefused()
    {
        var refused = Assert.Throws<InvalidOperationException>(
            () => LibraryEntry.Parse("odd", "Name: Odd\nKind: macos\n"));

        Assert.Equal("odd.yml has Kind: macos — windows or native", refused.Message);
    }

    [Fact]
    public void ADownloadWithNoChecksumIsRefused()
    {
        var refused = Assert.Throws<InvalidOperationException>(
            () => LibraryEntry.Parse(
                "loose", "Name: Loose\nKind: native\nUrl: https://example.invalid/x.zip\n"));

        Assert.Contains("has no Sha256", refused.Message);
    }

    [Fact]
    public void ANativeEntryCarryingAPrefixIsRefused()
    {
        var refused = Assert.Throws<InvalidOperationException>(
            () => LibraryEntry.Parse("vital", "Name: Vital\nKind: native\nSource: byo\nPrefix: v\n"));

        Assert.Contains("is native and carries Prefix", refused.Message);
    }

    [Fact]
    public void TheListIsOrderedByNameAndIgnoresWhatIsNotAnEntry()
    {
        Catalogue(
            ("zeta", "Name: Alpha\nKind: native\nSource: byo\n"),
            ("alpha", "Name: Zeta\nKind: native\nSource: byo\n"));

        File.WriteAllText(Path.Combine(root, "library", "notes.txt"), "not an entry");

        Assert.Equal(["Alpha", "Zeta"], Subject().Entries().Select(entry => entry.Name));
    }

    [Fact]
    public void ReadingTheLibraryRunsNoProcess()
    {
        Catalogue(("surge-xt", SurgeXt));

        Assert.Single(new Library(Layout(), new UnusedRunner()).Entries());
    }

    [Fact]
    public void AnUnknownIdSaysHowToSeeTheOnesThereAre()
    {
        Catalogue(("surge-xt", SurgeXt));

        var missing = Assert.Throws<InvalidOperationException>(() => Subject().Find("nope"));

        Assert.Equal(
            "no plugin 'nope' in the library — `cabinet library` lists what there is",
            missing.Message);
    }

    [Fact]
    public void APluginYouHadToBuySaysWhichInstallerToPass()
    {
        Catalogue(("serum", "Name: Serum\nKind: windows\nSource: byo\n"));

        var refused = Assert.Throws<InvalidOperationException>(
            () => Subject().Install(Subject().Find("serum")));

        Assert.Equal(
            "Serum cannot be downloaded — pass the installer you already have: "
            + "`cabinet library install serum serum <installer.exe>`",
            refused.Message);
    }

    [Fact]
    public void ALinuxPluginIsRefusedAPrefixBecauseItLoadsWithoutOne()
    {
        Catalogue(("dexed", "Name: Dexed\nKind: native\nSource: byo\n"));

        var refused = Assert.Throws<ArgumentException>(
            () => Subject().Install(Subject().Find("dexed"), "somewhere"));

        Assert.Contains("needs no prefix", refused.Message);
    }

    [Fact]
    public void AWindowsPluginIsBootedInstalledAndOnlyThenBridged()
    {
        Catalogue(("dexed", "Name: Dexed\nKind: windows\nSource: byo\n"));

        var installer = Path.Combine(root, "Dexed.exe");
        File.WriteAllText(installer, "");

        var recording = new RecordingRunner();
        var library = new Library(Layout(), recording);

        var stopped = Assert.Throws<InvalidOperationException>(
            () => library.Install(library.Find("dexed"), null, installer));

        Assert.Equal(
            ["wineboot", "wine"],
            recording.Calls.Select(call => Path.GetFileName(call.File)));
        Assert.Equal([installer], recording.Calls[1].Arguments);
        Assert.Contains("yabridgectl", stopped.Message);
    }

    [Fact]
    public void AnExistingPrefixKeepsItsRunnerAndFetchesNothing()
    {
        Catalogue(("dexed", "Name: Dexed\nKind: windows\nSource: byo\nRunner: 9.21\n"));

        var layout = Layout();
        Directory.CreateDirectory(Path.Combine(layout.PrefixPath("dexed"), "dosdevices"));

        var installer = Path.Combine(root, "Dexed.exe");
        File.WriteAllText(installer, "");

        var recording = new RecordingRunner();
        var library = new Library(layout, recording);
        var said = new List<string>();

        Assert.Throws<InvalidOperationException>(
            () => library.Install(library.Find("dexed"), null, installer, said.Add));

        Assert.DoesNotContain(recording.Calls, call => call.File == "curl");
        Assert.Contains(said, line => line.Contains("keeps bundled"));
    }

    [Fact]
    public void RemovingAWindowsPluginPointsAtDeleteInstead()
    {
        Catalogue(("serum", "Name: Serum\nKind: windows\nSource: byo\n"));

        var refused = Assert.Throws<InvalidOperationException>(() => Subject().RemoveNative("serum"));

        Assert.Equal(
            "Serum runs under Wine, so it lives in a prefix — "
            + "`cabinet delete serum` is what removes it",
            refused.Message);
    }

    [Fact]
    public void AnIdThatWalksOutOfTheNativeDirectoryIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Subject().RemoveNative("../runners"));
    }

    [Fact]
    public void RemovalTakesOnlyTheLinksThatPointIntoThePluginsOwnDirectory()
    {
        var layout = Layout();
        var installed = layout.NativePath("dexed");
        Directory.CreateDirectory(installed);

        var ours = Path.Combine(installed, "Dexed.clap");
        File.WriteAllText(ours, "");

        var elsewhere = Path.Combine(root, "Someone.clap");
        File.WriteAllText(elsewhere, "");

        var scan = layout.ScanDir(".clap");
        Directory.CreateDirectory(scan);
        File.CreateSymbolicLink(Path.Combine(scan, "Dexed.clap"), ours);
        File.CreateSymbolicLink(Path.Combine(scan, "Someone.clap"), elsewhere);

        Subject().RemoveNative("dexed");

        Assert.False(Directory.Exists(installed));
        Assert.False(Path.Exists(Path.Combine(scan, "Dexed.clap")));
        Assert.True(Path.Exists(Path.Combine(scan, "Someone.clap")));
    }

    [Fact]
    public void EveryBundleIsLinkedByFormatAndNoBundleIsWalkedInto()
    {
        var staging = Path.Combine(root, "fixture");
        Directory.CreateDirectory(Path.Combine(staging, "Thing.lv2"));
        File.WriteAllText(Path.Combine(staging, "Thing.lv2", "libThing.so"), "");
        File.WriteAllText(Path.Combine(staging, "Thing.lv2", "manifest.ttl"), "");
        Directory.CreateDirectory(Path.Combine(staging, "Thing.vst3"));
        File.WriteAllText(Path.Combine(staging, "Thing.clap"), "");
        File.WriteAllText(Path.Combine(staging, "README"), "");

        var archive = Path.Combine(root, "thing.tar.gz");
        var real = new ProcessRunner();
        Assert.True(real.Run("tar", ["-czf", archive, "-C", staging, "."]).Ok);

        Catalogue(("thing", $"""
            Name: Thing
            Kind: native
            Url: file://{archive}
            Sha256: {Checksum.Sha256(archive)}
            """));

        var layout = Layout();
        var library = new Library(layout, real);
        library.Install(library.Find("thing"));

        Assert.True(Path.Exists(Path.Combine(layout.ScanDir(".lv2"), "Thing.lv2")));
        Assert.True(Path.Exists(Path.Combine(layout.ScanDir(".vst3"), "Thing.vst3")));
        Assert.True(Path.Exists(Path.Combine(layout.ScanDir(".clap"), "Thing.clap")));
        Assert.False(Directory.Exists(layout.ScanDir(".so")));

        library.RemoveNative("thing");

        Assert.False(Path.Exists(Path.Combine(layout.ScanDir(".lv2"), "Thing.lv2")));
        Assert.Empty(library.InstalledNative());
    }

    [Fact]
    public void NothingIsInstalledUntilSomethingIsUnpacked()
    {
        Assert.Empty(Subject().InstalledNative());
    }

    private Layout Layout() =>
        new(root, "/run/user/1000", Path.Combine(root, "data"), null, Path.Combine(root, "library"));

    private Library Subject() => new(Layout(), new RecordingRunner());

    private void Catalogue(params (string Id, string Text)[] entries)
    {
        var directory = Path.Combine(root, "library");
        Directory.CreateDirectory(directory);

        foreach (var (id, text) in entries)
        {
            File.WriteAllText(Path.Combine(directory, id + ".yml"), text);
        }
    }
}
