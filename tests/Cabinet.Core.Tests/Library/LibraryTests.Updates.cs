using System.Text.Json;
using Cabinet.Core;

namespace Cabinet.Core.Tests;

public partial class LibraryTests
{
    private const string SetupRecipe = """
        Name: Thing
        Kind: windows
        Source: byo
        Prefix: catalogue-default
        Winetricks: corefonts
        """;

    [Fact]
    public void AChangedDependencyUpdatesTheRecordedPrefixWithoutRunningAnInstaller()
    {
        var layout = InstallSetup(SetupRecipe, "chosen");
        Catalogue(("thing", SetupRecipe.Replace("corefonts", "allfonts")));
        var recorder = new RecordingRunner();
        var library = new Library(layout, recorder);
        var update = Assert.IsType<PrefixUpdate>(library.PrefixUpdateOf(library.Find("thing")));

        Assert.True(update.Recorded);
        Assert.True(update.Available);
        Assert.Equal("chosen", update.Prefix);
        Assert.Contains("allfonts", string.Join("\n", update.Changes));

        library.UpdatePrefix(update);

        var dependency = Assert.Single(recorder.Ran, call => call.File == Core.Layout.Winetricks);
        Assert.Equal(["--unattended", "allfonts"], dependency.Arguments);
        Assert.Equal(layout.PrefixPath("chosen"), dependency.Environment["WINEPREFIX"]);
        Assert.DoesNotContain(recorder.Ran, call => call.File == "curl" || call.File == "sh");
        Assert.DoesNotContain(recorder.Ran, call => call.Arguments.Contains(SetupInstaller));
        Assert.False(Directory.Exists(layout.PrefixPath("catalogue-default")));
        Assert.Empty(library.PrefixUpdates());
        Assert.False(library.PrefixUpdateOf(library.Find("thing"))!.Available);
    }

    [Fact]
    public void CosmeticChangesAndReorderedRequirementsDoNotOfferAnUpdate()
    {
        var recipe = SetupRecipe.Replace("corefonts", "corefonts, vcrun2022")
                     + "\nEnv:\n  FIRST=one\n  SECOND=two\n";
        var layout = InstallSetup(recipe);
        Catalogue(("thing", recipe.Replace("Name: Thing", "Name: Renamed")
            .Replace("corefonts, vcrun2022", "vcrun2022, corefonts")
            .Replace("  FIRST=one\n  SECOND=two", "  SECOND=two\n  FIRST=one")
            + "\nDescription: A different description\n"));
        var library = new Library(layout, new UnusedRunner());

        Assert.False(library.PrefixUpdateOf(library.Find("thing"))!.Available);
        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void AnOlderInstallationMatchingTheCatalogueOffersNothing()
    {
        var layout = OlderInstallation(SetupRecipe, "corefonts\n");
        File.WriteAllText(layout.PrefixEnvFile("chosen"), "CUSTOM=value\n");
        var library = new Library(layout, new UnusedRunner());

        var update = Assert.IsType<PrefixUpdate>(library.PrefixUpdateOf(library.Find("thing")));

        Assert.False(update.Recorded);
        Assert.False(update.Available);
        Assert.Empty(library.PrefixUpdates());
        Assert.False(File.Exists(layout.PrefixSetupFile("chosen")));
    }

    [Fact]
    public void AnOlderInstallationMissingAVerbOffersOnlyThatVerb()
    {
        var layout = OlderInstallation(SetupRecipe.Replace("corefonts", "corefonts, vcrun2022"), "corefonts\n");
        var recorder = new RecordingRunner();
        var library = new Library(layout, recorder);
        var update = library.PrefixUpdateOf(library.Find("thing"))!;

        Assert.Equal(["Winetricks: vcrun2022."], update.Changes);

        library.UpdatePrefix(update);

        Assert.Equal(["--unattended", "vcrun2022"],
            Assert.Single(recorder.Ran, call => call.File == Core.Layout.Winetricks).Arguments);
        Assert.True(File.Exists(layout.PrefixSetupFile("chosen")));
        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void AnOlderInstallationOnAnotherRunnerKeepsIt()
    {
        var layout = OlderInstallation(SetupRecipe + "\nRunner: 9.21\n", "corefonts\n");
        var recorder = new RecordingRunner();
        var library = new Library(layout, recorder);
        var update = library.PrefixUpdateOf(library.Find("thing"))!;

        Assert.Equal(["Wine: 9.21."], update.Changes);
        Assert.Contains("Keep Wine bundled", string.Join("\n", update.Preserved));

        library.UpdatePrefix(update);

        Assert.DoesNotContain(recorder.Ran, call => call.Arguments.Contains("wineboot"));
        Assert.False(File.Exists(layout.PrefixRunnerFile("chosen")));
        Assert.Empty(library.PrefixUpdates());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"version\":2}")]
    public void AnUnreadableReceiptComparesThePrefixItself(string receipt)
    {
        var layout = InstallSetup(SetupRecipe);
        File.WriteAllText(layout.PrefixSetupFile("chosen"), receipt);
        var library = new Library(layout, new UnusedRunner());

        var update = Assert.IsType<PrefixUpdate>(library.PrefixUpdateOf(library.Find("thing")));

        Assert.False(update.Recorded);
        Assert.Equal(["Winetricks: corefonts."], update.Changes);
        Assert.Equal(receipt, File.ReadAllText(layout.PrefixSetupFile("chosen")));
    }

    [Fact]
    public void AnEnvironmentUpdateChangesOnlyValuesStillMatchingTheEarlierRecipe()
    {
        var recipe = SetupRecipe + "\nEnv:\n  MATCH=old\n  CUSTOM=old\n  REMOVE=old\n";
        var layout = InstallSetup(recipe);
        var settings = new PrefixSettings(layout);
        settings.SetVariable("chosen", "CUSTOM", "personal-choice");
        settings.SetVariable("chosen", "UNRELATED", "kept");
        Catalogue(("thing", SetupRecipe
            + "\nEnv:\n  MATCH=new\n  CUSTOM=new\n  ADDED=new\n"));
        var library = new Library(layout, new RecordingRunner());
        var update = library.PrefixUpdateOf(library.Find("thing"))!;

        library.UpdatePrefix(update);

        var variables = settings.Variables("chosen");
        Assert.Equal("new", variables["MATCH"]);
        Assert.Equal("personal-choice", variables["CUSTOM"]);
        Assert.Equal("new", variables["ADDED"]);
        Assert.Equal("kept", variables["UNRELATED"]);
        Assert.False(variables.ContainsKey("REMOVE"));
        Assert.Contains("CUSTOM", string.Join("\n", update.Preserved));
        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void AnEnvironmentUpdatePreservesAVariableTheUserRemoved()
    {
        var recipe = SetupRecipe + "\nEnv: MANAGED=old\n";
        var layout = InstallSetup(recipe);
        var settings = new PrefixSettings(layout);
        settings.SetVariable("chosen", "MANAGED", null);
        Catalogue(("thing", recipe.Replace("MANAGED=old", "MANAGED=new")));
        var library = new Library(layout, new RecordingRunner());
        var update = library.PrefixUpdateOf(library.Find("thing"))!;

        library.UpdatePrefix(update);

        Assert.False(settings.Variables("chosen").ContainsKey("MANAGED"));
        Assert.Contains("MANAGED", string.Join("\n", update.Preserved));
        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void APrefixSharedWithAnotherSetupGetsOnlyMissingDependencies()
    {
        var recipe = SetupRecipe + "\nSync: fsync\nEnv: MATCH=old\n";
        var layout = InstallSetup(recipe);
        var settings = new PrefixSettings(layout);
        settings.SetSync("chosen", SyncMode.Fsync);
        var receipt = File.ReadAllText(layout.PrefixSetupFile("chosen"));
        AnotherSetupIn("chosen");
        Catalogue(("thing", recipe.Replace("corefonts", "allfonts").Replace("fsync", "esync")
            .Replace("MATCH=old", "MATCH=new") + "Desktop: true\nEnv:\n  MATCH=new\n  ADDED=new\n"));
        var log = layout.PrefixWinetricksLog("chosen");
        var recorder = new RecordingRunner(arguments =>
            File.AppendAllLines(log, arguments.Where(argument => argument == "allfonts")));
        var library = new Library(layout, recorder);
        var update = library.PrefixUpdateOf(library.Find("thing"))!;

        library.UpdatePrefix(update);

        Assert.Equal(["another"], update.Sharing);
        Assert.Equal(["Winetricks: allfonts."], update.Changes);
        Assert.Equal(SyncMode.Fsync, settings.Sync("chosen"));
        Assert.Equal("old", settings.Variables("chosen")["MATCH"]);
        Assert.False(settings.Variables("chosen").ContainsKey("ADDED"));
        Assert.Equal(["--unattended", "allfonts"],
            Assert.Single(recorder.Ran, call => call.File == Core.Layout.Winetricks).Arguments);
        Assert.Equal(receipt, File.ReadAllText(layout.PrefixSetupFile("chosen")));
        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void AChangedSyncRecipePreservesACustomSyncMode()
    {
        var recipe = SetupRecipe + "\nSync: fsync\n";
        var layout = InstallSetup(recipe);
        var settings = new PrefixSettings(layout);
        settings.SetSync("chosen", SyncMode.Ntsync);
        Catalogue(("thing", recipe.Replace("fsync", "esync")));
        var library = new Library(layout, new RecordingRunner());
        var update = library.PrefixUpdateOf(library.Find("thing"))!;

        library.UpdatePrefix(update);

        Assert.Equal(SyncMode.Ntsync, settings.Sync("chosen"));
        Assert.NotEmpty(update.Preserved);
        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void AChangedSyncRecipeUpdatesTheEarlierManagedMode()
    {
        var recipe = SetupRecipe + "\nSync: fsync\n";
        var layout = InstallSetup(recipe, before: prepared =>
            new PrefixSettings(prepared).SetSync("chosen", SyncMode.Fsync));
        var settings = new PrefixSettings(layout);
        Catalogue(("thing", recipe.Replace("fsync", "esync")));
        var library = new Library(layout, new RecordingRunner());

        library.UpdatePrefix(library.PrefixUpdateOf(library.Find("thing"))!);

        Assert.Equal(SyncMode.Esync, settings.Sync("chosen"));
        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void RemovingTheManagedRunnerRecommendationReturnsToBundledWine()
    {
        var recipe = SetupRecipe + "\nRunner: 9.21\n";
        var layout = InstallSetup(recipe, before: OnRunner("9.21"));
        Catalogue(("thing", SetupRecipe));
        var recorder = new RecordingRunner();
        var library = new Library(layout, recorder);

        library.UpdatePrefix(library.PrefixUpdateOf(library.Find("thing"))!);

        Assert.False(File.Exists(layout.PrefixRunnerFile("chosen")));
        Assert.Contains(recorder.Ran, call => call.Arguments.SequenceEqual(["wineboot", "-u"]));
        Assert.DoesNotContain(recorder.Ran, call => call.File == "curl");
        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void AFailedRunnerChangeKeepsItsReceiptAndCanBeRetried()
    {
        var recipe = SetupRecipe + "\nRunner: 9.21\n";
        var layout = InstallSetup(recipe, before: OnRunner("9.21"));
        var wantedWine = layout.RunnerWine("10.0");
        Directory.CreateDirectory(Path.GetDirectoryName(wantedWine)!);
        File.WriteAllText(wantedWine, "");
        var receipt = File.ReadAllText(layout.PrefixSetupFile("chosen"));
        Catalogue(("thing", recipe.Replace("9.21", "10.0")));
        var recorder = new RecordingRunner(exits: _ => 7);
        var library = new Library(layout, recorder);

        Assert.Throws<InvalidOperationException>(() => library.UpdatePrefix(
            library.PrefixUpdateOf(library.Find("thing"))!));

        Assert.Equal("9.21", File.ReadAllText(layout.PrefixRunnerFile("chosen")).Trim());
        Assert.Equal(receipt, File.ReadAllText(layout.PrefixSetupFile("chosen")));
        Assert.Contains(recorder.Ran, call => Path.GetFileName(call.File) == "wineserver"
            && call.Environment["WINELOADER"] == wantedWine);
        Assert.True(library.PrefixUpdateOf(library.Find("thing"))!.Available);

        var retry = new Library(layout, new RecordingRunner());
        retry.UpdatePrefix(retry.PrefixUpdateOf(retry.Find("thing"))!);

        Assert.Equal("10.0", File.ReadAllText(layout.PrefixRunnerFile("chosen")).Trim());
        Assert.Empty(retry.PrefixUpdates());
    }

    [Fact]
    public void ANewDesktopRecommendationAppliesToTheRecordedPrefix()
    {
        var layout = InstallSetup(SetupRecipe);
        Catalogue(("thing", SetupRecipe + "\nDesktop: true\n"));
        var recorder = new RecordingRunner();
        var library = new Library(layout, recorder);

        library.UpdatePrefix(library.PrefixUpdateOf(library.Find("thing"))!);

        var registry = recorder.Ran.Where(call => call.Arguments.Contains("reg")
            && call.Arguments.Contains("add")).ToList();
        Assert.Equal(2, registry.Count);
        Assert.All(registry, call => Assert.Equal(layout.PrefixPath("chosen"),
            call.Environment["WINEPREFIX"]));
        Assert.Contains(registry, call => call.Arguments.Contains("1920x1080"));
        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void ReviewingAnOlderInstallationAddsDependenciesAndKeepsExistingPreferences()
    {
        var layout = InstallSetup(SetupRecipe + "\nEnv: EXISTING=old\n");
        File.Delete(layout.PrefixSetupFile("chosen"));
        var settings = new PrefixSettings(layout);
        settings.SetSync("chosen", SyncMode.Ntsync);
        settings.SetVariable("chosen", "EXISTING", "custom");
        Catalogue(("thing", SetupRecipe.Replace("corefonts", "allfonts")
            + "\nSync: fsync\nEnv:\n  EXISTING=new\n  MISSING=added\n"));
        var recorder = new RecordingRunner();
        var library = new Library(layout, recorder);

        library.UpdatePrefix(library.PrefixUpdateOf(library.Find("thing"))!);

        Assert.Equal(SyncMode.Ntsync, settings.Sync("chosen"));
        Assert.Equal("custom", settings.Variables("chosen")["EXISTING"]);
        Assert.Equal("added", settings.Variables("chosen")["MISSING"]);
        Assert.Equal(["--unattended", "allfonts"],
            Assert.Single(recorder.Ran, call => call.File == Core.Layout.Winetricks).Arguments);
        Assert.True(File.Exists(layout.PrefixSetupFile("chosen")));
        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void ADependencyRemovedFromTheRecipeIsAcknowledgedWithoutUninstallingIt()
    {
        var layout = InstallSetup(SetupRecipe);
        Catalogue(("thing", SetupRecipe.Replace("Winetricks: corefonts", "")));
        var recorder = new RecordingRunner();
        var library = new Library(layout, recorder);
        var update = library.PrefixUpdateOf(library.Find("thing"))!;

        Assert.True(update.Available);
        library.UpdatePrefix(update);

        Assert.DoesNotContain(recorder.Ran, call => call.File == Core.Layout.Winetricks);
        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void ARecipeChangedAfterReviewIsRefusedBeforeAnyWork()
    {
        var layout = InstallSetup(SetupRecipe);
        Catalogue(("thing", SetupRecipe.Replace("corefonts", "allfonts")));
        var recorder = new RecordingRunner();
        var library = new Library(layout, recorder);
        var update = library.PrefixUpdateOf(library.Find("thing"))!;
        Catalogue(("thing", SetupRecipe.Replace("corefonts", "vcrun2022")));

        Assert.Throws<InvalidOperationException>(() => library.UpdatePrefix(update));

        Assert.Empty(recorder.Ran);
    }

    [Fact]
    public void SettingsChangedAfterReviewAreRefusedBeforeAnyWork()
    {
        var layout = InstallSetup(SetupRecipe);
        Catalogue(("thing", SetupRecipe.Replace("corefonts", "allfonts")));
        var recorder = new RecordingRunner();
        var library = new Library(layout, recorder);
        var update = library.PrefixUpdateOf(library.Find("thing"))!;
        new PrefixSettings(layout).SetVariable("chosen", "CHANGED", "after-review");

        Assert.Throws<InvalidOperationException>(() => library.UpdatePrefix(update));

        Assert.Empty(recorder.Ran);
    }

    [Fact]
    public void ADxvkVersionChangedAfterReviewIsRefusedBeforeAnyWork()
    {
        var layout = InstallSetup(SetupRecipe);
        File.WriteAllText(layout.PrefixDxvkFile("chosen"), "2.6\n");
        Catalogue(("thing", SetupRecipe.Replace("corefonts", "allfonts")));
        var recorder = new RecordingRunner();
        var library = new Library(layout, recorder);
        var update = library.PrefixUpdateOf(library.Find("thing"))!;
        File.WriteAllText(layout.PrefixDxvkFile("chosen"), "2.7\n");

        Assert.Throws<InvalidOperationException>(() => library.UpdatePrefix(update));

        Assert.Empty(recorder.Ran);
    }

    [Fact]
    public void PluginsAddedToThePrefixAfterReviewAreRefusedBeforeAnyWork()
    {
        var layout = InstallSetup(SetupRecipe);
        Catalogue(("thing", SetupRecipe.Replace("corefonts", "allfonts")));
        var recorder = new RecordingRunner();
        var library = new Library(layout, recorder);
        var update = library.PrefixUpdateOf(library.Find("thing"))!;
        AnotherSetupIn("chosen");

        Assert.Throws<InvalidOperationException>(() => library.UpdatePrefix(update));

        Assert.Empty(recorder.Ran);
    }

    [Fact]
    public void ADawUsingThePrefixRefusesTheUpdate()
    {
        var layout = InstallSetup(SetupRecipe);
        Catalogue(("thing", SetupRecipe.Replace("corefonts", "allfonts")));
        var recorder = new RecordingRunner();
        var library = new Library(layout, recorder);
        var update = library.PrefixUpdateOf(library.Find("thing"))!;
        using var plugin = SessionFiles.HeldByAPlugin(SessionFiles.Of(layout, "chosen").Busy);

        Assert.Throws<PrefixInUseException>(() => library.UpdatePrefix(update));

        Assert.Empty(recorder.Ran);
    }

    [Fact]
    public void AFailedDependencyUpdateKeepsTheEarlierReceiptAndRemainsAvailable()
    {
        var layout = InstallSetup(SetupRecipe);
        var receipt = File.ReadAllText(layout.PrefixSetupFile("chosen"));
        Catalogue(("thing", SetupRecipe.Replace("corefonts", "allfonts")));
        var recorder = new RecordingRunner(exits: _ => 7);
        var library = new Library(layout, recorder);
        var update = library.PrefixUpdateOf(library.Find("thing"))!;

        Assert.Throws<InvalidOperationException>(() => library.UpdatePrefix(update));

        Assert.Equal(receipt, File.ReadAllText(layout.PrefixSetupFile("chosen")));
        Assert.True(library.PrefixUpdateOf(library.Find("thing"))!.Available);
        Assert.Equal("wineserver", recorder.Ran.Last().File);
        Assert.Equal(["-k"], recorder.Ran.Last().Arguments);
    }

    [Fact]
    public void ASuccessfulInstallRecordsTheRecipeAndAFailedReinstallKeepsIt()
    {
        var layout = InstallSetup(SetupRecipe);
        var receipt = File.ReadAllText(layout.PrefixSetupFile("chosen"));
        Catalogue(("thing", SetupRecipe.Replace("corefonts", "allfonts")));
        var library = new Library(layout, new RecordingRunner(exits: _ => 7));

        Assert.Throws<InvalidOperationException>(() => library.Install(
            library.Find("thing"), installer: Path.Combine(root, SetupInstaller)));

        Assert.Equal(receipt, File.ReadAllText(layout.PrefixSetupFile("chosen")));
    }

    [Fact]
    public void ASuccessfulInstallPinsItsSetupToTheConfigVersion()
    {
        var layout = InstallSetup(SetupRecipe + "\nVersion: 1.2.3\n");
        var library = new Library(layout, new UnusedRunner());
        using var receipt = JsonDocument.Parse(
            File.ReadAllText(layout.PrefixSetupFile("chosen")));

        Assert.Equal("1.2.3", receipt.RootElement.GetProperty("configVersion").GetString());
        Assert.Equal("1.2.3, revision 1", library.PrefixUpdateOf(library.Find("thing"))!.Applied);
        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void AVersionOnlyChangeReviewsItsSetupWithoutRunningWine()
    {
        var recipe = SetupRecipe + "\nVersion: 1.2.3\n";
        var layout = InstallSetup(recipe);
        Catalogue(("thing", recipe.Replace("1.2.3", "1.3.0")));
        var recorder = new RecordingRunner();
        var library = new Library(layout, recorder);
        var update = library.PrefixUpdateOf(library.Find("thing"))!;

        Assert.True(update.Available);
        Assert.Equal("1.2.3, revision 1", update.Applied);
        Assert.Contains("1.3.0", string.Join("\n", update.Changes));

        library.UpdatePrefix(update);

        Assert.Empty(recorder.Ran);
        Assert.Equal("1.3.0, revision 1", library.PrefixUpdateOf(library.Find("thing"))!.Applied);
        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void ARecipeChangeWithinTheSameEntryVersionStillOffersAnUpdate()
    {
        var recipe = SetupRecipe + "\nVersion: 1.2.3\n";
        var layout = InstallSetup(recipe);
        Catalogue(("thing", recipe.Replace("corefonts", "allfonts")));
        var recorder = new RecordingRunner();
        var library = new Library(layout, recorder);
        var update = library.PrefixUpdateOf(library.Find("thing"))!;

        Assert.True(update.Available);
        Assert.Equal("1.2.3, revision 1", update.Applied);

        library.UpdatePrefix(update);

        Assert.Equal(["--unattended", "allfonts"],
            Assert.Single(recorder.Ran, call => call.File == Core.Layout.Winetricks).Arguments);
        Assert.Equal("1.2.3, revision 1", library.PrefixUpdateOf(library.Find("thing"))!.Applied);
        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void NativeAndUninstalledEntriesHaveNoPrefixUpdate()
    {
        Catalogue(("thing", SetupRecipe), ("native", "Name: Native\nKind: native\nSource: byo\n"));
        var layout = Layout();
        Directory.CreateDirectory(layout.NativePath("native"));
        var library = new Library(layout, new UnusedRunner());

        Assert.Null(library.PrefixUpdateOf(library.Find("thing")));
        Assert.Null(library.PrefixUpdateOf(library.Find("native")));
        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void RemovingOnePluginKeepsThePrefixReceiptForTheOthers()
    {
        var layout = InstallSetup(SetupRecipe);
        File.AppendAllText(layout.PrefixPluginsFile("chosen"), "another\n");
        var receipt = File.ReadAllText(layout.PrefixSetupFile("chosen"));
        var library = RemovableThing(layout);

        library.Remove(library.RemovalOf(library.Find("thing")));

        Assert.Equal(receipt, File.ReadAllText(layout.PrefixSetupFile("chosen")));
        Assert.Equal("another", Assert.Single(library.Installed()).Key);
    }

    [Fact]
    public void RemovingTheLastPluginForgetsThePrefixReceipt()
    {
        var layout = InstallSetup(SetupRecipe);
        var library = RemovableThing(layout);

        library.Remove(library.RemovalOf(library.Find("thing")));

        Assert.False(File.Exists(layout.PrefixSetupFile("chosen")));
        Assert.True(Directory.Exists(layout.PrefixPath("chosen")));
    }

    [Fact]
    public void ThePrefixConfigFollowsTheVersionTheInstalledSoftwareReports()
    {
        Catalogue(
            ("thing", "Name: Thing\nKind: windows\nSource: byo\nVersion: 2.0\n"),
            ("prefix-1.0", "Revision: 1\nWinetricks: corefonts\n"),
            ("prefix-2.0", "Revision: 1\nWinetricks: allfonts\n"));
        var layout = Layout();
        Directory.CreateDirectory(Path.Combine(layout.PrefixPath("chosen"), "dosdevices"));
        File.WriteAllText(
            layout.PrefixPluginsFile("chosen"),
            "thing\tHKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Thing\n");
        File.WriteAllText(layout.PrefixWinetricksLog("chosen"), "corefonts\n");
        var library = new Library(layout, new UnusedRunner());

        InstalledThingReports(layout, "1.5");
        var older = library.PrefixUpdateOf(library.Find("thing"))!;
        InstalledThingReports(layout, "2.1");
        var newer = library.PrefixUpdateOf(library.Find("thing"))!;

        Assert.Equal("1.5", older.Software);
        Assert.Equal("1.0, revision 1", older.Config);
        Assert.Empty(older.Changes);
        Assert.Equal("2.1", newer.Software);
        Assert.Equal("2.0, revision 1", newer.Config);
        Assert.Equal(["Winetricks: allfonts."], newer.Changes);
    }

    [Fact]
    public void AFamilySharesOneUpdateAcrossItsMembers()
    {
        var layout = InstallFamily();
        Catalogue(("prefix-1", FamilyConfig.Replace("Revision: 1", "Revision: 2").Replace("corefonts", "allfonts")));
        var recorder = new RecordingRunner();
        var library = new Library(layout, recorder);

        Assert.Equal(["one", "two"], library.PrefixUpdates().Keys.Order(StringComparer.Ordinal));
        var update = Assert.Single(library.PendingPrefixUpdates());
        Assert.Equal(["one", "two"], update.Members);
        Assert.Empty(update.Sharing);
        Assert.Equal("1, revision 1", update.Applied);
        Assert.Contains("Config 1: revision 1 → 2.", update.Changes);

        library.UpdatePrefix(update);

        Assert.Single(recorder.Ran, call => call.File == Core.Layout.Winetricks);
        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void AFamilyMemberFollowsItsSiblingIntoItsPrefix()
    {
        var layout = InstallFamily();

        Assert.Equal("custom", new Library(layout, new UnusedRunner()).Installed()["two"]);
    }

    [Fact]
    public void AnInstallIntoAPrefixOnAnotherWineKeepsItWithoutOfferingToMoveIt()
    {
        var layout = InstallSetup(SetupRecipe + "\nRunner: 10.0\n", before: OnRunner("9.21"));
        var library = new Library(layout, new UnusedRunner());

        Assert.Empty(library.PrefixUpdates());
        Assert.Equal("9.21", File.ReadAllText(layout.PrefixRunnerFile("chosen")).Trim());
    }

    [Fact]
    public void AFamilyMemberSkipsASiblingsPrefixThatHoldsAnotherSetup()
    {
        Catalogue(
            ("prefix-1", FamilyConfig),
            ("one", "Name: One\nKind: windows\nSource: byo\n"),
            ("two", "Name: Two\nKind: windows\nSource: byo\n"));
        var layout = Layout();
        Directory.CreateDirectory(layout.PrefixPath("mixed"));
        AnotherSetupIn("mixed");
        File.AppendAllText(layout.PrefixPluginsFile("mixed"), "one\n");
        var library = new Library(layout, new UnusedRunner());

        Assert.Equal(Vendor, library.DefaultPrefix(library.Find("two")));
    }

    [Fact]
    public void AFolderLeftWithoutAWinePrefixIsSetUpAsANewOne()
    {
        Catalogue(("thing", SetupRecipe + "\nSync: fsync\n"));
        var layout = Layout();
        Directory.CreateDirectory(layout.PrefixPath("chosen"));
        File.WriteAllText(Path.Combine(layout.PrefixPath("chosen"), "stray"), "");
        var installer = Path.Combine(root, SetupInstaller);
        File.WriteAllText(installer, "");
        var library = new Library(layout, new RecordingRunner(_ =>
            Directory.CreateDirectory(Path.Combine(layout.PrefixPath("chosen"), "dosdevices"))));

        library.Install(library.Find("thing"), "chosen", installer);

        Assert.Equal(SyncMode.Fsync, new PrefixSettings(layout).Sync("chosen"));
        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void ACatalogueDefaultHeldByAnotherSetupFallsBackToAPrefixOfItsOwn()
    {
        var layout = InstallFamily();
        Write("another-vendor", "stray.yml", "Name: Stray\nKind: windows\nSource: byo\nPrefix: custom\n");
        var library = new Library(layout, new RecordingRunner());

        library.Install(library.Find("stray"), installer: Path.Combine(root, SetupInstaller));

        Assert.Equal("stray", library.Installed()["stray"]);
    }

    [Fact]
    public void InstallingIntoAPrefixSetUpForOtherPluginsIsRefused()
    {
        var layout = InstallFamily();
        Write("another-vendor", "thing.yml", SetupRecipe);
        var recorder = new RecordingRunner();
        var library = new Library(layout, recorder);

        var refused = Assert.Throws<InvalidOperationException>(() => library.Install(
            library.Find("thing"), "custom", Path.Combine(root, SetupInstaller)));

        Assert.Contains("custom holds one", refused.Message);
        Assert.Empty(recorder.Ran);
        Assert.False(library.Installed().ContainsKey("thing"));
    }

    private const string SetupInstaller = "synthetic-setup.exe";

    private const string FamilyConfig = "Revision: 1\nWinetricks: corefonts\n";

    private Layout InstallFamily()
    {
        Catalogue(
            ("prefix-1", FamilyConfig),
            ("one", "Name: One\nKind: windows\nSource: byo\n"),
            ("two", "Name: Two\nKind: windows\nSource: byo\n"));
        var layout = Layout();
        Directory.CreateDirectory(Path.Combine(layout.PrefixPath("custom"), "dosdevices"));
        var installer = Path.Combine(root, SetupInstaller);
        File.WriteAllText(installer, "");
        var library = new Library(layout, new RecordingRunner());
        library.Install(library.Find("one"), "custom", installer);
        library.Install(library.Find("two"), installer: installer);
        return layout;
    }

    private void AnotherSetupIn(string prefix)
    {
        Write("another-vendor", "another.yml", "Name: Another\nKind: windows\nSource: byo\n");
        File.AppendAllText(Layout().PrefixPluginsFile(prefix), "another\n");
    }

    private static Action<Layout> OnRunner(string name) => prepared =>
    {
        var wine = prepared.RunnerWine(name);
        Directory.CreateDirectory(Path.GetDirectoryName(wine)!);
        File.WriteAllText(wine, "");
        File.WriteAllText(prepared.PrefixRunnerFile("chosen"), name + "\n");
    };

    private Layout OlderInstallation(string recipe, string verbs)
    {
        Catalogue(("thing", recipe));
        var layout = Layout();
        Directory.CreateDirectory(Path.Combine(layout.PrefixPath("chosen"), "dosdevices"));
        File.WriteAllText(layout.PrefixPluginsFile("chosen"), "thing\n");
        File.WriteAllText(layout.PrefixWinetricksLog("chosen"), verbs);
        return layout;
    }

    private static void InstalledThingReports(Layout layout, string version) =>
        File.WriteAllText(layout.PrefixSystemReg("chosen"), $$"""
            WINE REGISTRY Version 2

            [Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Thing] 1787344290
            "DisplayName"="Thing version {{version}}"
            "DisplayVersion"="{{version}}"
            "UninstallString"="C:\\uninstall.exe"
            """);

    private Library RemovableThing(Layout layout)
    {
        var bundle = Path.Combine(layout.PrefixVst3Dir("chosen"), "Thing.vst3");
        File.WriteAllText(bundle, "");
        File.WriteAllText(layout.PrefixSystemReg("chosen"), """
            WINE REGISTRY Version 2

            [Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Thing] 1787344290
            "DisplayName"="Thing"
            "UninstallString"="C:\\uninstall.exe"
            """);
        return new Library(layout, new RecordingRunner(_ => File.Delete(bundle)));
    }

    private Layout InstallSetup(string recipe, string prefix = "chosen", Action<Layout>? before = null)
    {
        Catalogue(("thing", recipe));
        var layout = Layout();
        Directory.CreateDirectory(Path.Combine(layout.PrefixPath(prefix), "dosdevices"));
        before?.Invoke(layout);
        var installer = Path.Combine(root, SetupInstaller);
        File.WriteAllText(installer, "");
        var library = new Library(layout, new RecordingRunner());
        library.Install(library.Find("thing"), prefix, installer);
        return layout;
    }
}
