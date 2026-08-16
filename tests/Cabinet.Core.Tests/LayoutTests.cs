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
}
