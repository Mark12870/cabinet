using System.Xml.Linq;

namespace Cabinet.Core;

public sealed record Metainfo(string Version, string? Homepage, string? BugTracker)
{
    public static readonly Metainfo Unknown = new("unknown", null, null);

    public static Metainfo Parse(string xml)
    {
        var component = XDocument.Parse(xml).Root;

        if (component is null)
        {
            return Unknown;
        }

        var newest = component.Element("releases")?.Elements("release")
            .Select(release => release.Attribute("version")?.Value)
            .FirstOrDefault(version => !string.IsNullOrEmpty(version));

        return new Metainfo(newest ?? "unknown", Url(component, "homepage"), Url(component, "bugtracker"));
    }

    private static string? Url(XElement component, string type) =>
        component.Elements("url")
            .FirstOrDefault(url => url.Attribute("type")?.Value == type)?
            .Value.Trim();
}
