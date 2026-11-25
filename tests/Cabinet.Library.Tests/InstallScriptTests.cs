using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class InstallScriptTests : IDisposable
{
    private readonly string root = TestRoot.Create("install-script");

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void NovationPlayRequestsItsZipBeforeUnpackingAnExecutable()
    {
        var layout = new Layout(root, Path.Combine(root, "runtime"), libraryDir: Repo.Path("data/library"));
        var entry = LibraryEntry.Parse("novation-play",
            File.ReadAllText(Repo.Path("data/library/novation/novation-play.yml")), "novation");
        var said = new List<string>();

        Assert.Throws<InvalidOperationException>(() =>
            new InstallScript(layout, new ProcessRunner()).Run(
                entry, Path.Combine(root, "Play Installer.exe"), Path.Combine(root, "work"),
                root, new Dictionary<string, string>(), said.Add, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(entry.InstallInstructions!, said);
        Assert.False(Directory.Exists(Path.Combine(root, "work", "unpacked")));
    }

    [Fact]
    public void SpliceInstrumentDoesNotGiveWineDescendantsTheInstallPipe()
    {
        var fdTargets = Path.Combine(root, "fd-targets");

        var work = RunSpliceInstrument($"""readlink /proc/$$/fd/1 /proc/$$/fd/2 | cat >>"{fdTargets}" """);

        Assert.Equal(
            [Path.Combine(work, "splice-instrument-installer.log")],
            File.ReadAllLines(fdTargets).Distinct());
    }

    [Fact]
    public void SpliceInstrumentFailsWhenTheInstallerLeavesNoApplication()
    {
        Assert.Throws<InvalidOperationException>(() => RunSpliceInstrument(
            """rm -f "$CABINET_PREFIX/drive_c/Program Files/Splice/Splice INSTRUMENT/Splice INSTRUMENT.exe" """));
    }

    [Fact]
    public void AProcessTheScriptLeavesRunningDoesNotHoldUpTheInstall()
    {
        var layout = new Layout(
            root,
            Path.Combine(root, "runtime"),
            libraryDir: Path.Combine(root, "library"));
        var vendor = Directory.CreateDirectory(Path.Combine(root, "library", "a-vendor")).FullName;
        var work = Path.Combine(root, "work");
        File.WriteAllText(Path.Combine(vendor, "fixture.sh"), """
            sh -c 'while [ -d "$1" ] && [ ! -e "$1/release" ]; do sleep 0.2; done' sh "$CABINET_WORK" &
            echo "the script is done"
            """);
        var entry = LibraryEntry.Parse(
            "thing", "Name: Thing\nKind: windows\nSource: byo\nScript: fixture.sh\n", "a-vendor");
        var said = new List<string>();
        var started = DateTime.UtcNow;

        new InstallScript(layout, new ProcessRunner()).Run(
            entry, "archive", work, root, new Dictionary<string, string>(), said.Add, cancellationToken: TestContext.Current.CancellationToken);
        var took = DateTime.UtcNow - started;
        File.WriteAllText(Path.Combine(work, "release"), "");

        Assert.Contains("the script is done", said);
        Assert.True(took < TimeSpan.FromSeconds(10), $"the install waited {took.TotalSeconds:0} seconds");
    }

    [Fact]
    public void APluginBoutiqueDownloadRunsSilentlyAndAPartialOneIsDropped()
    {
        var layout = new Layout(root, Path.Combine(root, "runtime"), libraryDir: Repo.Path("data/library"));
        var entry = LibraryEntry.Parse(
            "plugin-boutique-app",
            File.ReadAllText(Repo.Path("data/library/plugin-boutique/plugin-boutique-app.yml")),
            "plugin-boutique");
        var prefix = Path.Combine(root, "prefix");
        var kept = Path.Combine(prefix, ".cabinet-kept");
        var calls = Path.Combine(root, "calls");
        var wine = Path.Combine(root, "wine");
        Directory.CreateDirectory(kept);
        File.WriteAllText(Path.Combine(kept, "Product Setup.exe"), "Inno Setup");
        File.SetLastWriteTimeUtc(Path.Combine(kept, "Product Setup.exe"), DateTime.UtcNow.AddMinutes(-1));
        File.WriteAllText(Path.Combine(kept, "6975"), "partial");
        File.WriteAllText(wine, $$"""
            #!/bin/sh
            shift; printf '%s|' "$@" >>"{{calls}}"; echo >>"{{calls}}"
            """);
        File.SetUnixFileMode(wine, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        new InstallScript(layout, new ProcessRunner()).Finish(
            entry, prefix, kept,
            new Dictionary<string, string> { ["CABINET_PREFIX"] = prefix, ["WINE"] = wine },
            _ => { });

        Assert.Equal(["|", "|", "/VERYSILENT|/SUPPRESSMSGBOXES|/NORESTART|"], File.ReadAllLines(calls));
        Assert.Empty(Directory.EnumerateFileSystemEntries(kept));
    }

    [Fact]
    public void APluginBoutiqueInstallerJustDownloadedIsLeftForTheAppToStart()
    {
        var layout = new Layout(root, Path.Combine(root, "runtime"), libraryDir: Repo.Path("data/library"));
        var entry = LibraryEntry.Parse(
            "plugin-boutique-app",
            File.ReadAllText(Repo.Path("data/library/plugin-boutique/plugin-boutique-app.yml")),
            "plugin-boutique");
        var prefix = Path.Combine(root, "prefix");
        var kept = Path.Combine(prefix, ".cabinet-kept");
        var calls = Path.Combine(root, "calls");
        var listings = Path.Combine(root, "listings");
        var wine = Path.Combine(root, "wine");
        var said = new List<string>();
        Directory.CreateDirectory(kept);
        File.WriteAllText(Path.Combine(kept, "Product Setup.exe"), "Inno Setup");
        File.WriteAllText(wine, $$"""
            #!/bin/sh
            if [ "$1" = tasklist ]; then
                echo >>"{{listings}}"
                case $(wc -l <"{{listings}}") in 2 | 3) echo "Product Setup.exe      32 Console" ;; esac
                exit 0
            fi
            printf '%s|' "$@" >>"{{calls}}"; echo >>"{{calls}}"
            """);
        File.SetUnixFileMode(wine, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        new InstallScript(layout, new ProcessRunner()).Finish(
            entry, prefix, kept,
            new Dictionary<string, string> { ["CABINET_PREFIX"] = prefix, ["WINE"] = wine },
            said.Add);

        Assert.False(File.Exists(calls));
        Assert.Contains("  Plugin Boutique App is running Product Setup.exe itself, so Cabinet waits for it", said);
        Assert.Empty(Directory.EnumerateFileSystemEntries(kept));
    }

    [Fact]
    public void APluginBoutiqueInstallerTheAppAlreadyRunsIsNotStartedASecondTime()
    {
        var layout = new Layout(root, Path.Combine(root, "runtime"), libraryDir: Repo.Path("data/library"));
        var entry = LibraryEntry.Parse(
            "plugin-boutique-app",
            File.ReadAllText(Repo.Path("data/library/plugin-boutique/plugin-boutique-app.yml")),
            "plugin-boutique");
        var prefix = Path.Combine(root, "prefix");
        var kept = Path.Combine(prefix, ".cabinet-kept");
        var calls = Path.Combine(root, "calls");
        var listed = Path.Combine(root, "listed");
        var wine = Path.Combine(root, "wine");
        var said = new List<string>();
        Directory.CreateDirectory(kept);
        File.WriteAllText(Path.Combine(kept, "A_Long_Product_Name_Installer.exe"), "Inno Setup");
        File.SetLastWriteTimeUtc(
            Path.Combine(kept, "A_Long_Product_Name_Installer.exe"), DateTime.UtcNow.AddMinutes(-1));
        File.WriteAllText(wine, $$"""
            #!/bin/sh
            if [ "$1" = tasklist ]; then
                [ -f "{{listed}}" ] || echo "A_Long_Product_Name_Insta      32 Console"
                touch "{{listed}}"
                exit 0
            fi
            printf '%s|' "$@" >>"{{calls}}"; echo >>"{{calls}}"
            """);
        File.SetUnixFileMode(wine, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        new InstallScript(layout, new ProcessRunner()).Finish(
            entry, prefix, kept,
            new Dictionary<string, string> { ["CABINET_PREFIX"] = prefix, ["WINE"] = wine },
            said.Add);

        Assert.False(File.Exists(calls));
        Assert.Contains(
            "  Plugin Boutique App is running A_Long_Product_Name_Installer.exe itself, so Cabinet waits for it",
            said);
        Assert.Empty(Directory.EnumerateFileSystemEntries(kept));
    }

    [Fact]
    public void PluginBoutiqueIsPointedAtTheWatchedFolderBeforeEveryLaunch()
    {
        var layout = new Layout(root, Path.Combine(root, "runtime"), libraryDir: Repo.Path("data/library"));
        var entry = LibraryEntry.Parse(
            "plugin-boutique-app",
            File.ReadAllText(Repo.Path("data/library/plugin-boutique/plugin-boutique-app.yml")),
            "plugin-boutique");
        var prefix = Path.Combine(root, "prefix");
        var preferences = Path.Combine(
            prefix, "drive_c", "users", "someone", "AppData", "Roaming", "Plugin Boutique", "preferences.json");
        Directory.CreateDirectory(Path.Combine(prefix, "drive_c", "users", "someone"));

        new InstallScript(layout, new ProcessRunner()).Finish(
            entry, prefix, Path.Combine(prefix, ".cabinet-kept"),
            new Dictionary<string, string> { ["CABINET_PREFIX"] = prefix, ["WINE"] = "false" },
            _ => { });

        Assert.Contains(
            @"""downloadLocation"": ""C:\\users\\Public\\Downloads\\Plugin Boutique""",
            File.ReadAllText(preferences));
        Assert.True(Directory.Exists(Path.Combine(prefix, entry.Keep!)));
    }

    [Fact]
    public void PluginBoutiqueOpensItsDownloadsFolderWithoutAWindow()
    {
        var layout = new Layout(root, Path.Combine(root, "runtime"), libraryDir: Repo.Path("data/library"));
        var entry = LibraryEntry.Parse(
            "plugin-boutique-app",
            File.ReadAllText(Repo.Path("data/library/plugin-boutique/plugin-boutique-app.yml")),
            "plugin-boutique");
        var prefix = Path.Combine(root, "prefix");
        var calls = Path.Combine(root, "calls");
        var wine = Path.Combine(root, "wine");
        Directory.CreateDirectory(prefix);
        File.WriteAllText(
            Path.Combine(prefix, "system.reg"),
            "[Software\\\\Classes\\\\folder\\\\shell\\\\open\\\\ddeexec] 1795529011\n");
        File.WriteAllText(wine, $$"""
            #!/bin/sh
            printf '%s|' "$@" >>"{{calls}}"; echo >>"{{calls}}"
            """);
        File.SetUnixFileMode(wine, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        new InstallScript(layout, new ProcessRunner()).Finish(
            entry, prefix, Path.Combine(prefix, ".cabinet-kept"),
            new Dictionary<string, string> { ["CABINET_PREFIX"] = prefix, ["WINE"] = wine },
            _ => { });

        Assert.Equal(
            [
                @"reg|delete|HKLM\Software\Classes\Folder\shell\open\ddeexec|/f|",
                @"reg|add|HKLM\Software\Classes\Folder\shell\open\command|/ve|/d|C:\windows\system32\rundll32.exe|/f|",
            ],
            File.ReadAllLines(calls));
    }

    [Fact]
    public void APluginBoutiqueDownloadWhoseInstallerFailsIsSetAsideAndSaysWhere()
    {
        var layout = new Layout(root, Path.Combine(root, "runtime"), libraryDir: Repo.Path("data/library"));
        var entry = LibraryEntry.Parse(
            "plugin-boutique-app",
            File.ReadAllText(Repo.Path("data/library/plugin-boutique/plugin-boutique-app.yml")),
            "plugin-boutique");
        var prefix = Path.Combine(root, "prefix");
        var kept = Path.Combine(prefix, ".cabinet-kept");
        var downloaded = Path.Combine(prefix, entry.Keep!, "Product Setup.exe");
        var wine = Path.Combine(root, "wine");
        var said = new List<string>();
        Directory.CreateDirectory(kept);
        Directory.CreateDirectory(Path.GetDirectoryName(downloaded)!);
        File.WriteAllText(downloaded, "Inno Setup");
        File.Copy(downloaded, Path.Combine(kept, "Product Setup.exe"));
        File.SetLastWriteTimeUtc(Path.Combine(kept, "Product Setup.exe"), DateTime.UtcNow.AddMinutes(-1));
        File.WriteAllText(wine, "#!/bin/sh\nexit 3\n");
        File.SetUnixFileMode(wine, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        var failure = Assert.Throws<InvalidOperationException>(() =>
            new InstallScript(layout, new ProcessRunner()).Finish(
                entry, prefix, kept,
                new Dictionary<string, string> { ["CABINET_PREFIX"] = prefix, ["WINE"] = wine },
                said.Add));

        Assert.False(File.Exists(downloaded));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(downloaded)!, "Not installed", "Product Setup.exe")));
        Assert.Contains("  Product Setup.exe could not install silently (exit 3), so its own installer opens", said);
        Assert.Contains("  Product Setup.exe exited with 3", said);
        Assert.Equal(
            @"Product Setup.exe did not install, so it is in C:\users\Public\Downloads\Plugin Boutique\Not installed",
            failure.Message);
    }

    [Fact]
    public void APluginBoutiqueZipPlacesA64BitPluginWithItsContentAndLeavesA32BitOneOut()
    {
        var layout = new Layout(root, Path.Combine(root, "runtime"), libraryDir: Repo.Path("data/library"));
        var entry = LibraryEntry.Parse(
            "plugin-boutique-app",
            File.ReadAllText(Repo.Path("data/library/plugin-boutique/plugin-boutique-app.yml")),
            "plugin-boutique");
        var prefix = Path.Combine(root, "prefix");
        var kept = Path.Combine(prefix, ".cabinet-kept");
        var downloaded = Path.Combine(prefix, entry.Keep!, "Thing.zip");
        var unpacked = Directory.CreateDirectory(Path.Combine(root, "unpacked")).FullName;
        var tools = Directory.CreateDirectory(Path.Combine(root, "tools")).FullName;
        var vst2 = Path.Combine(prefix, "drive_c", "Program Files", "Common Files", "VST2");
        Directory.CreateDirectory(Path.Combine(unpacked, "x86"));
        File.WriteAllBytes(Path.Combine(unpacked, "Thing.dll"), PortableExecutable(0x8664));
        File.WriteAllText(Path.Combine(unpacked, "Thing.instruments.zip"), "content");
        File.WriteAllBytes(Path.Combine(unpacked, "x86", "Old Thing.dll"), PortableExecutable(0x014c));
        File.WriteAllText(Path.Combine(tools, "7z"), $$"""
            #!/bin/sh
            [ "$1" = x ] && cp -R "{{unpacked}}/." "${3#-o}"
            """);
        File.SetUnixFileMode(Path.Combine(tools, "7z"), UnixFileMode.UserRead | UnixFileMode.UserExecute);
        Directory.CreateDirectory(kept);
        Directory.CreateDirectory(Path.GetDirectoryName(downloaded)!);
        File.WriteAllText(downloaded, "zip");
        File.Copy(downloaded, Path.Combine(kept, "Thing.zip"));

        new InstallScript(layout, new ProcessRunner()).Finish(
            entry, prefix, kept,
            new Dictionary<string, string>
            {
                ["CABINET_PREFIX"] = prefix,
                ["WINE"] = "false",
                ["PATH"] = tools + ":" + Environment.GetEnvironmentVariable("PATH"),
            },
            _ => { });

        Assert.Equal(
            ["Thing.dll", "Thing.instruments.zip"],
            Directory.EnumerateFiles(vst2).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.False(File.Exists(downloaded));
    }

    [Fact]
    public void APluginBoutiqueZipWithOnlyA32BitPluginSaysSo()
    {
        var layout = new Layout(root, Path.Combine(root, "runtime"), libraryDir: Repo.Path("data/library"));
        var entry = LibraryEntry.Parse(
            "plugin-boutique-app",
            File.ReadAllText(Repo.Path("data/library/plugin-boutique/plugin-boutique-app.yml")),
            "plugin-boutique");
        var prefix = Path.Combine(root, "prefix");
        var kept = Path.Combine(prefix, ".cabinet-kept");
        var downloaded = Path.Combine(prefix, entry.Keep!, "Old.zip");
        var unpacked = Directory.CreateDirectory(Path.Combine(root, "unpacked")).FullName;
        var tools = Directory.CreateDirectory(Path.Combine(root, "tools")).FullName;
        var said = new List<string>();
        File.WriteAllBytes(
            Path.Combine(unpacked, "Old.dll"), [.. PortableExecutable(0x014c), .. "VSTPluginMain"u8]);
        File.WriteAllText(Path.Combine(tools, "7z"), $$"""
            #!/bin/sh
            [ "$1" = x ] && cp -R "{{unpacked}}/." "${3#-o}"
            """);
        File.SetUnixFileMode(Path.Combine(tools, "7z"), UnixFileMode.UserRead | UnixFileMode.UserExecute);
        Directory.CreateDirectory(kept);
        Directory.CreateDirectory(Path.GetDirectoryName(downloaded)!);
        File.WriteAllText(downloaded, "zip");
        File.Copy(downloaded, Path.Combine(kept, "Old.zip"));

        var failure = Assert.Throws<InvalidOperationException>(() =>
            new InstallScript(layout, new ProcessRunner()).Finish(
                entry, prefix, kept,
                new Dictionary<string, string>
                {
                    ["CABINET_PREFIX"] = prefix,
                    ["WINE"] = "false",
                    ["PATH"] = tools + ":" + Environment.GetEnvironmentVariable("PATH"),
                },
                said.Add));

        Assert.Contains("  Old.dll is a 32-bit plugin", said);
        Assert.Equal(
            "Old.zip holds only a 32-bit plugin, and this prefix can bridge 64-bit ones only, "
            + @"so it is in C:\users\Public\Downloads\Plugin Boutique\Not installed",
            failure.Message);
    }

    private static byte[] PortableExecutable(ushort machine)
    {
        var image = new byte[70];
        image[0x3c] = 64;
        "PE"u8.CopyTo(image.AsSpan(64));
        BitConverter.TryWriteBytes(image.AsSpan(68), machine);
        return image;
    }

    [Fact]
    public void KontaktWithOnlyItsVst3GetsTheStandInAndItsInstallFoldersRegistered()
    {
        var kontakt = Kontakt();
        File.WriteAllText(Path.Combine(kontakt.Vst3, "Kontakt 8.vst3"), "plugin");

        var said = kontakt.Recover();

        Assert.Equal("stand-in", File.ReadAllText(Path.Combine(kontakt.SysWow64, "msi.dll")));
        Assert.Equal("wine msi", File.ReadAllText(Path.Combine(kontakt.SysWow64, "msi_wine.dll")));
        Assert.True(Directory.Exists(Path.Combine(kontakt.DriveC, "Program Files", "Native Instruments", "Kontakt 8")));
        Assert.True(Directory.Exists(
            Path.Combine(kontakt.DriveC, "Program Files", "Common Files", "Native Instruments", "Kontakt 8")));
        Assert.Equal(
            [
                @"reg|add|HKCU\Software\Wine\AppDefaults\Kontakt 8 Setup PC.exe\DllOverrides|/v|msi|/d|native|/f|",
                @"reg|add|HKLM\SOFTWARE\Native Instruments\Kontakt 8|/v|InstallDir|/d|C:\Program Files\Native Instruments\Kontakt 8|/f|",
                @"reg|add|HKLM\SOFTWARE\Native Instruments\Kontakt 8|/v|ContentDir|/d|C:\Program Files\Common Files\Native Instruments\Kontakt 8|/f|",
                @"reg|add|HKLM\SOFTWARE\Native Instruments\Kontakt 8|/v|ContentVersion|/d|4.0|/f|",
                @"reg|add|HKLM\SOFTWARE\Native Instruments\Kontakt 8|/v|InstallVST364Dir|/d|C:\Program Files\Common Files\VST3|/f|",
            ],
            File.ReadAllLines(kontakt.Calls));
        Assert.Contains("Registered Kontakt 8's install folders, so Native Access can update or repair it", said);
    }

    [Fact]
    public void AKontaktAlreadyBroughtUpToDateIsLeftAlone()
    {
        var kontakt = Kontakt();
        File.WriteAllText(Path.Combine(kontakt.Vst3, "Kontakt 8.vst3"), "plugin");
        kontakt.Recover();
        File.Delete(kontakt.Calls);

        kontakt.Recover();

        Assert.False(File.Exists(kontakt.Calls));
    }

    [Fact]
    public void AKontaktInstallTheDawBlockedIsExplainedOnceNativeAccessCloses()
    {
        var kontakt = Kontakt();
        var temp = Path.Combine(kontakt.DriveC, "windows", "temp");
        Directory.CreateDirectory(temp);
        File.WriteAllText(Path.Combine(temp, "cabinet-msi.result"), "failed\n");
        File.WriteAllText(
            Path.Combine(temp, "cabinet-msi.log"),
            "cabinet: A DAW is using plugins from native-instruments, so Cabinet will not install "
            + "Kontakt 8 Setup PC.msi into native-instruments — close those plugins in your DAW and try again.\n");

        var said = kontakt.Recover();

        Assert.Equal(
            [
                "Native Access could not install Kontakt 8:",
                "cabinet: A DAW is using plugins from native-instruments, so Cabinet will not install "
                + "Kontakt 8 Setup PC.msi into native-instruments — close those plugins in your DAW and try again.",
                "Install or update it again in Native Access once that is resolved.",
            ],
            said);
        Assert.False(File.Exists(Path.Combine(temp, "cabinet-msi.result")));
    }

    [Fact]
    public void AKeptKontaktDownloadWithoutAnInstallerIsDroppedSoTheNextOpenDoesNotTryAgain()
    {
        var kontakt = Kontakt();
        var kept = Path.Combine(kontakt.Prefix, ".cabinet-kept");
        Directory.CreateDirectory(kept);
        File.WriteAllText(Path.Combine(kept, "Kontakt_8_Installer.zip"), "not an archive");
        var said = new List<string>();

        Assert.Throws<InvalidOperationException>(() => kontakt.Recover(said));

        Assert.Empty(Directory.GetFiles(kept));
        Assert.Contains(
            "The kept Kontakt 8 download held no installer; install Kontakt 8 again in Native Access", said);
    }

    [Fact]
    public void AFailedRegistryWriteSaysWhatWineReported()
    {
        var kontakt = Kontakt();
        File.Delete(kontakt.Wine);
        File.WriteAllText(kontakt.Wine, """
            #!/bin/sh
            echo "wine: could not load kernel32.dll"
            exit 1
            """);
        File.SetUnixFileMode(kontakt.Wine, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var said = new List<string>();

        Assert.Throws<InvalidOperationException>(() => kontakt.Recover(said));

        Assert.Contains("wine: could not load kernel32.dll", said);
    }

    [Fact]
    public void KompleteKontrolScansUntilItsListNamesEveryVst3()
    {
        var kontakt = KompleteKontrol();
        File.WriteAllText(Path.Combine(kontakt.Vst3, "Massive X.vst3"), "plugin");
        Directory.CreateDirectory(Path.Combine(kontakt.Vst3, "iZotope", "Ozone 12 Equalizer.vst3", "Contents"));
        File.Delete(kontakt.Wine);
        File.WriteAllText(kontakt.Wine, $$"""
            #!/bin/sh
            printf '%s|' "$@" >>"{{kontakt.Calls}}"; echo >>"{{kontakt.Calls}}"
            printf 'C:\\VST3\\Massive X.vst3 C:\\VST3\\iZotope\\Ozone 12 Equalizer.vst3' >"{{kontakt.PluginList}}"
            """);
        File.SetUnixFileMode(kontakt.Wine, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        var said = kontakt.Recover();

        Assert.Equal(
            [@"start|/min|C:\Program Files\Native Instruments\Komplete Kontrol\Komplete Kontrol.exe|"],
            File.ReadAllLines(kontakt.Calls));
        Assert.Equal(
            [
                "Komplete Kontrol is scanning its plugins, so it can host them in a DAW. This takes a few minutes.",
                "Komplete Kontrol finished scanning its plugins",
            ],
            said);
    }

    [Fact]
    public void AKompleteKontrolListNamingEveryVst3ButItselfIsNotScannedAgain()
    {
        var kontakt = KompleteKontrol();
        File.WriteAllText(Path.Combine(kontakt.Vst3, "Massive X.vst3"), "plugin");
        File.WriteAllText(kontakt.PluginList, @"C:\VST3\Massive X.vst3");

        var said = kontakt.Recover();

        Assert.False(File.Exists(kontakt.Calls));
        Assert.Empty(said);
    }

    private KontaktPrefix KompleteKontrol()
    {
        var kontakt = Kontakt();
        File.WriteAllText(Path.Combine(kontakt.SysWow64, "msi.dll"), "stand-in");
        File.WriteAllText(Path.Combine(kontakt.SysWow64, "msi_wine.dll"), "wine msi");
        File.WriteAllText(Path.Combine(kontakt.Vst3, "Komplete Kontrol.vst3"), "plugin");
        Directory.CreateDirectory(Path.GetDirectoryName(kontakt.Standalone)!);
        File.WriteAllText(kontakt.Standalone, "app");
        Directory.CreateDirectory(Path.GetDirectoryName(kontakt.PluginList)!);
        return kontakt;
    }

    private KontaktPrefix Kontakt()
    {
        var library = Path.Combine(root, "library", "native-instruments");
        Directory.CreateDirectory(library);
        foreach (var script in new[] { "recover.sh", "kontakt-8.sh", "komplete-kontrol.sh" })
        {
            File.Copy(Repo.Path($"data/library/native-instruments/{script}"), Path.Combine(library, script));
        }

        File.Copy(Repo.Path("data/library/native-instruments/native-access.yml"), Path.Combine(library, "native-access.yml"));
        File.WriteAllText(Path.Combine(library, "msi.dll"), "stand-in");

        var runner = Path.Combine(root, "runner");
        var wineMsi = Path.Combine(runner, "lib", "wine", "i386-windows", "msi.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(wineMsi)!);
        File.WriteAllText(wineMsi, "wine msi");

        var prefix = Path.Combine(root, "prefix");
        var kontakt = new KontaktPrefix(
            new Layout(root, Path.Combine(root, "runtime"), libraryDir: Path.Combine(root, "library")),
            prefix,
            Path.Combine(runner, "bin", "wine"),
            Path.Combine(root, "calls"));
        Directory.CreateDirectory(kontakt.SysWow64);
        Directory.CreateDirectory(kontakt.Vst3);
        Directory.CreateDirectory(Path.GetDirectoryName(kontakt.Wine)!);
        File.WriteAllText(kontakt.Wine, $$"""
            #!/bin/sh
            printf '%s|' "$@" >>"{{kontakt.Calls}}"; echo >>"{{kontakt.Calls}}"
            """);
        File.SetUnixFileMode(kontakt.Wine, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        return kontakt;
    }

    private sealed record KontaktPrefix(Layout Layout, string Prefix, string Wine, string Calls)
    {
        public string DriveC => Path.Combine(Prefix, "drive_c");

        public string SysWow64 => Path.Combine(DriveC, "windows", "syswow64");

        public string Vst3 => Path.Combine(DriveC, "Program Files", "Common Files", "VST3");

        public string Standalone => Path.Combine(
            DriveC, "Program Files", "Native Instruments", "Komplete Kontrol", "Komplete Kontrol.exe");

        public string PluginList => Path.Combine(
            DriveC, "users", "someone", "AppData", "Local", "Native Instruments", "Komplete Kontrol", "Plugin_kk3.data");

        public List<string> Recover() => Recover([]);

        public List<string> Recover(List<string> said)
        {
            var entry = LibraryEntry.Parse(
                "native-access",
                File.ReadAllText(Path.Combine(Layout.LibraryDir, "native-instruments", "native-access.yml")),
                "native-instruments");

            new InstallScript(Layout, new ProcessRunner()).Finish(
                entry,
                Prefix,
                Path.Combine(Prefix, ".cabinet-kept"),
                new Dictionary<string, string> { ["CABINET_PREFIX"] = Prefix, ["WINE"] = Wine },
                said.Add);

            return said;
        }
    }

    private string RunSpliceInstrument(string record) => RunScript(
        "splice-instrument",
        "splice",
        "Name: Splice INSTRUMENT\nKind: windows\nSource: rolling\n"
        + "Url: https://example.invalid/installer.exe\nScript: splice-instrument.sh\n",
        """
        mkdir -p "$CABINET_PREFIX/drive_c/Program Files/Common Files/VST3/Splice/Splice INSTRUMENT.vst3" \
            "$CABINET_PREFIX/drive_c/Program Files/Splice/Splice INSTRUMENT"
        touch "$CABINET_PREFIX/drive_c/Program Files/Splice/Splice INSTRUMENT/Splice INSTRUMENT.exe"
        """,
        record);

    private string RunScript(string id, string vendor, string yaml, string install, string record)
    {
        var layout = new Layout(
            root,
            Path.Combine(root, "runtime"),
            libraryDir: Repo.Path("data/library"));
        var prefix = Path.Combine(root, "prefix");
        var work = Path.Combine(root, "work");
        var wine = Path.Combine(root, "wine");

        Directory.CreateDirectory(prefix);
        File.WriteAllText(wine, $$"""
            #!/bin/sh
            {{install}}
            {{record}}
            """);
        File.SetUnixFileMode(wine, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        var entry = LibraryEntry.Parse(id, yaml, vendor);
        var variables = new Dictionary<string, string>
        {
            ["CABINET_PREFIX"] = prefix,
            ["WINE"] = wine,
        };

        new InstallScript(layout, new ProcessRunner()).Run(
            entry, Path.Combine(root, "installer.exe"), work, prefix, variables, onOutput: null);

        return work;
    }
}
