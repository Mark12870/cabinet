using Cabinet.Core;

namespace Cabinet.Core.Tests;

public partial class LibraryTests : IDisposable
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
        Winetricks: corefonts, vcrun2022
        Env: WINEDLLOVERRIDES=wbemprox=n
        Developer: Surge Synth Team
        Version: 1.3.4
        Licence: GPL-3.0
        Licensing:
          Free and open source, so there is no key
          and no account.
        Formats: VST3, CLAP, LV2
        Description:
          Three oscillators per scene, twelve filter types
          and a modulation matrix.

          Open sourced in 2018.
        """;

    private const string Gone =
        "INFO: No tasks are running which match the specified criteria.";

    private const string Zeros =
        "0000000000000000000000000000000000000000000000000000000000000000";

    private readonly string root = TestRoot.Create("library");

    public void Dispose() => Directory.Delete(root, recursive: true);

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
    public void AnUnknownIdIsNamed()
    {
        Catalogue(("surge-xt", SurgeXt));

        var missing = Assert.Throws<KeyNotFoundException>(() => Subject().Find("nope"));

        Assert.Equal(
            "no plugin 'nope' in the library", missing.Message);
    }

    [Fact]
    public void ARecordOfBareIdsStillReads()
    {
        var layout = Layout();
        Directory.CreateDirectory(layout.PrefixPath("gadget"));
        File.WriteAllText(layout.PrefixPluginsFile("gadget"), "gadget\n");

        Assert.Equal(
            "gadget",
            Assert.Single(new Library(layout, new UnusedRunner()).Installed()).Key);
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
        Directory.CreateDirectory(layout.PrefixPath("gadget"));
        File.WriteAllText(layout.PrefixPluginsFile("gadget"), "gadget\n\n");
        Directory.CreateDirectory(layout.NativePath("thing"));

        var installed = Subject().Installed();

        Assert.Equal("gadget", installed["gadget"]);
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
        Directory.CreateDirectory(layout.PrefixPath("gadget"));
        File.WriteAllText(layout.PrefixPluginsFile("gadget"), "gadget\n");

        var recording = new RecordingRunner();

        new Prefixes(layout, recording).Delete("gadget");

        Assert.Contains(recording.Calls, Synced);
        Assert.Empty(Subject().Installed());
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

    private string Bundles(params string[] names)
    {
        var payload = Path.Combine(root, "payload");

        foreach (var name in names)
        {
            Directory.CreateDirectory(Path.Combine(payload, name));
        }

        var archive = Path.Combine(root, "payload.tar.gz");
        Assert.True(new ProcessRunner().Run("tar", ["-czf", archive, "-C", payload, "."]).Ok);

        return archive;
    }

    private string NestedArchive()
    {
        var folder = Path.Combine(root, "nested", "Sampler-Linux");
        var module = Path.Combine(folder, "Sampler.vst3", "Contents", "x86_64-linux");
        Directory.CreateDirectory(module);
        File.WriteAllText(Path.Combine(module, "Sampler.so"), "vst3");
        File.WriteAllText(Path.Combine(folder, "Sampler.so"), "vst2");
        File.WriteAllText(Path.Combine(folder, "Sampler"), "standalone");
        File.WriteAllText(Path.Combine(folder, "readme.txt"), "readme");

        return Archive(Path.Combine(root, "nested"));
    }

    private static string Archive(string payload)
    {
        var archive = payload + ".tar.gz";
        Assert.True(new ProcessRunner().Run("tar", ["-czf", archive, "-C", payload, "."]).Ok);

        return archive;
    }

    private void Script(string name, string body) => Write(Vendor, name, body + "\n");

    private Layout Layout()
    {
        var yabridge = Path.Combine(root, "yabridge");
        Directory.CreateDirectory(yabridge);
        File.WriteAllText(Path.Combine(yabridge, "yabridgectl"), "");

        return new(
            root,
            Path.Combine(root, "runtime"),
            Path.Combine(root, "data"),
            null,
            Path.Combine(root, "library"),
            yabridge,
            Path.Combine(root, "tmp"));
    }

    private Library Subject() => new(Layout(), new RecordingRunner());

    private static void Recorded(Layout layout, LibraryEntry entry)
    {
        Directory.CreateDirectory(layout.PrefixPath(entry.Prefix));
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), entry.Id + "\n");
    }

    private static bool Synced(RecordingRunner.Call call) =>
        Path.GetFileName(call.File) == "yabridgectl"
        && call.Arguments.SequenceEqual(["sync", "--prune", "--no-verify"]);

    private static LibraryEntry Manager() =>
        LibraryEntry.Parse(
            "thing",
            "Name: Thing\nKind: windows\nSource: byo\n"
            + @"Launch: C:\Program Files\Thing\Thing.exe" + "\n");

    private static LibraryEntry Native(string id) =>
        LibraryEntry.Parse(id, $"Name: {id}\nKind: native\nSource: byo\n");

    private const string Vendor = "a-vendor";

    private const string Linked =
        "Name: Thing\nKind: windows\nSource: byo\n"
        + @"Launch: C:\Program Files\Thing\Thing.exe" + "\n"
        + "Scheme: thingmanager\n";

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
