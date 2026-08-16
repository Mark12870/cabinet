using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class EnrolmentTests
{
    private static readonly Layout Layout = new("/home/u", "/run/user/1000");

    [Theory]
    // Each of these was observed failing during bring-up when absent.
    [InlineData("--device=shm")]
    [InlineData("--filesystem=xdg-run/yabridge:create")]
    [InlineData("--talk-name=org.freedesktop.Flatpak")]
    [InlineData("--env=YABRIDGE_TEMP_DIR=/run/user/1000/yabridge")]
    // Without this the DAW cannot read the chainloader: ~/.local/share/flatpak is masked
    // even under --filesystem=home.
    [InlineData("--filesystem=/home/u/.local/share/flatpak/app/"
                + "io.github.mark12870.cabinet/current/active/files:ro")]
    // The bundles yabridgectl writes symlink into the prefix, and the DAW follows them.
    [InlineData("--filesystem=/home/u/.var/app/io.github.mark12870.cabinet/data/prefixes:ro")]
    [InlineData("--env=WINELOADER=/home/u/.local/share/flatpak/app/"
                + "io.github.mark12870.cabinet/current/active/files/lib/yabridge/cabinet-wine")]
    public void TheOverrideCarriesEverythingTheBoundaryNeeds(string expected)
    {
        Assert.Contains(expected, Enrolment.OverrideArguments("fm.reaper.Reaper", Layout));
    }

    [Fact]
    public void TheOverrideScopesItselfToTheUser()
    {
        var arguments = Enrolment.OverrideArguments("fm.reaper.Reaper", Layout);

        Assert.Equal("override", arguments[0]);
        Assert.Contains("--user", arguments);
        Assert.Contains("fm.reaper.Reaper", arguments);
    }

    [Fact]
    public void PathsAreSpelledOutBecauseFlatpakDoesNoExpansion()
    {
        var command = Enrolment.OverrideCommand("fm.reaper.Reaper", Layout);

        Assert.DoesNotContain("$XDG_RUNTIME_DIR", command);
        Assert.DoesNotContain("~", command);
        Assert.StartsWith("flatpak override --user fm.reaper.Reaper", command);
    }

    /// <summary>
    /// The self-test has to run in the DAW's sandbox, not Cabinet's — that is the whole
    /// point of it, so the app id must be the DAW's.
    /// </summary>
    [Fact]
    public void TheSelfTestRunsTheShimInsideTheDaw()
    {
        Assert.Equal(
            "flatpak run --command=/home/u/.local/share/flatpak/app/io.github.mark12870.cabinet"
            + "/current/active/files/lib/yabridge/cabinet-wine fm.reaper.Reaper "
            + "--cabinet-self-test",
            Enrolment.SelfTestCommand("fm.reaper.Reaper", Layout));
    }
}
