namespace Cabinet.Core;

public sealed record RunnerFamily(
    string Label,
    string Description,
    string NamePrefix,
    string NameSuffix,
    string AssetPrefix,
    string AssetSuffix,
    string? SumsFile)
{
    public string AssetFor(string version) => AssetPrefix + version + AssetSuffix;

    public string? VersionOf(string name)
    {
        if (!name.StartsWith(NamePrefix, StringComparison.Ordinal)
            || !name.EndsWith(NameSuffix, StringComparison.Ordinal)
            || name.Length <= NamePrefix.Length + NameSuffix.Length)
        {
            return null;
        }

        var version = name[NamePrefix.Length..(name.Length - NameSuffix.Length)];

        return char.IsAsciiDigit(version[0]) ? version : null;
    }

    public static readonly RunnerFamily Soda = new(
        "Soda",
        "Valve's Wine with Staging and Proton patches.",
        NamePrefix: "soda-",
        NameSuffix: "",
        AssetPrefix: "soda-",
        AssetSuffix: "-x86_64.tar.xz",
        SumsFile: null);

    public static readonly RunnerFamily Kron4ek = new(
        "Kron4ek",
        "Wine upstream with Staging and Staging-TkG patches.",
        NamePrefix: "kron4ek-wine-",
        NameSuffix: "-staging-tkg-amd64",
        AssetPrefix: "wine-",
        AssetSuffix: "-staging-tkg-amd64.tar.xz",
        SumsFile: "sha256sums.txt");
}

public sealed record RunnerRelease(
    RunnerFamily Family, string Version, string Asset, string ManifestUrl)
{
    public string Name => Runners.DeriveName(Asset);
}

public sealed class RunnerIndex(IProcessRunner runner)
{
    private readonly Http http = new(runner);

    public static readonly IReadOnlyList<RunnerFamily> Families =
        [RunnerFamily.Soda, RunnerFamily.Kron4ek];

    public IReadOnlyList<RunnerRelease> Available()
    {
        var entries = Components.Entries(http.Text(Components.IndexUrl));

        return Families.SelectMany(family => ReleasesFrom(family, entries)).ToList();
    }

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

    public string Download(
        RunnerRelease release,
        string directory,
        Action<string>? onOutput = null,
        Action<double>? onProgress = null)
    {
        Directory.CreateDirectory(directory);

        var listed = Components.Manifest(http.Text(release.ManifestUrl));
        if (listed.Url.Length == 0 || listed.FileName != release.Asset)
        {
            throw new InvalidOperationException(
                $"{release.Name} no longer offers {release.Asset} in Bottles' component index");
        }

        var target = Path.Combine(directory, release.Asset);
        http.ToFile(listed.Url, target, onOutput, onProgress);

        if (release.Family.SumsFile is not { } sums)
        {
            Checksum.ExpectMd5(target, listed.Checksum);
            return target;
        }

        var expected = ChecksumFor(http.Text(SumsUrlFor(listed.Url, sums)), release.Asset)
                       ?? throw new InvalidOperationException(
                           $"{release.Asset} is not listed in {sums}");

        Checksum.Expect(target, expected);
        return target;
    }

    public static IReadOnlyList<RunnerRelease> ReleasesFrom(
        RunnerFamily family, IReadOnlyList<ComponentEntry> entries) =>
        entries
            .Where(entry =>
                entry is { Category: "runners", SubCategory: "wine", Channel: "stable" })
            .Select(entry => (entry, version: family.VersionOf(entry.Name)))
            .Where(found => found.version is not null)
            .OrderByDescending(found => found.entry.Date)
            .ThenByDescending(found => found.entry.Name, StringComparer.Ordinal)
            .Select(found => new RunnerRelease(
                family,
                found.version!,
                family.AssetFor(found.version!),
                Components.ManifestUrl(found.entry.Name)))
            .ToList();

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

    private static string SumsUrlFor(string url, string sums) =>
        url[..(url.LastIndexOf('/') + 1)] + sums;
}
