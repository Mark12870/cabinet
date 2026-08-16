using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class IniFileTests
{
    /// A real `flatpak override --user --show fm.reaper.Reaper`.
    private static readonly string[] ReaperOverride =
    [
        "[Context]",
        "devices=shm;",
        "filesystems=xdg-run/yabridge:create;",
        "",
        "[Session Bus Policy]",
        "org.freedesktop.Flatpak=talk",
        "",
        "[Environment]",
        "YABRIDGE_TEMP_DIR=/run/user/1000/yabridge",
        "WINELOADER=/home/testuser/.local/bin/cabinet-wine",
        "YABRIDGE_NO_WATCHDOG=1",
    ];

    [Fact]
    public void ReadsValuesFromTheSectionTheyBelongTo()
    {
        var ini = IniFile.Parse(ReaperOverride);

        Assert.Equal("shm;", ini.Get("Context", "devices"));
        Assert.Equal("talk", ini.Get("Session Bus Policy", "org.freedesktop.Flatpak"));
        Assert.Equal("/run/user/1000/yabridge", ini.Get("Environment", "YABRIDGE_TEMP_DIR"));
    }

    [Fact]
    public void MissingKeysAndSectionsAreNull()
    {
        var ini = IniFile.Parse(ReaperOverride);

        Assert.Null(ini.Get("Context", "sockets"));
        Assert.Null(ini.Get("Nope", "devices"));
    }

    [Fact]
    public void ValuesContainingEqualsSignsSurvive()
    {
        // Real flatpak-info carries these, e.g. LD_LIBRARY_PATH-style settings.
        var ini = IniFile.Parse(["[Environment]", "WINEDLLOVERRIDES=mscoree=d;mshtml=d"]);

        Assert.Equal("mscoree=d;mshtml=d", ini.Get("Environment", "WINEDLLOVERRIDES"));
    }
}
