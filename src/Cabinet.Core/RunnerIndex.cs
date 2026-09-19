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

    public static readonly RunnerFamily D2D1Dcomp = new(
        "wine-d2d1-dcomp",
        "mklnln's Wine with Direct2D 1.3 and DirectComposition, for JUCE 8 plugin GUIs.",
        NamePrefix: "wine-d2d1-",
        NameSuffix: "",
        AssetPrefix: "wine-d2d1-",
        AssetSuffix: "-x86_64.tar.zst",
        SumsFile: null);
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

    private sealed record FixedRunner(RunnerRelease Release, string Url, string Sha256);

    private static readonly IReadOnlyList<FixedRunner> Fixed =
    [
        Pinned(
            RunnerFamily.Kron4ek, "9.21",
            "https://github.com/Kron4ek/Wine-Builds/releases/download/9.21/",
            "a9aaf78cc4453269e130edb299679d5fbf4756ebb5f0e75136e8e69e51a6bc11"),
        Pinned(
            RunnerFamily.Soda, "11.0-5",
            "https://github.com/bottlesdevs/wine/releases/download/soda-11.0-5/",
            "63dfa05aee8be3a95bab4875a5dd69f432670ecb828491453bfc5e238e6f1595"),
        new(
            new RunnerRelease(
                RunnerFamily.D2D1Dcomp, "d2d1-11.0", RunnerFamily.D2D1Dcomp.AssetFor("11.0"), ""),
            "https://github.com/mklnln/wine-d2d1-dcomp/releases/download/v11.0/"
            + "wine-d2d1-11.0-x86_64.tar.zst",
            "909e283e1e087a93e196defffd2a67120ab2df6ea4edd7f6f45b97528bf8646b"),
    ];

    private static FixedRunner Pinned(RunnerFamily family, string version, string directory, string sha256)
    {
        var asset = family.AssetFor(version);
        return new(new RunnerRelease(family, version, asset, ""), directory + asset, sha256);
    }

    public const string Provenance =
        "A pinned build is checked against a SHA-256 that Cabinet ships. Any other build is "
        + "checked only against Bottles' component index, which also says where to fetch it.";

    public static bool IsPinned(RunnerRelease release) =>
        Fixed.Any(known => known.Release.Name == release.Name);

    public static bool IsPinned(string spec) => PinnedFor(spec) is not null;

    private static RunnerRelease? PinnedFor(string spec) =>
        Fixed.Select(known => known.Release)
            .FirstOrDefault(release => release.Version == spec || release.Name == spec);

    public IReadOnlyList<RunnerRelease> Available(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entries = Components.Entries(http.Text(Components.IndexUrl, cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();

        var listed = Families.SelectMany(family => ReleasesFrom(family, entries))
            .Select(release => PinnedFor(release.Name) ?? release)
            .ToList();

        return listed
            .Concat(Fixed.Select(known => known.Release).Where(release => !listed.Contains(release)))
            .ToList();
    }

    public RunnerRelease Find(string spec)
    {
        if (PinnedFor(spec) is { } pinned)
        {
            return pinned;
        }

        var matches = Available()
            .Where(release => release.Version == spec || release.Name == spec)
            .ToList();

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new KeyNotFoundException(
                $"no Wine {spec} among the versions available upstream"),
            _ => throw new InvalidOperationException(
                $"{spec} is more than one build — ask for "
                + string.Join(" or ", matches.Select(match => match.Name))),
        };
    }

    public static bool MatchesFixedRunner(string name, string spec) =>
        Fixed.Any(known => known.Release.Name == name && known.Release.Version == spec);

    public string Download(
        RunnerRelease release,
        string directory,
        Action<string>? onOutput = null,
        Action<double>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory);
        cancellationToken.ThrowIfCancellationRequested();

        if (Fixed.FirstOrDefault(known => known.Release.Name == release.Name) is { } found)
        {
            var downloaded = Path.Combine(directory, release.Asset);
            http.ToFile(found.Url, downloaded, onOutput, onProgress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Checksum.Expect(downloaded, found.Sha256);
            cancellationToken.ThrowIfCancellationRequested();
            return downloaded;
        }

        var listed = Components.Manifest(http.Text(release.ManifestUrl, cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        if (listed.Url.Length == 0 || listed.FileName != release.Asset)
        {
            throw new InvalidOperationException(
                $"{release.Name} no longer offers {release.Asset} in Bottles' component index");
        }

        var target = Path.Combine(directory, release.Asset);
        http.ToFile(listed.Url, target, onOutput, onProgress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (release.Family.SumsFile is not { } sums)
        {
            Checksum.ExpectMd5(target, listed.Checksum);
            cancellationToken.ThrowIfCancellationRequested();
            return target;
        }

        var expected = ChecksumFor(http.Text(SumsUrlFor(listed.Url, sums), cancellationToken), release.Asset)
                       ?? throw new InvalidOperationException(
                           $"{release.Asset} is not listed in {sums}");

        cancellationToken.ThrowIfCancellationRequested();
        Checksum.Expect(target, expected);
        cancellationToken.ThrowIfCancellationRequested();
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
