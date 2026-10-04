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
            <release version="0.3.0" date="2026-08-18">
              <description>
                <p>Prefixes run
                  in a desktop</p>
                <ul>
                  <li>Run one with cabinet set &lt;name&gt; desktop</li>
                  <li>Doctor names the desktop</li>
                </ul>
              </description>
            </release>
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

    [Fact]
    public void EachReleaseCarriesItsDateHeadlineAndChangesNewestFirst()
    {
        var newest = Metainfo.Parse(Xml).Releases[0];

        Assert.Equal(["0.3.0", "0.2.1", "0.1.0"], Metainfo.Parse(Xml).Releases.Select(release => release.Version));
        Assert.Equal("2026-08-18", newest.Date);
        Assert.Equal("Prefixes run in a desktop", newest.Headline);
        Assert.Equal(["Run one with cabinet set <name> desktop", "Doctor names the desktop"], newest.Changes);
    }

    [Fact]
    public void AReleaseWithoutNotesHasAnEmptyHeadlineAndNoChanges()
    {
        var bare = Metainfo.Parse(Xml).Releases[1];

        Assert.Equal("", bare.Headline);
        Assert.Empty(bare.Changes);
    }

    [Fact]
    public void EveryShippedReleaseHasAHeadline()
    {
        var releases = Metainfo.Parse(Repo.Read("io.github.mark12870.cabinet.metainfo.xml")).Releases;

        Assert.NotEmpty(releases);
        Assert.DoesNotContain(releases, release => release.Headline.Length == 0);
    }
}
