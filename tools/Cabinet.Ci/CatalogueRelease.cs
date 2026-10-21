using System.Globalization;
using System.Numerics;
using System.Security;
using System.Text.RegularExpressions;
using System.Xml;

namespace Cabinet.Ci;

internal static class CatalogueRelease
{
    internal const string MetainfoPath = "flatpak/io.github.mark12870.cabinet.metainfo.xml";
    private const string ReleasesTag = "<releases>";
    private static readonly Regex ReleaseVersion = new("^(?<major>0|[1-9][0-9]*)\\.(?<minor>0|[1-9][0-9]*)\\.(?<patch>0|[1-9][0-9]*)\\z",
        RegexOptions.CultureInvariant);
    private static readonly Regex CandidateVersion = new("^(?:0|[1-9][0-9]*)(?:\\.(?:0|[1-9][0-9]*))*(?:-(?:0|[1-9][0-9]*))?\\z",
        RegexOptions.CultureInvariant);
    private static readonly Regex FirstRelease = new("^    <release version=\"[^\"]+\" date=\"(?<date>[^\"]+)\">$",
        RegexOptions.CultureInvariant);

    public static string Generate(string baseline, string name, string candidateVersion, DateOnly date)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidDataException("trusted catalogue entry has no name");
        }
        if (!CandidateVersion.IsMatch(candidateVersion))
        {
            throw new InvalidDataException("candidate version is not a stable numeric version");
        }

        var releaseVersion = NextReleaseVersion(baseline);
        var (insertion, newline) = InsertionPoint(baseline);
        try
        {
            XmlConvert.VerifyXmlChars(name);
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException("trusted catalogue name is not valid XML text", exception);
        }

        var escapedName = SecurityElement.Escape(name)!;
        var dateText = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var release = string.Join(newline,
        [
            $"    <release version=\"{releaseVersion}\" date=\"{dateText}\">",
            "      <description>",
            $"        <p>Update {escapedName}</p>",
            "        <ul>",
            $"          <li>Install {escapedName} {candidateVersion} from the updated catalogue</li>",
            "        </ul>",
            "      </description>",
            "    </release>"
        ]);
        return baseline.Insert(insertion, release + newline + newline);
    }

    public static DateOnly ReadCandidateDate(string metainfo)
    {
        var (insertion, newline) = InsertionPoint(metainfo);
        var lineEnd = metainfo.IndexOf(newline, insertion, StringComparison.Ordinal);
        if (lineEnd < 0)
        {
            throw new InvalidDataException("candidate metainfo has no first release line");
        }

        var match = FirstRelease.Match(metainfo[insertion..lineEnd]);
        var value = match.Success ? match.Groups["date"].Value : "";
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            || date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) != value)
        {
            throw new InvalidDataException("candidate release date must be canonical ISO format");
        }

        return date;
    }

    private static string NextReleaseVersion(string metainfo)
    {
        var firstRelease = Regex.Match(metainfo, "<release\\s+version=\"(?<version>[^\"]+)\"", RegexOptions.CultureInvariant);
        var version = firstRelease.Success ? ReleaseVersion.Match(firstRelease.Groups["version"].Value) : Match.Empty;
        if (!version.Success)
        {
            throw new InvalidDataException("trusted metainfo has no canonical first release version");
        }

        var patch = BigInteger.Parse(version.Groups["patch"].Value, CultureInfo.InvariantCulture) + BigInteger.One;
        return $"{version.Groups["major"].Value}.{version.Groups["minor"].Value}.{patch}";
    }

    private static (int Insertion, string Newline) InsertionPoint(string metainfo)
    {
        var opening = metainfo.IndexOf(ReleasesTag, StringComparison.Ordinal);
        if (opening < 0 || opening != metainfo.LastIndexOf(ReleasesTag, StringComparison.Ordinal)
            || metainfo.IndexOf("</releases>", opening + ReleasesTag.Length, StringComparison.Ordinal) < 0)
        {
            throw new InvalidDataException("metainfo must contain one releases section");
        }

        var content = opening + ReleasesTag.Length;
        var newline = metainfo.AsSpan(content).StartsWith("\r\n", StringComparison.Ordinal) ? "\r\n"
            : metainfo.AsSpan(content).StartsWith("\n", StringComparison.Ordinal) ? "\n" : "";
        if (newline.Length == 0)
        {
            throw new InvalidDataException("metainfo releases section must start on a new line");
        }

        return (content + newline.Length, newline);
    }
}
