using System.Text.Json;

namespace Cabinet.Core;

public sealed record RunnerFamily(
    string Label,
    string Description,
    string ReleasesUrl,
    string TagPrefix,
    string AssetPrefix,
    string AssetSuffix,
    string? SumsFile)
{
    public string AssetFor(string tag) => AssetPrefix + tag + AssetSuffix;

    public string VersionOf(string tag) => tag[TagPrefix.Length..];

    public static readonly RunnerFamily Soda = new(
        "Soda",
        "Valve's Wine with Staging and Proton patches.",
        "https://api.github.com/repos/bottlesdevs/wine/releases?per_page=100",
        TagPrefix: "soda-",
        AssetPrefix: "",
        AssetSuffix: "-x86_64.tar.xz",
        SumsFile: null);

    public static readonly RunnerFamily Kron4ek = new(
        "Kron4ek",
        "Wine upstream with Staging and Staging-TkG patches.",
        "https://api.github.com/repos/Kron4ek/Wine-Builds/releases?per_page=100",
        TagPrefix: "",
        AssetPrefix: "wine-",
        AssetSuffix: "-staging-tkg-amd64.tar.xz",
        SumsFile: "sha256sums.txt");
}

public sealed record RunnerRelease(RunnerFamily Family, string Version, string Asset, string Url)
{
    public string Name => Runners.DeriveName(Asset);
}

public sealed class RunnerIndex(IProcessRunner runner)
{
    public static readonly IReadOnlyList<RunnerFamily> Families =
        [RunnerFamily.Soda, RunnerFamily.Kron4ek];

    public IReadOnlyList<RunnerRelease> Available() =>
        Families.SelectMany(family => ParseReleases(family, Fetch(family.ReleasesUrl))).ToList();

    public RunnerRelease Find(string spec)
    {
        var matches = Available()
            .Where(release => release.Version == spec || release.Name == spec)
            .ToList();

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"no Wine {spec} upstream — `cabinet runners available` lists what there is"),
            _ => throw new InvalidOperationException(
                $"{spec} is more than one build — ask for "
                + string.Join(" or ", matches.Select(match => match.Name))),
        };
    }

    public string Download(RunnerRelease release, string directory, Action<string>? onOutput = null)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, Path.GetFileName(release.Asset));

        var fetched = runner.Run("curl", ["-fL", "--retry", "2", "-o", target, release.Url]);
        if (!fetched.Ok)
        {
            throw new InvalidOperationException($"could not download {release.Url}");
        }

        if (release.Family.SumsFile is not { } sums)
        {
            onOutput?.Invoke(
                $"{release.Family.Label} publishes no checksum, so {release.Asset} is taken "
                + "on the strength of its https download alone");
            return target;
        }

        var expected = ChecksumFor(Fetch(SumsUrlFor(release, sums)), release.Asset)
                       ?? throw new InvalidOperationException(
                           $"{release.Asset} is not listed in {sums}");

        Checksum.Expect(target, expected);
        return target;
    }

    public static IReadOnlyList<RunnerRelease> ParseReleases(RunnerFamily family, string json)
    {
        var releases = new List<RunnerRelease>();
        using var document = JsonDocument.Parse(json);

        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (!element.TryGetProperty("tag_name", out var tagged)
                || tagged.GetString() is not { Length: > 0 } tag
                || !tag.StartsWith(family.TagPrefix, StringComparison.Ordinal)
                || !element.TryGetProperty("assets", out var assets))
            {
                continue;
            }

            var wanted = family.AssetFor(tag);

            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out var name) && name.GetString() == wanted
                    && asset.TryGetProperty("browser_download_url", out var url)
                    && url.GetString() is { Length: > 0 } href)
                {
                    releases.Add(new RunnerRelease(family, family.VersionOf(tag), wanted, href));
                    break;
                }
            }
        }

        return releases;
    }

    public static string? ChecksumFor(string sums, string asset)
    {
        foreach (var line in sums.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (fields.Length >= 2 && fields[^1].TrimStart('*') == asset)
            {
                return fields[0];
            }
        }

        return null;
    }

    private static string SumsUrlFor(RunnerRelease release, string sums) =>
        release.Url[..(release.Url.LastIndexOf('/') + 1)] + sums;

    private string Fetch(string url)
    {
        var result = runner.Run("curl", ["-fsSL", url]);

        return result.Ok
            ? result.Stdout
            : throw new InvalidOperationException($"could not reach {url}");
    }
}
