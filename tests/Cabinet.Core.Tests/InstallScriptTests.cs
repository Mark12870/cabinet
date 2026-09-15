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
    public void SpliceInstrumentFailsWhenTheInstallerLeavesNoApplication()
    {
        Assert.Throws<InvalidOperationException>(() => RunSpliceInstrument(
            """rm -f "$CABINET_PREFIX/drive_c/Program Files/Splice/Splice INSTRUMENT/Splice INSTRUMENT.exe" """));
    }

    private string RunSpliceInstrument(string record)
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
            mkdir -p "$CABINET_PREFIX/drive_c/Program Files/Common Files/VST3/Splice/Splice INSTRUMENT.vst3" \
                "$CABINET_PREFIX/drive_c/Program Files/Splice/Splice INSTRUMENT"
            touch "$CABINET_PREFIX/drive_c/Program Files/Splice/Splice INSTRUMENT/Splice INSTRUMENT.exe"
            {{record}}
            """);
        File.SetUnixFileMode(wine, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        var entry = LibraryEntry.Parse(
            "splice-instrument",
            "Name: Splice INSTRUMENT\nKind: windows\nSource: rolling\n"
            + "Url: https://example.invalid/installer.exe\nScript: splice-instrument.sh\n",
            vendor: "splice");
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
