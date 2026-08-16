using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class LayoutTests
{
    private static readonly Layout Layout = new("/home/u", "/run/user/1000");

    /// <summary>
    /// Everything Cabinet owns stays inside its own Flatpak directory — the point of
    /// the layout, and what stops `cabinet` from scattering files across $HOME.
    /// </summary>
    [Fact]
    public void EverythingCabinetOwnsLivesInItsOwnDataDirectory()
    {
        Assert.Equal(
            "/home/u/.var/app/io.github.mark12870.cabinet/data/prefixes", Layout.PrefixesDir);
    }

    /// <summary>
    /// Nothing is copied onto the host: the shim and the libraries a DAW loads are read
    /// out of the installed Flatpak, through the alias flatpak repoints on update rather
    /// than the content-addressed path that changes with every commit.
    /// </summary>
    [Fact]
    public void TheDawSideFilesAreReadOutOfTheInstalledFlatpak()
    {
        var files = "/home/u/.local/share/flatpak/app/io.github.mark12870.cabinet/current/active/files";

        Assert.Equal($"{files}/lib/yabridge", Layout.HostYabridgeDir);
        Assert.Equal($"{files}/lib/yabridge/cabinet-wine", Layout.ShimPath);
    }

    /// <summary>
    /// yabridgectl searches its own XDG_DATA_HOME, so that has to link to the install
    /// tree — and to the *host* path, because the chainloader hands those back to the shim.
    /// </summary>
    [Fact]
    public void YabridgectlIsPointedAtTheInstallTree()
    {
        var layout = new Layout("/home/u", "/run/user/1000", "/home/u/.var/app/cab/data");

        Assert.Equal("/home/u/.var/app/cab/data/yabridge", layout.SandboxYabridgeLink);
        Assert.NotEqual(layout.HostYabridgeDir, layout.SandboxYabridgeLink);
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
            "/home/u/.var/app/io.github.mark12870.cabinet/data/prefixes/serum/drive_c/Program Files/Common Files/VST3",
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
        var prefix = "/home/u/.var/app/io.github.mark12870.cabinet/data/prefixes/serum/drive_c";

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
