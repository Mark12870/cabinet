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
                root, new Dictionary<string, string>(), said.Add));

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
    public void SpliceInstrumentHandsWebView2ItsFlagsThroughTheMachinePolicy()
    {
        var calls = Path.Combine(root, "calls");

        RunSpliceInstrument($"""printf '%s|' "$@" >>"{calls}"; echo >>"{calls}" """);

        foreach (var host in new[] { "yabridge-host.exe", "Splice INSTRUMENT.exe" })
        {
            Assert.Contains(
                "reg|add|HKLM\\Software\\Policies\\Microsoft\\Edge\\WebView2\\AdditionalBrowserArguments|"
                + $"/v|{host}|/d|--no-sandbox --disable-gpu-sandbox --disable-gpu --disable-gpu-compositing "
                + "--in-process-gpu|/f|",
                File.ReadAllLines(calls));
        }
    }

    [Fact]
    public void SinePlayerHandsWebView2ItsFlagsThroughTheMachinePolicy()
    {
        var calls = Path.Combine(root, "calls");

        RunSinePlayer($"""printf '%s|' "$@" >>"{calls}"; echo >>"{calls}" """);

        foreach (var host in new[] { "yabridge-host.exe", "SINE Player.exe" })
        {
            Assert.Contains(
                "reg|add|HKLM\\Software\\Policies\\Microsoft\\Edge\\WebView2\\AdditionalBrowserArguments|"
                + $"/v|{host}|/d|--no-sandbox --disable-gpu-sandbox --disable-gpu --disable-gpu-compositing "
                + "--in-process-gpu|/f|",
                File.ReadAllLines(calls));
        }
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
            entry, "archive", work, root, new Dictionary<string, string>(), said.Add);
        var took = DateTime.UtcNow - started;
        File.WriteAllText(Path.Combine(work, "release"), "");

        Assert.Contains("the script is done", said);
        Assert.True(took < TimeSpan.FromSeconds(10), $"the install waited {took.TotalSeconds:0} seconds");
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

    private KontaktPrefix Kontakt()
    {
        var library = Path.Combine(root, "library", "native-instruments");
        Directory.CreateDirectory(library);
        File.Copy(Repo.Path("data/library/native-instruments/kontakt-8.sh"), Path.Combine(library, "kontakt-8.sh"));
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

        public List<string> Recover() => Recover([]);

        public List<string> Recover(List<string> said)
        {
            var entry = LibraryEntry.Parse(
                "native-access",
                File.ReadAllText(Path.Combine(Layout.LibraryDir, "native-instruments", "native-access.yml")),
                "native-instruments");

            new InstallScript(Layout, new ProcessRunner()).Recover(
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

    private string RunSinePlayer(string record) => RunScript(
        "sine-player",
        "orchestral-tools",
        "Name: SINEplayer\nKind: windows\nSource: rolling\n"
        + "Url: https://example.invalid/installer.exe\nScript: sine-player.sh\n",
        """
        drive="$CABINET_PREFIX/drive_c/Program Files"
        mkdir -p "$drive/Common Files/VST3/SINE Player.vst3/Contents/x86_64-win" "$drive/VstPlugins" \
            "$drive/SINE Player"
        touch "$drive/Common Files/VST3/SINE Player.vst3/Contents/x86_64-win/SINE Player.vst3" \
            "$drive/VstPlugins/SINE Player.dll" "$drive/SINE Player/SINE Player.exe"
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
