using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class LayoutTests
{
    private static readonly Layout Layout = new("/home/u", "/run/user/1000");

    [Fact]
    public void EverythingCabinetOwnsLivesInItsOwnDataDirectory()
    {
        Assert.Equal(
            "/home/u/.var/app/io.github.mark12870.cabinet/data/prefixes", Layout.PrefixesDir);
    }

    [Fact]
    public void TheDawSideFilesAreReadOutOfTheInstalledFlatpak()
    {
        var files = "/home/u/.local/share/flatpak/app/io.github.mark12870.cabinet/current/active/files";

        Assert.Equal($"{files}/lib/yabridge", Layout.HostYabridgeDir);
        Assert.Equal($"{files}/lib/yabridge/cabinet-wine", Layout.ShimPath);
    }

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
        Assert.Equal("/run/user/1000/yabridge", Layout.SocketDir);
    }

    [Fact]
    public void PrefixesGetTheConventionalWindowsVst3Directory()
    {
        Assert.Equal(
            "/home/u/.var/app/io.github.mark12870.cabinet/data/prefixes/serum/drive_c/Program Files/Common Files/VST3",
            Layout.PrefixVst3Dir("serum"));
    }

    [Fact]
    public void PluginDirectoriesCoverBothBitnesses()
    {
        var prefix = "/home/u/.var/app/io.github.mark12870.cabinet/data/prefixes/serum/drive_c";

        Assert.Equal(
            [
                $"{prefix}/Program Files/Common Files/VST2",
                $"{prefix}/Program Files/Common Files/VST3",
                $"{prefix}/Program Files/Common Files/CLAP",
                $"{prefix}/Program Files/VstPlugins",
                $"{prefix}/Program Files (x86)/Common Files/VST2",
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
