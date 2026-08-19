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
    [InlineData("--filesystem=/home/u/.var/app/io.github.mark12870.cabinet/data/native:ro")]
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

    [Fact]
    public void LinkingPointsTheDawAtTheYabridgeItMustRead()
    {
        using var home = new TempHome();
        Directory.CreateDirectory(home.Layout.DawDataHome("fm.reaper.Reaper"));

        var link = Enrolment.Link("fm.reaper.Reaper", home.Layout);

        Assert.Equal(home.Layout.DawYabridgeLink("fm.reaper.Reaper"), link);
        Assert.Equal(home.Layout.HostYabridgeDir, new DirectoryInfo(link).LinkTarget);
    }

    [Fact]
    public void LinkingAgainReplacesAStaleLink()
    {
        using var home = new TempHome();
        Directory.CreateDirectory(home.Layout.DawDataHome("fm.reaper.Reaper"));
        File.CreateSymbolicLink(
            home.Layout.DawYabridgeLink("fm.reaper.Reaper"), "/somewhere/else");

        var link = Enrolment.Link("fm.reaper.Reaper", home.Layout);

        Assert.Equal(home.Layout.HostYabridgeDir, new DirectoryInfo(link).LinkTarget);
    }

    [Fact]
    public void ADawThatIsNotInstalledIsRefused()
    {
        using var home = new TempHome();

        Assert.Throws<DirectoryNotFoundException>(
            () => Enrolment.Link("fm.reaper.Reaper", home.Layout));
    }

    private sealed class TempHome : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("cabinet").FullName;

        public Layout Layout => new(root, "/run/user/1000", Path.Combine(root, "data"));

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
