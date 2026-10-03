using Cabinet.Core;

namespace Cabinet.Core.Tests;

public partial class LibraryTests
{
    [Fact]
    public void InstallationInstructionsAreOptionalAndJoinWrappedLines()
    {
        var entry = LibraryEntry.Parse("thing", """
            Name: Thing
            Kind: windows
            Source: byo
            InstallInstructions:
              Choose the complete
              Windows installer ZIP.
            """);

        Assert.Equal("Choose the complete Windows installer ZIP.", entry.InstallInstructions);
        Assert.Null(LibraryEntry.Parse("thing", "Name: Thing\nKind: windows\nSource: byo\n")
            .InstallInstructions);
    }

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
        Assert.Equal(["corefonts", "vcrun2022"], entry.Winetricks);
        Assert.Equal("wbemprox=n", entry.Env["WINEDLLOVERRIDES"]);
        Assert.Null(entry.Script);
        Assert.Null(entry.Launch);
        Assert.Null(entry.LaunchService);
        Assert.Empty(entry.LaunchArgs);
        Assert.Null(entry.Keep);
        Assert.Null(entry.Recover);
        Assert.Null(entry.Data);
        Assert.Equal("Surge Synth Team", entry.Developer);
        Assert.Equal("1.3.4", entry.Version);
        Assert.Equal("GPL-3.0", entry.Licence);
        Assert.Equal("Free and open source, so there is no key and no account.", entry.Licensing);
        Assert.Equal(["VST3", "CLAP", "LV2"], entry.Formats);
        Assert.Equal(
            [
                "Three oscillators per scene, twelve filter types and a modulation matrix.",
                "Open sourced in 2018.",
            ],
            entry.Description);
    }

    [Fact]
    public void InstallingSaysWhoseTermsItAccepts()
    {
        Assert.Equal(
            "Cabinet installs Surge XT without showing you its licence, so installing it accepts "
            + "Surge Synth Team's terms on your behalf. Winetricks accepts the licences of "
            + "corefonts, vcrun2022 as well.",
            LibraryEntry.Parse("surge-xt", SurgeXt).Consent);
    }

    [Fact]
    public void AnEntryWithNoDeveloperStillSaysItsTermsAreAccepted()
    {
        Assert.Equal(
            "Cabinet installs Gadget without showing you its licence, so installing it accepts "
            + "its developer's terms on your behalf.",
            LibraryEntry.Parse("gadget", "Name: Gadget\nKind: windows\nSource: byo\n").Consent);
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
        var entry = LibraryEntry.Parse("gadget", "Name: Gadget\nKind: windows\nSource: byo\n");

        Assert.Null(entry.Developer);
        Assert.Empty(entry.Formats);
        Assert.Empty(entry.Description);
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
    public void AnAppCabinetOpensKeepsTheWindowsPathItWasGiven()
    {
        var entry = LibraryEntry.Parse(
            "thing",
            "Name: Thing\nKind: windows\nSource: byo\n"
            + @"Launch: C:\Program Files (x86)\Thing\Thing.exe" + "\n");

        Assert.Equal(@"C:\Program Files (x86)\Thing\Thing.exe", entry.Launch);
    }

    [Fact]
    public void AnAppCanNameAServiceToStartBeforeItOpens()
    {
        var entry = LibraryEntry.Parse(
            "thing",
            "Name: Thing\nKind: windows\nSource: byo\n"
            + @"Launch: C:\Program Files\Thing\Thing.exe" + "\n"
            + "LaunchService: ThingService\n");

        Assert.Equal("ThingService", entry.LaunchService);
    }

    [Fact]
    public void AServiceWithoutAnAppIsRefused()
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => LibraryEntry.Parse(
            "thing",
            "Name: Thing\nKind: windows\nSource: byo\nLaunchService: ThingService\n"));

        Assert.Contains("LaunchService but no Launch", thrown.Message);
    }

    [Fact]
    public void AnAppCanCarryTheArgumentsItOpensWith()
    {
        var entry = LibraryEntry.Parse(
            "thing",
            "Name: Thing\nKind: windows\nSource: byo\n"
            + @"Launch: C:\Program Files\Thing\Thing.exe" + "\n"
            + "LaunchArgs:\n  --disable-gpu\n  --disable-gpu-compositing\n");

        Assert.Equal(["--disable-gpu", "--disable-gpu-compositing"], entry.LaunchArgs);
    }

    [Fact]
    public void OneArgumentCanSitOnTheLaunchArgsLine()
    {
        var entry = LibraryEntry.Parse(
            "thing",
            "Name: Thing\nKind: windows\nSource: byo\n"
            + @"Launch: C:\Program Files\Thing\Thing.exe" + "\n"
            + "LaunchArgs: --disable-gpu\n");

        Assert.Equal(["--disable-gpu"], entry.LaunchArgs);
    }

    [Fact]
    public void ArgumentsWithoutAnAppAreRefused()
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => LibraryEntry.Parse(
            "thing",
            "Name: Thing\nKind: windows\nSource: byo\nLaunchArgs: --disable-gpu\n"));

        Assert.Contains("LaunchArgs but no Launch", thrown.Message);
    }

    [Fact]
    public void AnAppCanClaimTheLinksItRegisters()
    {
        var entry = LibraryEntry.Parse("thing", Linked);

        Assert.Equal("thingmanager", entry.Scheme);
    }

    [Fact]
    public void ASchemeWithoutAnAppIsRefused()
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => LibraryEntry.Parse(
            "thing", "Name: Thing\nKind: windows\nSource: byo\nScheme: thingmanager\n"));

        Assert.Contains("Scheme but no Launch", thrown.Message);
    }

    [Theory]
    [InlineData("thingmanager://")]
    [InlineData("ThingManager")]
    [InlineData("1thing")]
    [InlineData("thing manager")]
    [InlineData("https")]
    [InlineData("file")]
    public void ASchemeThatIsNotTheAppsOwnIsRefused(string scheme)
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => LibraryEntry.Parse(
            "thing",
            "Name: Thing\nKind: windows\nSource: byo\n"
            + @"Launch: C:\Program Files\Thing\Thing.exe" + "\n"
            + $"Scheme: {scheme}\n"));

        Assert.Contains($"Scheme: {scheme}", thrown.Message);
    }

    [Fact]
    public void AnAppCanNameTheHelperItLeavesRunning()
    {
        var entry = LibraryEntry.Parse("thing", Linked + "LaunchHelper: ThingHelper.exe\n");

        Assert.Equal("ThingHelper.exe", entry.LaunchHelper);
    }

    [Fact]
    public void AHelperWithoutAnAppIsRefused()
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => LibraryEntry.Parse(
            "thing", "Name: Thing\nKind: windows\nSource: byo\nLaunchHelper: ThingHelper.exe\n"));

        Assert.Contains("LaunchHelper but no Launch", thrown.Message);
    }

    [Theory]
    [InlineData(@"C:\Program Files\Thing\ThingHelper.exe")]
    [InlineData("helpers/ThingHelper.exe")]
    [InlineData("ThingHelper")]
    [InlineData("*.exe")]
    [InlineData("Thing?.exe")]
    [InlineData(".exe")]
    public void AHelperThatIsNotAnExeNameIsRefused(string helper)
    {
        var thrown = Assert.Throws<InvalidOperationException>(
            () => LibraryEntry.Parse("thing", Linked + $"LaunchHelper: {helper}\n"));

        Assert.Contains($"LaunchHelper: {helper}", thrown.Message);
    }

    [Theory]
    [InlineData("Thing.exe")]
    [InlineData("/opt/thing/thing")]
    [InlineData("C:/Program Files/Thing/Thing.exe")]
    [InlineData(@"\Program Files\Thing\Thing.exe")]
    public void ALaunchThatIsNotAWindowsPathIsRefused(string path)
    {
        var thrown = Assert.Throws<InvalidOperationException>(
            () => LibraryEntry.Parse(
                "thing", $"Name: Thing\nKind: windows\nSource: byo\nLaunch: {path}\n"));

        Assert.Contains(path, thrown.Message);
    }

    [Fact]
    public void ANativePluginIsRefusedALaunchBecauseTheDawLoadsItItself()
    {
        Assert.Throws<InvalidOperationException>(
            () => LibraryEntry.Parse(
                "thing",
                "Name: Thing\nKind: native\nSource: byo\n"
                + @"Launch: C:\Thing\Thing.exe" + "\n"));
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
        var entry = LibraryEntry.Parse("gadget", "Name: Gadget\nKind: windows\nSource: byo\n");

        Assert.Equal("gadget", entry.Prefix);
        Assert.Equal(SyncMode.System, entry.Sync);
        Assert.False(entry.Dxvk);
        Assert.Empty(entry.Winetricks);
        Assert.Null(entry.Runner);
    }

    [Fact]
    public void ANativeEntryCannotCarryWinetricksDependencies()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => LibraryEntry.Parse("thing", """
            Name: Thing
            Kind: native
            Source: byo
            Winetricks: corefonts
            """));

        Assert.Contains("carries Winetricks", refused.Message);
    }

    [Theory]
    [InlineData("--unattended")]
    [InlineData("core fonts")]
    [InlineData("corefonts!")]
    public void AWinetricksOptionIsRefused(string verb)
    {
        Assert.Throws<InvalidOperationException>(() => LibraryEntry.Parse(
            "thing", $"Name: Thing\nKind: windows\nSource: byo\nWinetricks: {verb}\n"));
    }

    [Fact]
    public void DuplicateWinetricksDependenciesAreRefused()
    {
        Assert.Throws<InvalidOperationException>(() => LibraryEntry.Parse(
            "thing",
            "Name: Thing\nKind: windows\nSource: byo\n"
            + "Winetricks: corefonts, COREfonts\n"));
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
    public void ARollingEntryIsTheOneKindWithNoChecksum()
    {
        var entry = LibraryEntry.Parse("thing", """
            Name: Thing
            Kind: windows
            Source: rolling
            Url: https://example.invalid/thing.exe
            """);

        Assert.Equal(PluginSource.Rolling, entry.Source);
        Assert.Null(entry.Sha256);
    }

    [Fact]
    public void ARollingEntryCarryingAChecksumIsRefused()
    {
        var refused = Assert.Throws<InvalidOperationException>(
            () => LibraryEntry.Parse("thing", $"""
                Name: Thing
                Kind: windows
                Source: rolling
                Url: https://example.invalid/thing.exe
                Sha256: {Zeros}
                """));

        Assert.Contains("is rolling and carries Sha256", refused.Message);
    }

    [Fact]
    public void ARollingEntryWithNothingToDownloadIsRefused()
    {
        var refused = Assert.Throws<InvalidOperationException>(
            () => LibraryEntry.Parse("thing", "Name: Thing\nKind: windows\nSource: rolling\n"));

        Assert.Equal("thing.yml says Source: rolling but has no Url", refused.Message);
    }

    [Fact]
    public void AByoEntryMayOfferAPinnedWindowsDemo()
    {
        var entry = LibraryEntry.Parse("thing", $"""
            Name: Thing
            Kind: windows
            Source: byo
            DemoUrl: https://example.invalid/thing-demo.exe
            DemoSha256: {Zeros}
            """);

        Assert.Equal(PluginSource.Byo, entry.Source);
        Assert.Null(entry.Url);
        Assert.Equal("https://example.invalid/thing-demo.exe", entry.DemoUrl);
        Assert.Equal(Zeros, entry.DemoSha256);
    }

    [Fact]
    public void AByoDemoWithoutAChecksumIsRefused()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => LibraryEntry.Parse("thing", """
            Name: Thing
            Kind: windows
            Source: byo
            DemoUrl: https://example.invalid/thing.exe
            """));

        Assert.Contains("has a DemoUrl but no DemoSha256", refused.Message);
    }

    [Fact]
    public void AByoChecksumWithoutADemoIsRefused()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => LibraryEntry.Parse("thing", $"""
            Name: Thing
            Kind: windows
            Source: byo
            DemoSha256: {Zeros}
            """));

        Assert.Contains("carries DemoSha256 but has no DemoUrl", refused.Message);
    }

    [Fact]
    public void ADemoUrlIsRefusedOutsideAByoEntry()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => LibraryEntry.Parse("thing", $"""
            Name: Thing
            Kind: windows
            Source: download
            Url: https://example.invalid/thing.exe
            Sha256: {Zeros}
            DemoUrl: https://example.invalid/thing-demo.exe
            DemoSha256: {Zeros}
            """));

        Assert.Contains("is not Source: byo", refused.Message);
    }

    [Fact]
    public void ANativeEntryCarriesADemoToo()
    {
        var entry = LibraryEntry.Parse("thing", $"""
            Name: Thing
            Kind: native
            Source: byo
            DemoUrl: https://example.invalid/thing.zip
            DemoSha256: {Zeros}
            """);

        Assert.Equal("https://example.invalid/thing.zip", entry.DemoUrl);
    }

    [Fact]
    public void ARegularUrlIsRefusedOnAByoEntry()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => LibraryEntry.Parse("thing", $"""
            Name: Thing
            Kind: windows
            Source: byo
            Url: https://example.invalid/thing.exe
            Sha256: {Zeros}
            """));

        Assert.Contains("use DemoUrl", refused.Message);
    }

    [Fact]
    public void ARegularChecksumIsRefusedOnAByoEntry()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => LibraryEntry.Parse("thing", $"""
            Name: Thing
            Kind: windows
            Source: byo
            Sha256: {Zeros}
            """));

        Assert.Contains("use DemoSha256", refused.Message);
    }

    [Fact]
    public void ANativeEntryMayCarryALoginPageInsteadOfADownload()
    {
        var entry = LibraryEntry.Parse(
            "vital",
            "Name: Vital\nKind: native\nSource: byo\nAccount: https://account.vital.audio\n");

        Assert.Equal(PluginSource.Byo, entry.Source);
        Assert.Equal("https://account.vital.audio", entry.Account);
        Assert.Null(entry.Url);
    }

    [Fact]
    public void ALoginPageOnAPluginCabinetDownloadsItselfIsRefused()
    {
        var refused = Assert.Throws<InvalidOperationException>(
            () => LibraryEntry.Parse("thing", $"""
                Name: Thing
                Kind: native
                Url: https://example.invalid/thing.zip
                Sha256: {Zeros}
                Account: https://example.invalid/login
                """));

        Assert.Contains("carries Account but Cabinet downloads it", refused.Message);
    }

    [Fact]
    public void ANativeEntryCarryingAPrefixIsRefused()
    {
        var refused = Assert.Throws<InvalidOperationException>(
            () => LibraryEntry.Parse("vital", "Name: Vital\nKind: native\nSource: byo\nPrefix: v\n"));

        Assert.Contains("is native and carries Prefix", refused.Message);
    }

    [Fact]
    public void ANativeEntryCarryingADesktopIsRefused()
    {
        var refused = Assert.Throws<InvalidOperationException>(
            () => LibraryEntry.Parse(
                "vital", "Name: Vital\nKind: native\nSource: byo\nDesktop: true\n"));

        Assert.Contains("is native and carries Desktop", refused.Message);
    }

    [Fact]
    public void ANativeEntryCarryingAnEnvironmentIsRefused()
    {
        var refused = Assert.Throws<InvalidOperationException>(
            () => LibraryEntry.Parse(
                "vital", "Name: Vital\nKind: native\nSource: byo\nEnv: WINEDEBUG=-all\n"));

        Assert.Contains("is native and carries Env", refused.Message);
    }

    [Fact]
    public void AnEnvironmentBlockCarriesAVariableALine()
    {
        var entry = LibraryEntry.Parse(
            "helix",
            "Name: Helix\nKind: windows\nSource: byo\n"
            + "Env:\n  WINEDLLOVERRIDES=*wbemprox,*wbemcomn=n\n  PULSE_LATENCY_MSEC=60\n");

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["WINEDLLOVERRIDES"] = "*wbemprox,*wbemcomn=n",
                ["PULSE_LATENCY_MSEC"] = "60",
            },
            entry.Env);
    }

    [Fact]
    public void AnEnvironmentLineThatIsNotAnAssignmentIsRefused()
    {
        foreach (var wrong in new[] { "WINEDEBUG", "=orphan" })
        {
            var refused = Assert.Throws<InvalidOperationException>(
                () => LibraryEntry.Parse(
                    "helix", $"Name: Helix\nKind: windows\nSource: byo\nEnv: {wrong}\n"));

            Assert.Contains("one KEY=VALUE a line", refused.Message);
        }
    }

    [Fact]
    public void AnEntryIsRefusedTheVariablesCabinetPinsItself()
    {
        foreach (var owned in PrefixSettings.Owned)
        {
            var refused = Assert.Throws<InvalidOperationException>(
                () => LibraryEntry.Parse(
                    "helix", $"Name: Helix\nKind: windows\nSource: byo\nEnv: {owned}=/x\n"));

            Assert.Contains($"sets {owned}, which Cabinet sets itself", refused.Message);
        }
    }

    [Fact]
    public void ARelinkBlockCarriesASonameALine()
    {
        var entry = LibraryEntry.Parse(
            "vital",
            "Name: Vital\nKind: native\nSource: byo\n"
            + "Relink:\n  libcurl-gnutls.so.4 = libcurl.so.4\n  libssl.so.3 = libtls.so.3\n");

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["libcurl-gnutls.so.4"] = "libcurl.so.4",
                ["libssl.so.3"] = "libtls.so.3",
            },
            entry.Relink);
    }

    [Fact]
    public void ARelinkLineThatNamesOnlyOneSonameIsRefused()
    {
        foreach (var wrong in new[] { "libcurl-gnutls.so.4", "= libcurl.so.4", "libssl.so.3 =" })
        {
            var refused = Assert.Throws<InvalidOperationException>(
                () => LibraryEntry.Parse(
                    "vital", $"Name: Vital\nKind: native\nSource: byo\nRelink: {wrong}\n"));

            Assert.Contains("one OLD.so.N = NEW.so.N a line", refused.Message);
        }
    }

    [Fact]
    public void ARelinkToALongerSonameIsRefused()
    {
        var refused = Assert.Throws<InvalidOperationException>(
            () => LibraryEntry.Parse(
                "vital",
                "Name: Vital\nKind: native\nSource: byo\n"
                + "Relink: libcurl.so.4 = libcurl-gnutls.so.4\n"));

        Assert.Contains("a string table cannot grow in place", refused.Message);
    }

    [Fact]
    public void AWindowsEntryCarryingARelinkIsRefused()
    {
        var refused = Assert.Throws<InvalidOperationException>(
            () => LibraryEntry.Parse(
                "helix",
                "Name: Helix\nKind: windows\nSource: byo\n"
                + "Relink: libcurl-gnutls.so.4 = libcurl.so.4\n"));

        Assert.Contains("is a Windows plugin and carries Relink", refused.Message);
    }

    [Fact]
    public void ADesktopSettingIsReadAsOnOrOff()
    {
        Assert.True(LibraryEntry.Parse(
            "thing", "Name: Thing\nKind: windows\nSource: byo\nDesktop: true\n").Desktop);
        Assert.False(LibraryEntry.Parse(
            "thing", "Name: Thing\nKind: windows\nSource: byo\nDesktop: false\n").Desktop);
    }

    [Fact]
    public void ACatalogueEntryWhosePrefixIsNotANameIsRefused()
    {
        Assert.Contains(
            "has Prefix: ../escape",
            Assert.Throws<InvalidOperationException>(() => LibraryEntry.Parse(
                "thing", "Name: Thing\nKind: windows\nSource: byo\nPrefix: ../escape\n")).Message);
    }

    [Fact]
    public void AnEntryWithALaunchIsAManagerAndNamesItsExecutable()
    {
        var entry = Manager();

        Assert.True(entry.Manager);
        Assert.Equal("Thing.exe", entry.LaunchExe);
    }

    [Fact]
    public void APluginIsNoManagerAndNamesNoExecutable()
    {
        var entry = LibraryEntry.Parse("thing", SurgeXt);

        Assert.False(entry.Manager);
        Assert.Null(entry.LaunchExe);
    }

    [Fact]
    public void AFilterOfNothingMatchesEverything()
    {
        Assert.True(new LibraryFilter().Matches(LibraryEntry.Parse("surge-xt", SurgeXt), false));
        Assert.True(new LibraryFilter().Matches(Native("aalto"), true));
    }

    [Fact]
    public void ASearchTermReadsTheNameTheDeveloperAndTheSummary()
    {
        var entry = LibraryEntry.Parse("surge-xt", SurgeXt);

        Assert.True(new LibraryFilter(Search: "surge").Matches(entry, false));
        Assert.True(new LibraryFilter(Search: "SYNTH TEAM").Matches(entry, false));
        Assert.True(new LibraryFilter(Search: "hybrid").Matches(entry, false));
        Assert.False(new LibraryFilter(Search: "reverb").Matches(entry, false));
    }

    [Fact]
    public void EverySearchTermHasToMatch()
    {
        var entry = LibraryEntry.Parse("surge-xt", SurgeXt);

        Assert.True(new LibraryFilter(Search: "surge  synthesizer").Matches(entry, false));
        Assert.False(new LibraryFilter(Search: "surge reverb").Matches(entry, false));
    }

    [Fact]
    public void CategoryDeveloperKindAndInstalledEachNarrow()
    {
        var entry = LibraryEntry.Parse("surge-xt", SurgeXt);

        Assert.True(new LibraryFilter(Category: "synth").Matches(entry, true));
        Assert.False(new LibraryFilter(Category: "Effect").Matches(entry, true));

        Assert.True(new LibraryFilter(Developer: "Surge Synth Team").Matches(entry, true));
        Assert.False(new LibraryFilter(Developer: "u-he").Matches(entry, true));

        Assert.True(new LibraryFilter(Kind: PluginKind.Windows).Matches(entry, true));
        Assert.False(new LibraryFilter(Kind: PluginKind.Native).Matches(entry, true));

        Assert.True(new LibraryFilter(Installed: true).Matches(entry, true));
        Assert.False(new LibraryFilter(Installed: true).Matches(entry, false));
        Assert.True(new LibraryFilter(Installed: false).Matches(entry, false));
    }

    [Fact]
    public void TheCategoriesAndDevelopersOnOfferAreDistinctAndSorted()
    {
        LibraryEntry[] entries =
        [
            LibraryEntry.Parse("surge-xt", SurgeXt),
            Manager(),
            Native("aalto"),
        ];

        Assert.Equal(["Plugin", "Synth"], Library.Categories(entries));
        Assert.Equal(["Surge Synth Team"], Library.Developers(entries));
    }
}
