using Cabinet.Ci;

namespace Cabinet.Core.Tests;

public sealed class CatalogueReleaseTests
{
    [Fact]
    public void GenerationIncrementsTheNewestPatchAndPreservesTheExistingDocument()
    {
        const string baseline = "<component>\n  <releases>\n    <release version=\"1.2.10\" date=\"2026-01-02\">\n      <description>\n        <p>Newest</p>\n      </description>\n    </release>\n    <release version=\"1.2.9\" date=\"2026-01-01\">\n      <description>\n        <p>Older</p>\n      </description>\n    </release>\n  </releases>\n</component>\n";

        var generated = CatalogueRelease.Generate(baseline, [new("Plugin & <One>", "4.5.6")], new DateOnly(2026, 10, 21));

        Assert.StartsWith("<component>\n  <releases>\n", generated, StringComparison.Ordinal);
        Assert.Contains("<release version=\"1.2.11\" date=\"2026-10-21\">", generated);
        Assert.Contains("<p>Update Plugin &amp; &lt;One&gt;</p>", generated);
        Assert.Contains("<li>Install Plugin &amp; &lt;One&gt; 4.5.6 from the updated catalogue</li>", generated);
        var originalReleases = baseline[baseline.IndexOf("    <release", StringComparison.Ordinal)..];
        Assert.EndsWith(originalReleases, generated, StringComparison.Ordinal);
        Assert.Equal(new DateOnly(2026, 10, 21), CatalogueRelease.ReadCandidateDate(generated));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void GenerationPreservesExistingLfAndCrLfContent(string newline)
    {
        var baseline = string.Join(newline,
        [
            "<component>",
            "  <releases>",
            "    <release version=\"1.0.0\" date=\"2026-01-01\">",
            "    </release>",
            "  </releases>",
            "</component>",
            ""
        ]);

        var generated = CatalogueRelease.Generate(baseline, [new("Plugin", "1.0.0")], new DateOnly(2026, 1, 2));
        var insertion = baseline.IndexOf("<releases>", StringComparison.Ordinal) + "<releases>".Length + newline.Length;
        var release = string.Join(newline,
        [
            "    <release version=\"1.0.1\" date=\"2026-01-02\">",
            "      <description>",
            "        <p>Update Plugin</p>",
            "        <ul>",
            "          <li>Install Plugin 1.0.0 from the updated catalogue</li>",
            "        </ul>",
            "      </description>",
            "    </release>"
        ]) + newline + newline;

        Assert.Equal(baseline.Insert(insertion, release), generated);
    }

    [Theory]
    [InlineData("2026-1-01")]
    [InlineData("2026-02-30")]
    [InlineData("not-a-date")]
    public void CandidateDatesMustBeCanonicalAndValid(string date)
    {
        var generated = CatalogueRelease.Generate("<releases>\n    <release version=\"1.0.0\" date=\"2026-01-01\">\n    </release>\n</releases>",
            [new("Plugin", "1.0.0")], new DateOnly(2026, 1, 2));
        generated = generated.Replace("date=\"2026-01-02\"", $"date=\"{date}\"", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => CatalogueRelease.ReadCandidateDate(generated));
    }

    [Fact]
    public void VersionValidatorsRejectTrailingLineFeeds()
    {
        const string baseline = "<releases>\n    <release version=\"1.0.0\" date=\"2026-01-01\">\n    </release>\n</releases>";

        Assert.Throws<InvalidDataException>(() => CatalogueRelease.Generate(baseline, [new("Plugin", "1.0.0\n")], new DateOnly(2026, 1, 2)));
        Assert.Throws<InvalidDataException>(() => CatalogueRelease.Generate(
            baseline.Replace("version=\"1.0.0\"", "version=\"1.0.0\n\"", StringComparison.Ordinal),
            [new("Plugin", "1.0.0")], new DateOnly(2026, 1, 2)));
    }

    [Fact]
    public void BatchGenerationUsesOneNeutralHeadlineAndEscapedEntryBullets()
    {
        const string baseline = "<component>\n  <releases>\n    <release version=\"1.2.10\" date=\"2026-01-02\">\n      <description>\n        <p>Older</p>\n      </description>\n    </release>\n  </releases>\n</component>\n";

        var generated = CatalogueRelease.Generate(baseline,
            [new("Second & <Plugin>", "2.3.4"), new("First Plugin", "1.2.3")], new DateOnly(2026, 10, 21));

        Assert.Contains("<p>Update catalogue plugins</p>", generated);
        Assert.Contains("<li>Install Second &amp; &lt;Plugin&gt; 2.3.4 from the updated catalogue</li>", generated);
        Assert.Contains("<li>Install First Plugin 1.2.3 from the updated catalogue</li>", generated);
        Assert.Equal(1, generated.Split("<release version=\"1.2.11\"", StringSplitOptions.None).Length - 1);
        Assert.EndsWith(baseline[baseline.IndexOf("    <release", StringComparison.Ordinal)..], generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GenerationRejectsAnEmptyBatch()
    {
        Assert.Throws<InvalidDataException>(() => CatalogueRelease.Generate("<releases>\n</releases>", [], new DateOnly(2026, 1, 2)));
    }
}
