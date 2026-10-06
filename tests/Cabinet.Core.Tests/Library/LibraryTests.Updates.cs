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
        var recorder = Winetricked(layout);
        var library = new Library(layout, recorder);
        var update = Assert.IsType<PrefixUpdate>(library.PrefixUpdateOf(library.Find("thing")));

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
    public void AnInstallationWithoutAReceiptIsRecordedOnTheFirstConfig()
    {
        var layout = OlderInstallation();
        var library = new Library(layout, new UnusedRunner());

        var update = Assert.IsType<PrefixUpdate>(library.PrefixUpdateOf(library.Find("thing")));

        Assert.Equal("1.0, revision 1", update.Applied);
        Assert.Equal("2.0, revision 1", update.Config);
        Assert.Contains("Previously version 1.0", update.Summary);
        Assert.Equal(["Install allfonts with Winetricks."], update.Changes);
        using var receipt = JsonDocument.Parse(File.ReadAllText(layout.PrefixSetupFile("chosen")));
        Assert.Equal("1.0", receipt.RootElement.GetProperty("configVersion").GetString());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"version\":2}")]
    [InlineData("{\"version\":1,\"configVersion\":\"2.0\",\"runner\":null,\"dxvk\":false,\"sync\":\"system\",\"winetricks\":[\"allfonts\"],\"env\":{},\"desktop\":false}")]
    public void AnUnreadableReceiptIsReplacedByTheFirstConfig(string receipt)
    {
        var layout = OlderInstallation();
        File.WriteAllText(layout.PrefixSetupFile("chosen"), receipt);
        var library = new Library(layout, new UnusedRunner());

        var update = Assert.IsType<PrefixUpdate>(library.PrefixUpdateOf(library.Find("thing")));

        Assert.Equal("1.0, revision 1", update.Applied);
        Assert.Equal(["Install allfonts with Winetricks."], update.Changes);
        Assert.NotEqual(receipt, File.ReadAllText(layout.PrefixSetupFile("chosen")));
        Assert.Equal("1.0, revision 1", library.PrefixUpdateOf(library.Find("thing"))!.Applied);
    }

    [Fact]
    public void UpdatingAnInstallationWithoutAReceiptAdjustsEveryManagedSetting()
    {
        var layout = OlderInstallation();
        var settings = new PrefixSettings(layout);
        settings.SetSync("chosen", SyncMode.Ntsync);
        settings.SetVariable("chosen", "CUSTOM", "value");
        var recorder = Winetricked(layout);
        var library = new Library(layout, recorder);

        library.UpdatePrefix(library.PrefixUpdateOf(library.Find("thing"))!);

        Assert.Equal(SyncMode.System, settings.Sync("chosen"));
        Assert.Equal("value", settings.Variables("chosen")["CUSTOM"]);
        Assert.Equal(["--unattended", "allfonts"],
            Assert.Single(recorder.Ran, call => call.File == Core.Layout.Winetricks).Arguments);
        Assert.Equal("2.0, revision 1", library.PrefixUpdateOf(library.Find("thing"))!.Applied);
        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void ReinstallingIntoAPrefixWithoutAReceiptRecordsTheFirstConfig()
    {
        var layout = OlderInstallation();
        var installer = Path.Combine(root, SetupInstaller);
        File.WriteAllText(installer, "");
        var library = new Library(layout, new RecordingRunner());

        library.Install(library.Find("thing"), "chosen", installer);

        Assert.Equal("1.0, revision 1", library.PrefixUpdateOf(library.Find("thing"))!.Applied);
        Assert.Single(library.PrefixUpdates());
    }

    [Fact]
    public void AnEnvironmentUpdateSetsEveryVariableTheConfigNames()
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
        Assert.Equal("new", variables["CUSTOM"]);
        Assert.Equal("new", variables["ADDED"]);
        Assert.Equal("kept", variables["UNRELATED"]);
        Assert.False(variables.ContainsKey("REMOVE"));
        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void AnEnvironmentUpdateRestoresAVariableTheUserRemoved()
    {
        var recipe = SetupRecipe + "\nEnv: MANAGED=old\n";
        var layout = InstallSetup(recipe);
        var settings = new PrefixSettings(layout);
        settings.SetVariable("chosen", "MANAGED", null);
        Catalogue(("thing", recipe.Replace("MANAGED=old", "MANAGED=new")));
        var library = new Library(layout, new RecordingRunner());
        var update = library.PrefixUpdateOf(library.Find("thing"))!;

        library.UpdatePrefix(update);

        Assert.Equal("new", settings.Variables("chosen")["MANAGED"]);
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
        Assert.Null(update.Overrides);
        Assert.Equal(["Install allfonts with Winetricks."], update.Changes);
        Assert.Equal(SyncMode.Fsync, settings.Sync("chosen"));
        Assert.Equal("old", settings.Variables("chosen")["MATCH"]);
        Assert.False(settings.Variables("chosen").ContainsKey("ADDED"));
        Assert.Equal(["--unattended", "allfonts"],
            Assert.Single(recorder.Ran, call => call.File == Core.Layout.Winetricks).Arguments);
        Assert.Equal(receipt, File.ReadAllText(layout.PrefixSetupFile("chosen")));
        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void AChangedSetupResetsEverySettingChangedByHand()
    {
        var recipe = SetupRecipe + "\nRunner: 9.21\nSync: fsync\nEnv: MANAGED=recommended\n";
        var layout = InstallSetup(recipe, before: OnRunner("9.21"));
        File.WriteAllText(layout.PrefixWinetricksLog("chosen"), "corefonts\n");
        OnRunner("10.0")(layout);
        var settings = new PrefixSettings(layout);
        settings.SetSync("chosen", SyncMode.Ntsync);
        settings.SetVariable("chosen", "MANAGED", "personal");
        File.WriteAllText(layout.PrefixDxvkFile("chosen"), Dxvk.Version + "\n");
        File.WriteAllText(layout.PrefixUserReg("chosen"), """
            WINE REGISTRY Version 2

            [Software\\Wine\\Explorer] 1787567817
            "Desktop"="Default"

            [Software\\Wine\\Explorer\\Desktops] 1787567817
            "Default"="1920x1080"
            """);
        Catalogue(("thing", recipe.Replace("corefonts", "allfonts")));
        var library = new Library(layout, new RecordingRunner());

        var update = library.PrefixUpdateOf(library.Find("thing"))!;

        Assert.Equal(["Install allfonts with Winetricks."], update.Changes);
        Assert.Equal(
            [
                "Switch Wine from 10.0 to 9.21.",
                "Switch the sync mode from ntsync to fsync.",
                "Turn off DXVK and use Wine's own Direct3D.",
                "Turn off the Wine virtual desktop.",
                "Change MANAGED from personal to recommended.",
            ],
            update.Resets);
        Assert.Contains("It may override your own changes to the prefix config.", update.Description);

        library.UpdatePrefix(update);

        Assert.Equal("9.21", File.ReadAllText(layout.PrefixRunnerFile("chosen")).Trim());
        Assert.Equal(SyncMode.Fsync, settings.Sync("chosen"));
        Assert.Equal("recommended", settings.Variables("chosen")["MANAGED"]);
        Assert.False(File.Exists(layout.PrefixDxvkFile("chosen")));
    }

    [Fact]
    public void KeepingCustomChangesAppliesOnlyTheRestAndRecordsTheNewSetup()
    {
        var recipe = SetupRecipe + "\nSync: fsync\nEnv:\n  MANAGED=recommended\n  FOLLOWED=old\n";
        var layout = InstallSetup(recipe);
        var settings = new PrefixSettings(layout);
        settings.SetSync("chosen", SyncMode.Ntsync);
        settings.SetVariable("chosen", "MANAGED", "personal");
        Catalogue(("thing", recipe.Replace("fsync", "esync").Replace("recommended", "newer")
            .Replace("FOLLOWED=old", "FOLLOWED=new")));
        var library = new Library(layout, Winetricked(layout));
        var update = library.PrefixUpdateOf(library.Find("thing"))!;

        library.UpdatePrefix(update, keepCustom: true);

        Assert.Equal(["Change FOLLOWED from old to new."], update.Changes);
        Assert.Equal(SyncMode.Ntsync, settings.Sync("chosen"));
        Assert.Equal("personal", settings.Variables("chosen")["MANAGED"]);
        Assert.Equal("new", settings.Variables("chosen")["FOLLOWED"]);
        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void AMissingComponentIsOfferedWithoutAConfigChange()
    {
        var layout = InstallSetup(SetupRecipe);
        File.WriteAllText(layout.PrefixWinetricksLog("chosen"), "");
        var library = new Library(layout, Winetricked(layout));

        var update = library.PrefixUpdateOf(library.Find("thing"))!;

        Assert.True(update.Available);
        Assert.Null(update.Revised);
        Assert.Equal(["Install corefonts with Winetricks."], update.Changes);
        Assert.Empty(update.Resets);

        library.UpdatePrefix(update);

        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void TheConfigStateShowsTheAppliedAndTheNewConfigInOneLine()
    {
        var layout = OlderInstallation();
        var library = new Library(layout, new UnusedRunner());

        Assert.Equal("1.0, revision 1 → 2.0, revision 1",
            library.PrefixUpdateOf(library.Find("thing"))!.ConfigState);
    }

    [Fact]
    public void SettingsStillOnTheOldConfigAreChangesNotResets()
    {
        var recipe = SetupRecipe + "\nRunner: 9.21\nSync: fsync\nEnv:\n  KEPT=same\n  CHANGED=old\n  REMOVED=gone\n";
        var layout = InstallSetup(recipe, before: prepared =>
        {
            OnRunner("9.21")(prepared);
            new PrefixSettings(prepared).SetSync("chosen", SyncMode.Fsync);
        });
        Catalogue(("thing", SetupRecipe
            + "\nRunner: 10.0\nSync: esync\nDxvk: true\nDesktop: true\nEnv:\n  KEPT=same\n  CHANGED=new\n  ADDED=new\n"));
        var library = new Library(layout, new UnusedRunner());

        var update = library.PrefixUpdateOf(library.Find("thing"))!;

        Assert.Equal(
            [
                "Switch Wine from 9.21 to 10.0.",
                "Switch the sync mode from fsync to esync.",
                "Turn on DXVK for Direct3D.",
                "Turn on the Wine virtual desktop.",
                "Set ADDED to new.",
                "Change CHANGED from old to new.",
                "Remove the variable REMOVED (now gone).",
            ],
            update.Changes);
        Assert.Empty(update.Resets);
        Assert.Null(update.Overrides);
        Assert.DoesNotContain(PrefixUpdate.ReplacesYours, update.Description);
    }

    [Fact]
    public void KeepingCustomChangesLeavesEveryHandChangedSetting()
    {
        var recipe = SetupRecipe + "\nRunner: 9.21\nSync: fsync\nEnv: MANAGED=recommended\n";
        var layout = InstallSetup(recipe, before: OnRunner("9.21"));
        OnRunner("10.0")(layout);
        var settings = new PrefixSettings(layout);
        settings.SetSync("chosen", SyncMode.Ntsync);
        settings.SetVariable("chosen", "MANAGED", "personal");
        File.WriteAllText(layout.PrefixDxvkFile("chosen"), Dxvk.Version + "\n");
        File.WriteAllText(layout.PrefixUserReg("chosen"), """
            WINE REGISTRY Version 2

            [Software\\Wine\\Explorer] 1787567817
            "Desktop"="Default"

            [Software\\Wine\\Explorer\\Desktops] 1787567817
            "Default"="1920x1080"
            """);
        Catalogue(("thing", recipe.Replace("corefonts", "allfonts")));
        var recorder = Winetricked(layout);
        var library = new Library(layout, recorder);

        library.UpdatePrefix(library.PrefixUpdateOf(library.Find("thing"))!, keepCustom: true);

        Assert.Equal("10.0", File.ReadAllText(layout.PrefixRunnerFile("chosen")).Trim());
        Assert.Equal(SyncMode.Ntsync, settings.Sync("chosen"));
        Assert.Equal("personal", settings.Variables("chosen")["MANAGED"]);
        Assert.True(File.Exists(layout.PrefixDxvkFile("chosen")));
        Assert.DoesNotContain(recorder.Ran, call => call.Arguments.Contains("reg"));
        Assert.Equal(["--unattended", "allfonts"],
            Assert.Single(recorder.Ran, call => call.File == Core.Layout.Winetricks).Arguments);
        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void AnUpToDatePrefixShowsOnlyItsConfig()
    {
        var layout = InstallSetup(SetupRecipe + "\nVersion: 1.0\n");
        var library = new Library(layout, new UnusedRunner());

        var review = Assert.Single(library.PrefixReviews()).Value;

        Assert.False(review.Available);
        Assert.Equal("1.0, revision 1", review.ConfigState);
        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void AMissingComponentShowsOnlyTheConfigItIsOn()
    {
        var layout = InstallSetup(SetupRecipe + "\nVersion: 1.0\n");
        File.WriteAllText(layout.PrefixWinetricksLog("chosen"), "");
        var library = new Library(layout, new UnusedRunner());

        var review = Assert.Single(library.PrefixReviews()).Value;

        Assert.True(review.Available);
        Assert.Equal("1.0, revision 1", review.ConfigState);
        Assert.Equal(["thing"], library.PrefixUpdates().Keys);
    }

    [Fact]
    public void APrefixSharedWithAnotherSetupGetsNoReceiptOfItsOwn()
    {
        var layout = OlderInstallation();
        AnotherSetupIn("chosen");
        var library = new Library(layout, new UnusedRunner());

        var update = library.PrefixUpdateOf(library.Find("thing"))!;

        Assert.Null(update.Applied);
        Assert.Equal(["Install allfonts with Winetricks."], update.Changes);
        Assert.False(File.Exists(layout.PrefixSetupFile("chosen")));
    }

    [Fact]
    public void ARecordedReceiptIsNotRewrittenByAReview()
    {
        var layout = InstallSetup(SetupRecipe);
        var receipt = File.ReadAllText(layout.PrefixSetupFile("chosen"));
        Catalogue(("thing", SetupRecipe.Replace("corefonts", "allfonts")));
        var library = new Library(layout, new UnusedRunner());

        library.PrefixUpdateOf(library.Find("thing"));

        Assert.Equal(receipt, File.ReadAllText(layout.PrefixSetupFile("chosen")));
    }

    [Fact]
    public void AReviewDoesNotRecreateAPrefixThatIsGone()
    {
        var layout = OlderInstallation();
        Directory.Delete(layout.PrefixPath("chosen"), recursive: true);
        var library = new Library(layout, new UnusedRunner());

        Assert.Null(library.PrefixUpdateOf(library.Find("thing")));
        Assert.Empty(library.PrefixReviews());
        Assert.False(Directory.Exists(layout.PrefixPath("chosen")));
    }

    [Fact]
    public void APrefixThatDiffersFromItsConfigIsEditedWithoutAnUpdate()
    {
        var recipe = SetupRecipe + "\nSync: fsync\nEnv:\n  CHANGED=old\n  REMOVED=gone\n";
        var layout = InstallSetup(recipe, before: prepared =>
            new PrefixSettings(prepared).SetSync("chosen", SyncMode.Fsync));
        var settings = new PrefixSettings(layout);
        settings.SetSync("chosen", SyncMode.Ntsync);
        settings.SetVariable("chosen", "CHANGED", "mine");
        settings.SetVariable("chosen", "REMOVED", null);
        settings.SetVariable("chosen", "UNRELATED", "kept");
        File.WriteAllText(layout.PrefixDxvkFile("chosen"), Dxvk.Version + "\n");
        File.WriteAllText(layout.PrefixUserReg("chosen"), """
            WINE REGISTRY Version 2

            [Software\\Wine\\Explorer] 1787567817
            "Desktop"="Default"

            [Software\\Wine\\Explorer\\Desktops] 1787567817
            "Default"="1920x1080"
            """);
        var library = new Library(layout, new UnusedRunner());

        var review = library.PrefixUpdateOf(library.Find("thing"))!;

        Assert.False(review.Available);
        Assert.Equal("Edited", review.State);
        Assert.Equal(
            [
                "Sync mode ntsync instead of fsync.",
                "DXVK on instead of off.",
                "Virtual desktop on instead of off.",
                "CHANGED mine instead of old.",
                "REMOVED removed instead of gone.",
            ],
            review.Edits);
        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void APrefixOnAnotherWineThanItsConfigIsEdited()
    {
        var layout = InstallSetup(SetupRecipe + "\nRunner: 9.21\n", before: OnRunner("9.21"));
        OnRunner("10.0")(layout);
        var library = new Library(layout, new UnusedRunner());

        Assert.Equal(["Wine 10.0 instead of 9.21."], library.PrefixUpdateOf(library.Find("thing"))!.Edits);
    }

    [Fact]
    public void APrefixMatchingItsConfigIsUpToDate()
    {
        var layout = InstallSetup(SetupRecipe);
        var library = new Library(layout, new UnusedRunner());

        var review = library.PrefixUpdateOf(library.Find("thing"))!;

        Assert.Equal("Up to date", review.State);
        Assert.Empty(review.Edits);
    }

    [Fact]
    public void AnUpdateTakesPrecedenceOverEdits()
    {
        var layout = InstallSetup(SetupRecipe + "\nSync: fsync\n", before: prepared =>
            new PrefixSettings(prepared).SetSync("chosen", SyncMode.Fsync));
        new PrefixSettings(layout).SetSync("chosen", SyncMode.Ntsync);
        Catalogue(("thing", SetupRecipe + "\nSync: esync\n"));
        var library = new Library(layout, new UnusedRunner());

        var review = library.PrefixUpdateOf(library.Find("thing"))!;

        Assert.Equal("Update available", review.State);
        Assert.Equal(["Sync mode ntsync instead of fsync."], review.Edits);
    }

    [Fact]
    public void AnUnchangedSetupLeavesSettingsChangedByHand()
    {
        var recipe = SetupRecipe + "\nSync: fsync\nEnv: MANAGED=recommended\n";
        var layout = InstallSetup(recipe);
        var settings = new PrefixSettings(layout);
        settings.SetSync("chosen", SyncMode.Ntsync);
        settings.SetVariable("chosen", "MANAGED", "personal");
        var library = new Library(layout, new UnusedRunner());

        var update = library.PrefixUpdateOf(library.Find("thing"))!;

        Assert.False(update.Available);
        Assert.Empty(update.Changes);
    }

    [Fact]
    public void AFamilyWithoutAReceiptIsOnItsFirstConfig()
    {
        var layout = InstallFamily();
        File.Delete(layout.PrefixSetupFile("custom"));
        File.WriteAllText(layout.PrefixWinetricksLog("custom"), "corefonts\n");
        Catalogue(("prefix-2", "Revision: 1\nWinetricks: allfonts\n"));
        var library = new Library(layout, new UnusedRunner());

        var update = library.PrefixUpdateOf(library.Find("one"))!;

        Assert.Equal("1, revision 1", update.Applied);
        Assert.Equal("2, revision 1", update.Config);
        Assert.Equal(["Install allfonts with Winetricks."], update.Changes);
        Assert.Equal(["corefonts"], update.Dropped);
    }

    [Fact]
    public void AChangedSyncRecipeReplacesACustomSyncMode()
    {
        var recipe = SetupRecipe + "\nSync: fsync\n";
        var layout = InstallSetup(recipe);
        var settings = new PrefixSettings(layout);
        settings.SetSync("chosen", SyncMode.Ntsync);
        Catalogue(("thing", recipe.Replace("fsync", "esync")));
        var library = new Library(layout, new RecordingRunner());
        var update = library.PrefixUpdateOf(library.Find("thing"))!;

        library.UpdatePrefix(update);

        Assert.Equal(SyncMode.Esync, settings.Sync("chosen"));
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
    public void ADependencyDroppedFromTheConfigIsOfferedWithNothingToInstall()
    {
        var layout = InstallSetup(SetupRecipe);
        Catalogue(("thing", SetupRecipe.Replace("Winetricks: corefonts", "")));
        var library = new Library(layout, new UnusedRunner());
        var update = library.PrefixUpdateOf(library.Find("thing"))!;

        Assert.True(update.Available);
        Assert.Empty(update.Changes);
        Assert.Equal(["corefonts"], update.Dropped);
        Assert.Contains("has changed.", update.Summary);
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
    public void ANewConfigVersionIsOfferedEvenWithNothingToInstall()
    {
        var recipe = SetupRecipe + "\nVersion: 1.2.3\n";
        var layout = InstallSetup(recipe, before: prepared =>
            File.WriteAllText(prepared.PrefixWinetricksLog("chosen"), "corefonts\n"));
        Catalogue(("thing", recipe.Replace("1.2.3", "1.3.0")));
        var recorder = new RecordingRunner();
        var library = new Library(layout, recorder);
        var update = library.PrefixUpdateOf(library.Find("thing"))!;

        Assert.True(update.Available);
        Assert.Empty(update.Changes);
        Assert.Contains("Previously version 1.2.3", update.Summary);
        Assert.NotNull(update.NothingToInstall);

        library.UpdatePrefix(update);

        Assert.Empty(recorder.Ran);
        Assert.Equal("1.3.0, revision 1", library.PrefixUpdateOf(library.Find("thing"))!.Applied);
        Assert.Empty(library.PrefixUpdates());
    }

    [Fact]
    public void ARevisionWhoseComponentsAreInstalledSaysSoAndNamesWhatItDropped()
    {
        var layout = InstallFamily();
        File.WriteAllText(layout.PrefixWinetricksLog("custom"), "corefonts\nallfonts\n");
        Catalogue(("prefix-1", "Revision: 2\nWinetricks: allfonts\n"));
        var library = new Library(layout, new UnusedRunner());
        var update = library.PrefixUpdateOf(library.Find("one"))!;

        Assert.True(update.Available);
        Assert.Empty(update.Changes);
        Assert.Equal(["allfonts"], update.Present);
        Assert.Equal(["corefonts"], update.Dropped);
        Assert.Contains("Revision 1 → 2", update.Summary);
    }

    [Fact]
    public void ARecipeChangeWithinTheSameEntryVersionStillOffersAnUpdate()
    {
        var recipe = SetupRecipe + "\nVersion: 1.2.3\n";
        var layout = InstallSetup(recipe);
        Catalogue(("thing", recipe.Replace("corefonts", "allfonts")));
        var recorder = Winetricked(layout);
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
    public void AVerbThePrefixAlreadyHasIsNotOfferedAgain()
    {
        var layout = InstallSetup(SetupRecipe);
        File.WriteAllText(layout.PrefixWinetricksLog("chosen"), "corefonts\nallfonts\n");
        Catalogue(("thing", SetupRecipe.Replace("corefonts", "corefonts, allfonts")));
        var recorder = new RecordingRunner();
        var library = new Library(layout, recorder);
        var update = library.PrefixUpdateOf(library.Find("thing"))!;

        Assert.Empty(update.Changes);
        Assert.Empty(update.Winetricks);
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
        Assert.Equal(["Install allfonts with Winetricks."], newer.Changes);
    }

    [Fact]
    public void AFamilySharesOneUpdateAcrossItsMembers()
    {
        var layout = InstallFamily();
        Catalogue(("prefix-1", FamilyConfig.Replace("Revision: 1", "Revision: 2").Replace("corefonts", "allfonts")));
        var recorder = Winetricked(layout, "custom");
        var library = new Library(layout, recorder);

        Assert.Equal(["one", "two"], library.PrefixUpdates().Keys.Order(StringComparer.Ordinal));
        var update = Assert.Single(library.PendingPrefixUpdates());
        Assert.Equal(["one", "two"], update.Members);
        Assert.Empty(update.Sharing);
        Assert.Equal("1, revision 1", update.Applied);
        Assert.Equal(["Install allfonts with Winetricks."], update.Changes);

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
        var library = new Library(layout, new RecordingRunner(arguments =>
        {
            Directory.CreateDirectory(Path.Combine(layout.PrefixPath("chosen"), "dosdevices"));

            if (arguments.FirstOrDefault() == "--unattended")
            {
                File.AppendAllLines(layout.PrefixWinetricksLog("chosen"), arguments.Skip(1));
            }
        }));

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
        var library = new Library(layout, Winetricked(layout, "custom"));
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

    private Layout OlderInstallation()
    {
        Catalogue(
            ("thing", "Name: Thing\nKind: windows\nSource: byo\nVersion: 2.0\n"),
            ("prefix-1.0", "Revision: 1\nWinetricks: corefonts\n"),
            ("prefix-2.0", "Revision: 1\nWinetricks: allfonts\n"));
        var layout = Layout();
        Directory.CreateDirectory(Path.Combine(layout.PrefixPath("chosen"), "dosdevices"));
        File.WriteAllText(layout.PrefixPluginsFile("chosen"), "thing\n");
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
        var library = new Library(layout, Winetricked(layout, prefix));
        library.Install(library.Find("thing"), prefix, installer);
        return layout;
    }

    private static RecordingRunner Winetricked(Layout layout, string prefix = "chosen", Func<IReadOnlyList<string>, int>? exits = null) =>
        new(arguments =>
        {
            if (arguments.FirstOrDefault() == "--unattended")
            {
                File.AppendAllLines(layout.PrefixWinetricksLog(prefix), arguments.Skip(1));
            }
        }, exits);
}
