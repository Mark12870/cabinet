using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class InstallScriptTests : IDisposable
{
    private readonly string root = TestRoot.Create("install-script");

    public void Dispose() => Directory.Delete(root, recursive: true);

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
