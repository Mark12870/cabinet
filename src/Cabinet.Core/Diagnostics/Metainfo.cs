using System.Xml.Linq;

namespace Cabinet.Core;

public sealed record Release(string Version, string Date, string Headline, IReadOnlyList<string> Changes);

public sealed record Metainfo(
    string Version, string? Homepage, string? BugTracker, IReadOnlyList<Release> Releases)
{
    public static readonly Metainfo Unknown = new("unknown", null, null, []);

    public static Metainfo Parse(string xml)
    {
        var component = XDocument.Parse(xml).Root;

        if (component is null)
        {
            return Unknown;
        }

        var releases = component.Element("releases")?.Elements("release")
            .Select(ReleaseOf)
            .Where(release => release.Version.Length > 0)
            .ToList() ?? [];

        return new Metainfo(
            releases.FirstOrDefault()?.Version ?? "unknown",
            Url(component, "homepage"),
            Url(component, "bugtracker"),
            releases);
    }

    private static Release ReleaseOf(XElement release)
    {
        var description = release.Element("description");

        return new Release(
            release.Attribute("version")?.Value ?? "",
            release.Attribute("date")?.Value ?? "",
            Text(description?.Element("p")),
            description?.Element("ul")?.Elements("li").Select(Text).ToList() ?? []);
    }

    private static string Text(XElement? element) =>
        element is null ? "" : string.Join(' ', element.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? Url(XElement component, string type) =>
        component.Elements("url")
            .FirstOrDefault(url => url.Attribute("type")?.Value == type)?
            .Value.Trim();
}
