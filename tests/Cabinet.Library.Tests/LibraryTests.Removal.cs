using Cabinet.Core;

namespace Cabinet.Core.Tests;

public partial class LibraryTests
{
    [Fact]
    public void ADawsPluginsKeepAnUninstallOutOfTheirPrefix()
    {
        Catalogue(("thing", """
            Name: Thing
            Kind: windows
            Source: byo
            """));

        var layout = Layout();
        var recorder = new RecordingRunner();
        Directory.CreateDirectory(layout.PrefixPath("thing"));
        File.WriteAllText(layout.PrefixPluginsFile("thing"), "thing\n");
        using var plugin = SessionFiles.HeldByAPlugin(SessionFiles.Of(layout, "thing").Busy);

        var library = new Library(layout, recorder);
        var refused = Assert.Throws<PrefixInUseException>(
            () => library.Remove(library.RemovalOf(library.Find("thing"))));

        Assert.Contains("take Thing out of thing", refused.Message);
        Assert.Empty(recorder.Ran);
    }

    [Fact]
    public void AnIdThatWalksOutOfTheNativeDirectoryIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Subject().RemovalOf(Native("../runners")));
    }

    [Fact]
    public void ANativePluginIsRemovedWithNoPrefixToDecideAbout()
    {
        var layout = Layout();
        Directory.CreateDirectory(layout.NativePath("synth"));

        var removal = new Library(layout, new UnusedRunner()).RemovalOf(Native("synth"));

        Assert.Equal(RemovalKind.Native, removal.Kind);
        Assert.Null(removal.Prefix);
    }

    [Fact]
    public void AManagerTakesItsPrefixAndNamesEveryPluginThatGoesWithIt()
    {
        var layout = Layout();
        var entry = Manager();
        Directory.CreateDirectory(layout.PrefixPath(entry.Prefix));
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), "thing\nother\n");

        var removal = new Library(layout, new UnusedRunner()).RemovalOf(entry);

        Assert.Equal(RemovalKind.TakesPrefix, removal.Kind);
        Assert.Equal(["other"], removal.Sharing);
    }

    [Fact]
    public void TheOnlyPluginInAPrefixOffersToTakeThePrefixToo()
    {
        var layout = Layout();
        var entry = LibraryEntry.Parse("thing", "Name: Thing\nKind: windows\nSource: byo\n");
        Directory.CreateDirectory(layout.PrefixPath(entry.Prefix));
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), "thing\n");

        var removal = new Library(layout, new UnusedRunner()).RemovalOf(entry);

        Assert.Equal(RemovalKind.PluginOrPrefix, removal.Kind);
        Assert.Equal(entry.Prefix, removal.Prefix);
        Assert.Empty(removal.Sharing);
    }

    [Fact]
    public void APluginSharingItsPrefixKeepsItAndNamesWhoElseIsThere()
    {
        var layout = Layout();
        var entry = LibraryEntry.Parse("thing", "Name: Thing\nKind: windows\nSource: byo\n");
        Directory.CreateDirectory(layout.PrefixPath(entry.Prefix));
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), "thing\nother\nanother\n");

        var removal = new Library(layout, new UnusedRunner()).RemovalOf(entry);

        Assert.Equal(RemovalKind.KeepsPrefix, removal.Kind);
        Assert.Equal(["other", "another"], removal.Sharing);
    }

    [Fact]
    public void RemovalActsOnThePrefixThePluginIsRecordedInNotItsCatalogueDefault()
    {
        var layout = Layout();
        var entry = LibraryEntry.Parse("thing", "Name: Thing\nKind: windows\nSource: byo\n");
        Directory.CreateDirectory(layout.PrefixPath("thing"));
        File.WriteAllText(layout.PrefixPluginsFile("thing"), "someone-else\n");
        Directory.CreateDirectory(layout.PrefixPath("elsewhere"));
        File.WriteAllText(layout.PrefixPluginsFile("elsewhere"), "thing\n");
        var library = new Library(layout, new RecordingRunner());

        library.Remove(library.RemovalOf(entry), takePrefix: true);

        Assert.True(Directory.Exists(layout.PrefixPath("thing")));
        Assert.False(Directory.Exists(layout.PrefixPath("elsewhere")));
    }

    [Fact]
    public void APrefixIsNotDeletedForAPluginThatIsNotRecordedInIt()
    {
        var layout = Layout();
        var entry = LibraryEntry.Parse("thing", "Name: Thing\nKind: windows\nSource: byo\n");
        Directory.CreateDirectory(layout.PrefixPath("thing"));
        File.WriteAllText(layout.PrefixPluginsFile("thing"), "someone-else\n");
        var library = new Library(layout, new UnusedRunner());

        Assert.Equal(
            "Thing is not installed",
            Assert.Throws<KeyNotFoundException>(() => library.RemovalOf(entry)).Message);
        Assert.True(Directory.Exists(layout.PrefixPath("thing")));
    }

    [Fact]
    public void ARemovalAgreedBeforeTheOtherPluginsChangedIsRefused()
    {
        var layout = Layout();
        var entry = LibraryEntry.Parse("thing", "Name: Thing\nKind: windows\nSource: byo\n");
        Directory.CreateDirectory(layout.PrefixPath("thing"));
        File.WriteAllText(layout.PrefixPluginsFile("thing"), "thing\n");
        var library = new Library(layout, new RecordingRunner());
        var agreed = library.RemovalOf(entry);

        File.WriteAllText(layout.PrefixPluginsFile("thing"), "thing\narrived\n");

        Assert.Contains(
            "changed since you were asked",
            Assert.Throws<InvalidOperationException>(
                () => library.Remove(agreed, takePrefix: true)).Message);
        Assert.True(Directory.Exists(layout.PrefixPath("thing")));
    }

    [Fact]
    public void AManagerIsNeverTakenOutByItsOwnUninstaller()
    {
        var layout = Layout();
        var entry = Manager();
        Directory.CreateDirectory(layout.PrefixPath(entry.Prefix));
        File.WriteAllText(layout.PrefixPluginsFile(entry.Prefix), "thing\n");
        var library = new Library(layout, new RecordingRunner());

        Assert.Contains(
            "goes only with its prefix",
            Assert.Throws<InvalidOperationException>(
                () => library.Remove(library.RemovalOf(entry))).Message);
    }

    [Fact]
    public void APrefixOtherPluginsShareIsNotDeletedToRemoveOne()
    {
        var layout = Layout();
        var entry = LibraryEntry.Parse("thing", "Name: Thing\nKind: windows\nSource: byo\n");
        Directory.CreateDirectory(layout.PrefixPath("thing"));
        File.WriteAllText(layout.PrefixPluginsFile("thing"), "thing\nother\n");
        var library = new Library(layout, new RecordingRunner());

        Assert.Contains(
            "thing also holds other",
            Assert.Throws<InvalidOperationException>(
                () => library.Remove(library.RemovalOf(entry), takePrefix: true)).Message);
        Assert.True(Directory.Exists(layout.PrefixPath("thing")));
    }

    [Fact]
    public void APrefixKnowsWhichOtherPluginsWouldKeepItAlive()
    {
        var layout = Layout();
        Directory.CreateDirectory(layout.PrefixPath("valhalla"));
        File.WriteAllText(
            layout.PrefixPluginsFile("valhalla"),
            "valhalla-supermassive\nvalhalla-freq-echo\n");

        var library = new Library(layout, new UnusedRunner());

        Assert.Equal(["valhalla-freq-echo"], library.Sharing("valhalla", "valhalla-supermassive"));
        Assert.Empty(library.Sharing("gadget", "gadget"));
    }

    [Fact]
    public void AnUninstallerNobodyCanFindIsSaidToBeMissingAndNothingElse()
    {
        Catalogue(("aalto", "Name: Aalto\nKind: windows\nSource: byo\n"));

        var layout = Layout();
        Directory.CreateDirectory(layout.PrefixPath("aalto"));
        File.WriteAllText(layout.PrefixPluginsFile("aalto"), "aalto\n");
        File.WriteAllText(layout.PrefixSystemReg("aalto"), """
            WINE REGISTRY Version 2

            [Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Mono] 1787344290
            "DisplayName"="Wine Mono Runtime"
            "UninstallString"="C:\\mono.exe"
            """);

        var library = new Library(layout, new UnusedRunner());
        var refused = Assert.Throws<InvalidOperationException>(
            () => library.Remove(library.RemovalOf(library.Find("aalto"))));

        Assert.Equal(Library.NotFound(library.Find("aalto"), "aalto"), refused.Message);
        Assert.Equal("aalto", Assert.Single(library.Installed()).Key);
    }

    [Fact]
    public void OnlyUninstallersThatCouldBeThePluginsAreOffered()
    {
        Catalogue(("aalto", "Name: Aalto\nKind: windows\nSource: byo\n"));

        var layout = Layout();
        Directory.CreateDirectory(layout.PrefixPath("aalto"));
        File.WriteAllText(layout.PrefixPluginsFile("aalto"), "aalto\nkaivo\tHKLM\\K\\Kaivo\n");
        File.WriteAllText(layout.PrefixSystemReg("aalto"), """
            WINE REGISTRY Version 2

            [Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Aalto] 1787344290
            "DisplayName"="Aalto version 1.9.4"
            "UninstallString"="C:\\aalto.exe"

            [Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Mono] 1787344290
            "DisplayName"="Wine Mono Runtime"
            "UninstallString"="C:\\mono.exe"

            [Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Support] 1787344290
            "DisplayName"="Wine Mono Windows Support"
            "UninstallString"="C:\\support.exe"
            """);

        var script = Path.Combine(layout.PrefixPath("aalto"), "drive_c", "cabinet-uninstall.bat");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);

        var ran = "";
        var recording = new RecordingRunner(_ => ran = File.ReadAllText(script));
        var library = new Library(layout, recording);

        Assert.Equal(3, library.Uninstallers("aalto").Count);
        Assert.Throws<InvalidOperationException>(
            () => library.Remove(library.RemovalOf(library.Find("aalto"))));

        Assert.Equal(
            [["cmd", "/c", @"C:\cabinet-uninstall.bat"]],
            recording.Ran.Select(call => call.Arguments));
        Assert.Equal("C:\\aalto.exe\r\n", ran);
        Assert.False(File.Exists(script));
    }

    private const string Madrona = """
        WINE REGISTRY Version 2

        [Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Aalto] 1787344290
        "DisplayName"="Aalto version 1.9.4"
        "QuietUninstallString"="C:\\aalto.exe"

        [Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Aaltoverb] 1787344290
        "DisplayName"="Aaltoverb version 1.9.4"
        "QuietUninstallString"="C:\\aaltoverb.exe"

        [Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Runtime] 1787344290
        "DisplayName"="Microsoft Visual C++ 2022 Redistributable"
        "QuietUninstallString"="C:\\vcredist.exe"
        """;

    [Fact]
    public void RemovingAPluginNeverRunsTheUninstallerOfANeighbourWhoseNameContainsIt()
    {
        Catalogue(("aalto", "Name: Aalto\nKind: windows\nSource: byo\nPrefix: madrona\n"));

        var layout = Layout();
        Directory.CreateDirectory(Path.Combine(layout.PrefixPath("madrona"), "drive_c"));
        File.WriteAllText(layout.PrefixPluginsFile("madrona"), "aalto\naaltoverb\n");
        File.WriteAllText(layout.PrefixSystemReg("madrona"), Madrona);
        var recording = new RecordingRunner();
        var library = new Library(layout, recording);
        var removal = library.RemovalOf(library.Find("aalto"));

        Assert.Equal("Aalto version 1.9.4", Assert.Single(library.PossibleUninstallers(removal)).Name);
        Assert.Throws<InvalidOperationException>(() => library.Remove(removal));

        Assert.Single(recording.Ran);
        Assert.Contains("aaltoverb", library.Recorded("madrona"));
    }

    [Theory]
    [InlineData("Valhalla Supermassive", "ValhallaSupermassive version 5.0.0", 1)]
    [InlineData("Serum 2", "Serum 2 x64", 1)]
    [InlineData("Sitala 1", "Sitala 10.2", 0)]
    [InlineData("Aalto", "Aaltoverb version 1.9.4", 0)]
    public void AnUninstallerMatchesAPluginOnWholeWords(
        string name, string uninstaller, int matches)
    {
        Catalogue(("thing", $"Name: {name}\nKind: windows\nSource: byo\n"));

        var layout = Layout();
        Directory.CreateDirectory(layout.PrefixPath("thing"));
        File.WriteAllText(layout.PrefixPluginsFile("thing"), "thing\n");
        File.WriteAllText(layout.PrefixSystemReg("thing"), $$"""
            WINE REGISTRY Version 2

            [Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\One] 1787344290
            "DisplayName"="{{uninstaller}}"
            "UninstallString"="C:\\one.exe"
            """);
        var library = new Library(layout, new UnusedRunner());

        Assert.Equal(
            matches,
            library.PossibleUninstallers(library.RemovalOf(library.Find("thing"))).Count);
    }

    [Fact]
    public void UninstallersThatCouldEachBeThePluginsAreNotGuessedBetween()
    {
        Catalogue(("aalto", "Name: Aalto\nKind: windows\nSource: byo\nPrefix: madrona\n"));

        var layout = Layout();
        Directory.CreateDirectory(Path.Combine(layout.PrefixPath("madrona"), "drive_c"));
        File.WriteAllText(layout.PrefixPluginsFile("madrona"), "aalto\n");
        File.WriteAllText(layout.PrefixSystemReg("madrona"), Madrona.Replace(
            "Aaltoverb version 1.9.4", "Aalto 2 version 2.0.0"));
        var recording = new RecordingRunner();
        var library = new Library(layout, recording);
        var removal = library.RemovalOf(library.Find("aalto"));
        var possible = library.PossibleUninstallers(removal);

        Assert.Equal(2, possible.Count);
        Assert.Contains(
            "so Cabinet will not guess",
            Assert.Throws<InvalidOperationException>(() => library.Remove(removal)).Message);
        Assert.Empty(recording.Ran);

        Assert.Throws<InvalidOperationException>(
            () => library.Remove(removal, uninstaller: possible[1]));

        Assert.Equal(
            ["cmd", "/c", @"C:\cabinet-uninstall.bat"], Assert.Single(recording.Ran).Arguments);
    }

    [Fact]
    public void AnUninstallerThatCouldNotBeThePluginsIsRefusedEvenWhenChosen()
    {
        Catalogue(("aalto", "Name: Aalto\nKind: windows\nSource: byo\nPrefix: madrona\n"));

        var layout = Layout();
        Directory.CreateDirectory(layout.PrefixPath("madrona"));
        File.WriteAllText(layout.PrefixPluginsFile("madrona"), "aalto\n");
        File.WriteAllText(layout.PrefixSystemReg("madrona"), Madrona);
        var recording = new RecordingRunner();
        var library = new Library(layout, recording);
        var runtime = library.Uninstallers("madrona").Single(one => one.Name.StartsWith("Microsoft"));

        Assert.Contains(
            "is not an uninstaller that could be Aalto's",
            Assert.Throws<InvalidOperationException>(() => library.Remove(
                library.RemovalOf(library.Find("aalto")), uninstaller: runtime)).Message);
        Assert.Empty(recording.Ran);
    }

    [Fact]
    public void AnUninstallerAlreadyAttributedToAnotherPluginIsNotOffered()
    {
        Catalogue(("aalto", "Name: Aalto\nKind: windows\nSource: byo\n"));

        var layout = Layout();
        var key = @"HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\Kaivo";

        Directory.CreateDirectory(layout.PrefixPath("aalto"));
        File.WriteAllText(layout.PrefixPluginsFile("aalto"), $"aalto\nkaivo\t{key}\n");
        File.WriteAllText(layout.PrefixSystemReg("aalto"), """
            WINE REGISTRY Version 2

            [Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Kaivo] 1787344290
            "DisplayName"="Kaivo version 1.9.4"
            "UninstallString"="C:\\kaivo.exe"
            """);

        var library = new Library(layout, new UnusedRunner());
        var entry = library.Find("aalto");

        Assert.Equal(
            Library.NotFound(entry, "aalto"),
            Assert.Throws<InvalidOperationException>(() => library.Remove(library.RemovalOf(entry))).Message);
    }

    [Fact]
    public void AnUninstallerThatRemovedNothingLeavesTheRecordAlone()
    {
        Catalogue(("gadget", "Name: Gadget\nKind: windows\nSource: byo\n"));

        var layout = Layout();
        var key = @"HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\Gadget2";

        Directory.CreateDirectory(layout.PrefixVst3Dir("gadget"));
        File.WriteAllText(Path.Combine(layout.PrefixVst3Dir("gadget"), "Gadget2.vst3"), "");
        File.WriteAllText(layout.PrefixPluginsFile("gadget"), $"gadget\t{key}\n");
        File.WriteAllText(layout.PrefixSystemReg("gadget"), """
            WINE REGISTRY Version 2

            [Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Gadget2] 1787344290
            "DisplayName"="Xfer Records Gadget 2"
            "UninstallString"="C:\\Uninstall_Gadget2.exe"
            """);

        var recording = new RecordingRunner();
        var library = new Library(layout, recording);
        var refused = Assert.Throws<InvalidOperationException>(
            () => library.Remove(library.RemovalOf(library.Find("gadget"))));

        Assert.Contains("nothing has been removed", refused.Message);
        Assert.Equal(
            ["cmd", "/c", @"C:\cabinet-uninstall.bat"], recording.Ran.Single().Arguments);
        Assert.Equal("gadget", Assert.Single(library.Installed()).Key);
    }

    [Fact]
    public void RemovingAWindowsPluginTakesItsRecordAndLeavesTheOtherOnes()
    {
        Catalogue(("supermassive", "Name: Supermassive\nKind: windows\nPrefix: valhalla\n"
                                   + "Source: byo\n"));

        var layout = Layout();
        var vst3 = layout.PrefixVst3Dir("valhalla");
        Directory.CreateDirectory(vst3);
        File.WriteAllText(Path.Combine(vst3, "ValhallaSupermassive.vst3"), "");
        File.WriteAllText(Path.Combine(vst3, "ValhallaFreqEcho.vst3"), "");

        var key = @"HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\Super";
        File.WriteAllText(
            layout.PrefixPluginsFile("valhalla"), $"supermassive\t{key}\nfreq-echo\n");
        File.WriteAllText(layout.PrefixSystemReg("valhalla"), """
            WINE REGISTRY Version 2

            [Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Super] 1787390242
            "DisplayName"="ValhallaSupermassive version 5.0.0"
            "QuietUninstallString"="\"C:\\unins000.exe\" /SILENT"
            "UninstallString"="\"C:\\unins000.exe\""
            """);

        var recording = new RecordingRunner(
            _ => File.Delete(Path.Combine(vst3, "ValhallaSupermassive.vst3")));

        var library = new Library(layout, recording);

        library.Remove(library.RemovalOf(library.Find("supermassive")));

        Assert.Contains(recording.Calls, Synced);
        Assert.Equal("freq-echo", Assert.Single(library.Installed()).Key);
        Assert.True(File.Exists(Path.Combine(vst3, "ValhallaFreqEcho.vst3")));
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

        var scan = layout.NativeScanDir(".clap");
        Directory.CreateDirectory(scan);
        File.CreateSymbolicLink(Path.Combine(scan, "Dexed.clap"), ours);
        File.CreateSymbolicLink(Path.Combine(scan, "Someone.clap"), elsewhere);

        Subject().Remove(Subject().RemovalOf(Native("dexed")));

        Assert.False(Directory.Exists(installed));
        Assert.False(Path.Exists(Path.Combine(scan, "Dexed.clap")));
        Assert.True(Path.Exists(Path.Combine(scan, "Someone.clap")));
    }

    [Fact]
    public void RemovingANativePluginTakesItsPluginsOutOfTheScanPaths()
    {
        var archive = NestedArchive();
        Catalogue(("sampler", "Name: Sampler\nKind: native\nSource: byo\n"));
        var layout = Layout();
        var library = new Library(layout, new ProcessRunner());
        library.Install(library.Find("sampler"), installer: archive);

        library.Remove(library.RemovalOf(library.Find("sampler")));

        Assert.Empty(Directory.EnumerateFileSystemEntries(layout.NativeScanDir(".vst3")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(layout.NativeScanDir(".so")));
        Assert.False(Directory.Exists(layout.NativePath("sampler")));
    }

    [Fact]
    public void ANativeInstallMovedFromTheLegacyPlaceIsRemovedWithItsLinks()
    {
        var layout = Layout();
        var bundle = Path.Combine(layout.NativePath("gone"), "Gone.vst3");
        Directory.CreateDirectory(bundle);
        Directory.CreateDirectory(layout.ScanDir(".vst3"));
        File.CreateSymbolicLink(Path.Combine(layout.ScanDir(".vst3"), "Gone.vst3"), bundle);
        Enrolment.MoveLegacyScanLinks(layout);
        var library = new Library(layout, new RecordingRunner());

        library.Remove(library.RemovalOf(library.Removable("gone")));

        Assert.Empty(Directory.EnumerateFileSystemEntries(layout.NativeScanDir(".vst3")));
        Assert.Empty(Directory.EnumerateDirectories(layout.NativeDir));
    }

    [Fact]
    public void ANativeInstallTheCatalogueNoLongerListsCanStillBeRemoved()
    {
        var layout = Layout();
        var bundle = Path.Combine(layout.NativePath("gone"), "Gone.clap");
        Directory.CreateDirectory(layout.NativePath("gone"));
        File.WriteAllText(bundle, "");
        Directory.CreateDirectory(layout.NativeScanDir(".clap"));
        File.CreateSymbolicLink(Path.Combine(layout.NativeScanDir(".clap"), "Gone.clap"), bundle);
        var library = new Library(layout, new RecordingRunner());

        var retired = Assert.Single(library.Retired());
        Assert.Equal(("gone", PluginKind.Native), (retired.Id, retired.Kind));

        library.Remove(library.RemovalOf(library.Removable("gone")));

        Assert.Empty(library.Retired());
        Assert.Empty(Directory.EnumerateFileSystemEntries(layout.NativeScanDir(".clap")));
        Assert.Empty(Directory.EnumerateDirectories(layout.NativeDir));
    }

    [Fact]
    public void AWindowsInstallTheCatalogueNoLongerListsGoesWithItsPrefix()
    {
        var layout = Layout();
        Directory.CreateDirectory(Path.Combine(layout.PrefixPath("old"), "dosdevices"));
        File.WriteAllText(layout.PrefixPluginsFile("old"), "gone\n");
        var library = new Library(layout, new RecordingRunner());

        var removal = library.RemovalOf(library.Removable("gone"));

        Assert.Equal((RemovalKind.PluginOrPrefix, "old"), (removal.Kind, removal.Prefix));

        library.Remove(removal, takePrefix: true);

        Assert.False(Directory.Exists(layout.PrefixPath("old")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(layout.PrefixesDir));
    }
}
