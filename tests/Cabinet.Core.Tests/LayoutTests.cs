using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class LayoutTests
{
    private static readonly Layout Layout = new("/home/u", "/run/user/1000");

    [Fact]
    public void HostPathsIgnoreXdgDataHome()
    {
        // Cabinet runs inside its own Flatpak, where XDG_DATA_HOME points at
        // ~/.var/app/io.github.mark12870.cabinet/data. Exporting there would put the
        // DAW-side halves somewhere no DAW looks.
        Assert.Equal("/home/u/.local/share/yabridge", Layout.YabridgeDir);
        Assert.Equal("/home/u/.local/share/cabinet/prefixes", Layout.PrefixesDir);
    }

    /// <summary>
    /// The one path that is deliberately sandbox-local: yabridgectl searches its own
    /// XDG_DATA_HOME, so `setup` links that at the host directory it exported to.
    /// </summary>
    [Fact]
    public void YabridgectlIsPointedAtTheExportedFilesFromInsideTheSandbox()
    {
        var layout = new Layout("/home/u", "/run/user/1000", "/home/u/.var/app/cab/data");

        Assert.Equal("/home/u/.var/app/cab/data/yabridge", layout.SandboxYabridgeLink);
        Assert.NotEqual(layout.YabridgeDir, layout.SandboxYabridgeLink);
    }

    [Fact]
    public void AFlatpakDawLooksForYabridgeInItsOwnDataDirectory()
    {
        Assert.Equal(
            "/home/u/.var/app/fm.reaper.Reaper/data/yabridge",
            Layout.DawYabridgeLink("fm.reaper.Reaper"));
    }

    [Fact]
    public void TheSocketDirectoryIsUnderTheRuntimeDirectory()
    {
        // Same path inside and outside every sandbox, which is what makes it shareable.
        Assert.Equal("/run/user/1000/yabridge", Layout.SocketDir);
    }

    [Fact]
    public void PrefixesGetTheConventionalWindowsVst3Directory()
    {
        Assert.Equal(
            "/home/u/.local/share/cabinet/prefixes/serum/drive_c/Program Files/Common Files/VST3",
            Layout.PrefixVst3Dir("serum"));
    }

    /// <summary>
    /// The 32-bit half is the point: a 32-bit installer writes under
    /// `Program Files (x86)`, and registering only the 64-bit locations leaves those
    /// plugins unbridged with no error anywhere.
    /// </summary>
    [Fact]
    public void PluginDirectoriesCoverBothBitnesses()
    {
        var prefix = "/home/u/.local/share/cabinet/prefixes/serum/drive_c";

        Assert.Equal(
            [
                $"{prefix}/Program Files/Common Files/VST3",
                $"{prefix}/Program Files/Common Files/CLAP",
                $"{prefix}/Program Files/VstPlugins",
                $"{prefix}/Program Files (x86)/Common Files/VST3",
                $"{prefix}/Program Files (x86)/Common Files/CLAP",
                $"{prefix}/Program Files (x86)/VstPlugins",
            ],
            Layout.PrefixPluginDirs("serum"));
    }

    [Fact]
    public void TheUnpackHereDirectoryIsOneOfTheRegisteredOnes()
    {
        Assert.Contains(Layout.PrefixVst3Dir("serum"), Layout.PrefixPluginDirs("serum"));
    }
}
