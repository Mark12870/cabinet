using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class MetainfoTests
{
    private const string Xml = """
        <component type="desktop-application">
          <id>io.github.mark12870.cabinet</id>
          <url type="homepage">https://github.com/Mark12870/cabinet</url>
          <url type="bugtracker">https://github.com/Mark12870/cabinet/issues</url>
          <releases>
            <release version="0.3.0" date="2026-08-18" />
            <release version="0.2.1" date="2026-08-18" />
            <release version="0.1.0" date="2026-08-18" />
          </releases>
        </component>
        """;

    [Fact]
    public void TheNewestReleaseIsTheVersionTheBuildReports()
    {
        Assert.Equal("0.3.0", Metainfo.Parse(Xml).Version);
    }

    [Fact]
    public void TheProjectLinksComeFromTheirOwnUrlTags()
    {
        var metainfo = Metainfo.Parse(Xml);

        Assert.Equal("https://github.com/Mark12870/cabinet", metainfo.Homepage);
        Assert.Equal("https://github.com/Mark12870/cabinet/issues", metainfo.BugTracker);
    }

    [Fact]
    public void AMetainfoWithNoReleasesStillParses()
    {
        var metainfo = Metainfo.Parse("<component type=\"desktop-application\" />");

        Assert.Equal("unknown", metainfo.Version);
        Assert.Null(metainfo.Homepage);
    }
}
