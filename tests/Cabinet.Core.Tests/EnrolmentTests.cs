using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class EnrolmentTests
{
    private static readonly Layout Layout = new("/home/u", "/run/user/1000");

    [Theory]
    [InlineData("--device=shm")]
    [InlineData("--filesystem=xdg-run/yabridge:create")]
    [InlineData("--talk-name=org.freedesktop.Flatpak")]
    [InlineData("--env=YABRIDGE_TEMP_DIR=/run/user/1000/yabridge")]
    [InlineData("--filesystem=/home/u/.local/share/flatpak/app/"
                + "io.github.mark12870.cabinet/current/active/files:ro")]
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
