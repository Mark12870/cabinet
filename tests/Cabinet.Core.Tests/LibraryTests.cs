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
        Developer: Surge Synth Team
        Version: 1.3.4
        Licence: GPL-3.0
        Formats: VST3, CLAP, LV2
        Description:
          Three oscillators per scene, twelve filter types
          and a modulation matrix.

          Open sourced in 2018.
        """;

    private const string Zeros =
        "0000000000000000000000000000000000000000000000000000000000000000";

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
        Assert.Null(entry.Script);
        Assert.Null(entry.Data);
        Assert.Equal("Surge Synth Team", entry.Developer);
        Assert.Equal("1.3.4", entry.Version);
        Assert.Equal("GPL-3.0", entry.Licence);
        Assert.Equal(["VST3", "CLAP", "LV2"], entry.Formats);
        Assert.Equal(
            [
                "Three oscillators per scene, twelve filter types and a modulation matrix.",
                "Open sourced in 2018.",
            ],
            entry.Description);
    }

    [Fact]
    public void AKeyAfterADescriptionEndsIt()
    {
        var entry = LibraryEntry.Parse("thing", """
            Name: Thing
            Kind: native
            Source: download
            Description:
              One.

              Two.
            Url: https://example.invalid/thing.tar.gz
            Sha256: 0000000000000000000000000000000000000000000000000000000000000000
            Category: Effect
            """);

        Assert.Equal(["One.", "Two."], entry.Description);
        Assert.Equal("Effect", entry.Category);
        Assert.Equal("https://example.invalid/thing.tar.gz", entry.Url);
    }

    [Fact]
    public void AnEntryWithNoExtrasReadsAsEmptyRatherThanNull()
    {
        var entry = LibraryEntry.Parse("serum", "Name: Serum\nKind: windows\nSource: byo\n");

        Assert.Null(entry.Developer);
        Assert.Empty(entry.Formats);
        Assert.Empty(entry.Description);
    }

    [Fact]
    public void ArtworkIsFoundBesideTheEntryOrNotAtAll()
    {
        var layout = Layout();
        var icon = Write(Vendor, "thing.png", "");

        Assert.Equal(icon, layout.LibraryIcon(Vendor, "thing"));
        Assert.Null(layout.LibraryScreenshot(Vendor, "thing"));
        Assert.Null(layout.LibraryIcon("another-vendor", "thing"));
    }

    [Fact]
    public void AVendorLogoIsFoundInItsOwnDirectory()
    {
        var layout = Layout();
        var logo = Write(Vendor, "logo.png", "");

        Assert.Equal(logo, layout.LibraryLogo(Vendor));
        Assert.Null(layout.LibraryLogo("another-vendor"));
    }

    [Fact]
    public void AnEntryKnowsWhichVendorDirectoryItCameFrom()
    {
        Catalogue(("thing", "Name: Thing\nKind: windows\nSource: byo\n"));

        Assert.Equal(Vendor, Subject().Find("thing").Vendor);
    }

    [Fact]
    public void TwoVendorsShippingTheSameIdIsRefused()
    {
        var entry = "Name: Thing\nKind: windows\nSource: byo\n";
        Write("one-vendor", "thing.yml", entry);
        Write("other-vendor", "thing.yml", entry);

        Assert.Contains(
            "two vendors both ship thing.yml",
            Assert.Throws<InvalidOperationException>(() => Subject().Entries()).Message);
    }

    [Theory]
    [InlineData("../../evil.sh")]
    [InlineData("scripts/u-he.sh")]
    [InlineData("u-he")]
    public void AScriptThatIsNotAShippedFilenameIsRefused(string script)
    {
        var thrown = Assert.Throws<InvalidOperationException>(
            () => LibraryEntry.Parse(
                "thing", $"Name: Thing\nKind: windows\nSource: byo\nScript: {script}\n"));

        Assert.Contains(script, thrown.Message);
    }

    [Theory]
    [InlineData("/etc")]
    [InlineData(".u-he")]
    [InlineData(".u-he/../../elsewhere")]
    [InlineData(".vst3/Podolski")]
    public void ADataDirectoryOutsideThePluginsOwnIsRefused(string data)
    {
        Assert.Throws<InvalidOperationException>(
            () => LibraryEntry.Parse(
                "thing",
                $"Name: Thing\nKind: native\nUrl: file:///none\nSha256: {Zeros}\nData: {data}\n"));
    }

    [Fact]
    public void AWindowsPluginIsRefusedADataDirectoryBecauseItsPrefixIsOne()
    {
        Assert.Throws<InvalidOperationException>(
            () => LibraryEntry.Parse(
                "thing", "Name: Thing\nKind: windows\nSource: byo\nData: .u-he/Thing\n"));
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
    public void ANativeEntryNobodyCanDownloadIsRefused()
    {
        var refused = Assert.Throws<InvalidOperationException>(
            () => LibraryEntry.Parse("vital", "Name: Vital\nKind: native\nSource: byo\n"));

        Assert.Contains("is native and byo", refused.Message);
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
            ("zeta", "Name: Alpha\nKind: windows\nSource: byo\n"),
            ("alpha", "Name: Zeta\nKind: windows\nSource: byo\n"));

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
        Catalogue(("dexed", $"""
            Name: Dexed
            Kind: native
            Url: https://example.invalid/dexed.zip
            Sha256: {Zeros}
            """));

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
        Assert.Equal("dexed", library.Installed()["dexed"]);
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
        Assert.Empty(library.Installed());
    }

    [Fact]
    public void NothingIsInstalledUntilSomethingIsUnpacked()
    {
        Assert.Empty(Subject().Installed());
    }

    [Fact]
    public void AWindowsPluginIsInstalledWhereItsPrefixSaysSo()
    {
        var layout = Layout();
        Directory.CreateDirectory(layout.PrefixPath("serum"));
        File.WriteAllText(layout.PrefixPluginsFile("serum"), "serum\n\n");
        Directory.CreateDirectory(layout.NativePath("thing"));

        var installed = Subject().Installed();

        Assert.Equal("serum", installed["serum"]);
        Assert.Null(installed["thing"]);
        Assert.Equal(2, installed.Count);
    }

    [Fact]
    public void APrefixWithNoRecordHoldsNoPlugin()
    {
        Directory.CreateDirectory(Layout().PrefixPath("empty"));

        Assert.Empty(Subject().Installed());
    }

    [Fact]
    public void DeletingAPrefixTakesWhatItHeldWithIt()
    {
        var layout = Layout();
        Directory.CreateDirectory(layout.PrefixPath("serum"));
        File.WriteAllText(layout.PrefixPluginsFile("serum"), "serum\n");

        new Prefixes(layout, new RecordingRunner()).Delete("serum");

        Assert.Empty(Subject().Installed());
    }

    [Fact]
    public void AScriptReplacesTheUnpackAndFillsTheDirectoriesCabinetMade()
    {
        var archive = Fixture();

        Catalogue(("thing", $"""
            Name: Thing
            Kind: native
            Url: file://{archive}
            Sha256: {Checksum.Sha256(archive)}
            Script: fixture.sh
            Data: .thing/Thing
            """));

        Script("fixture.sh", """
            test "$(pwd)" = "$CABINET_DEST"
            tar -xf "$CABINET_ARCHIVE" -C "$CABINET_WORK"
            mkdir -p "$CABINET_DEST/Thing.vst3"
            cp "$CABINET_WORK/nested/deep/Thing.so" "$CABINET_DEST/Thing.vst3/Thing.so"
            cp "$CABINET_WORK/nested/deep/presets.txt" "$CABINET_DATA/presets.txt"
            """);

        var layout = Layout();
        var library = new Library(layout, new ProcessRunner());
        library.Install(library.Find("thing"));

        var data = Path.Combine(root, ".thing", "Thing");

        Assert.True(Path.Exists(Path.Combine(layout.ScanDir(".vst3"), "Thing.vst3")));
        Assert.True(File.Exists(Path.Combine(data, "presets.txt")));
        Assert.Equal("thing", Assert.Single(library.Installed()).Key);

        library.RemoveNative("thing");

        Assert.False(Path.Exists(Path.Combine(layout.ScanDir(".vst3"), "Thing.vst3")));
        Assert.False(Directory.Exists(data));
        Assert.False(Directory.Exists(layout.NativePath("thing")));
    }

    [Fact]
    public void AScriptThatFailsLeavesNeitherDirectoryBehind()
    {
        var archive = Fixture();

        Catalogue(("thing", $"""
            Name: Thing
            Kind: native
            Url: file://{archive}
            Sha256: {Checksum.Sha256(archive)}
            Script: fixture.sh
            Data: .thing/Thing
            """));

        Script("fixture.sh", "exit 3");

        var layout = Layout();
        var library = new Library(layout, new ProcessRunner());

        var thrown = Assert.Throws<InvalidOperationException>(
            () => library.Install(library.Find("thing")));

        Assert.Contains("fixture.sh exited with 3", thrown.Message);
        Assert.False(Directory.Exists(layout.NativePath("thing")));
        Assert.False(Directory.Exists(Path.Combine(root, ".thing", "Thing")));
        Assert.Empty(library.Installed());
    }

    [Fact]
    public void AScriptThisBuildDidNotShipSaysSoRatherThanFailingInsideSh()
    {
        var archive = Fixture();

        Catalogue(("thing", $"""
            Name: Thing
            Kind: native
            Url: file://{archive}
            Sha256: {Checksum.Sha256(archive)}
            Script: absent.sh
            """));

        var library = new Library(Layout(), new ProcessRunner());

        Assert.Contains(
            "absent.sh",
            Assert.Throws<FileNotFoundException>(() => library.Install(library.Find("thing")))
                .Message);
    }

    [Fact]
    public void ADataDirectorySomethingElseOwnsIsLeftAlone()
    {
        var archive = Fixture();

        Catalogue(("thing", $"""
            Name: Thing
            Kind: native
            Url: file://{archive}
            Sha256: {Checksum.Sha256(archive)}
            Script: fixture.sh
            Data: .thing/Thing
            """));

        Script("fixture.sh", "exit 0");
        var data = Directory.CreateDirectory(Path.Combine(root, ".thing", "Thing")).FullName;
        File.WriteAllText(Path.Combine(data, "mine.txt"), "");

        var library = new Library(Layout(), new UnusedRunner());

        Assert.Throws<InvalidOperationException>(() => library.Install(library.Find("thing")));
        Assert.True(File.Exists(Path.Combine(data, "mine.txt")));
    }

    [Fact]
    public void AWindowsScriptIsHandedThePrefixAndItsOwnWine()
    {
        Catalogue(("thing", """
            Name: Thing
            Kind: windows
            Source: byo
            Prefix: thing
            Script: fixture.sh
            """));

        Script("fixture.sh", "exit 0");

        var layout = Layout();
        var recording = new RecordingRunner();
        var installer = Path.Combine(root, "thing-setup.exe");
        File.WriteAllText(installer, "");

        var library = new Library(layout, recording);

        Assert.Contains(
            "yabridgectl",
            Assert.Throws<InvalidOperationException>(
                () => library.Install(library.Find("thing"), installer: installer)).Message);

        var script = Assert.Single(recording.Calls, call => call.File == "sh");

        Assert.Equal(["-e", layout.LibraryScript(Vendor, "fixture.sh")], script.Arguments);
        Assert.Equal(layout.PrefixPath("thing"), script.WorkingDirectory);
        Assert.Equal(layout.PrefixPath("thing"), script.Environment["WINEPREFIX"]);
        Assert.Equal(installer, script.Environment["CABINET_ARCHIVE"]);
        Assert.Contains("WINE", script.Environment.Keys);
        Assert.DoesNotContain(recording.Calls, call => call.Arguments.Contains(installer)
            && call.File != "sh");
    }

    private string Fixture()
    {
        var staging = Path.Combine(root, "fixture", "nested", "deep");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "Thing.so"), "");
        File.WriteAllText(Path.Combine(staging, "presets.txt"), "");

        var archive = Path.Combine(root, "thing.tar.gz");
        Assert.True(new ProcessRunner()
            .Run("tar", ["-czf", archive, "-C", Path.Combine(root, "fixture"), "."]).Ok);

        return archive;
    }

    private void Script(string name, string body) => Write(Vendor, name, body + "\n");

    private Layout Layout() =>
        new(root, "/run/user/1000", Path.Combine(root, "data"), null, Path.Combine(root, "library"));

    private Library Subject() => new(Layout(), new RecordingRunner());

    private const string Vendor = "a-vendor";

    private void Catalogue(params (string Id, string Text)[] entries)
    {
        foreach (var (id, text) in entries)
        {
            Write(Vendor, id + ".yml", text);
        }
    }

    private string Write(string vendor, string name, string text)
    {
        var directory = Path.Combine(root, "library", vendor);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, name);
        File.WriteAllText(path, text);

        return path;
    }
}
