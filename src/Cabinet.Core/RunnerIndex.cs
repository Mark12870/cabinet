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
    public static readonly IReadOnlyList<RunnerFamily> Families =
        [RunnerFamily.Soda, RunnerFamily.Kron4ek];

    public IReadOnlyList<RunnerRelease> Available()
    {
        var entries = Components.Entries(Fetch(Components.IndexUrl));

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

    public string Download(RunnerRelease release, string directory, Action<string>? onOutput = null)
    {
        Directory.CreateDirectory(directory);

        var listed = Components.Manifest(Fetch(release.ManifestUrl));
        if (listed.Url.Length == 0 || listed.FileName != release.Asset)
        {
            throw new InvalidOperationException(
                $"{release.Name} no longer offers {release.Asset} in Bottles' component index");
        }

        var target = Path.Combine(directory, release.Asset);
        onOutput?.Invoke($"Downloading {release.Asset}");

        var fetched = runner.Run("curl", ["-fL", "--retry", "2", "-o", target, listed.Url]);
        if (!fetched.Ok)
        {
            throw new InvalidOperationException($"could not download {listed.Url}");
        }

        if (release.Family.SumsFile is not { } sums)
        {
            Checksum.ExpectMd5(target, listed.Checksum);
            return target;
        }

        var expected = ChecksumFor(Fetch(SumsUrlFor(listed.Url, sums)), release.Asset)
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

    private static string? StatusOf(string headers)
    {
        string? status = null;

        foreach (var line in headers.Split('\n'))
        {
            if (line.StartsWith("HTTP/", StringComparison.Ordinal)
                && line.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 1 } fields)
            {
                status = fields[1];
            }
        }

        return status;
    }

    private static string SumsUrlFor(string url, string sums) =>
        url[..(url.LastIndexOf('/') + 1)] + sums;

    private static string LastLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            is { Length: > 0 } lines
            ? lines[^1]
            : "curl said nothing";

    private string Fetch(string url)
    {
        var result = runner.Run(
            "curl", ["-sSL", "--retry", "2", "--max-time", "30", "-D", "/dev/stderr", url]);

        var host = new Uri(url).Host;

        if (!result.Ok)
        {
            throw new InvalidOperationException($"could not reach {host} — {LastLine(result.Stderr)}");
        }

        var status = StatusOf(result.Stderr);

        return status is null or "200"
            ? result.Stdout
            : throw new InvalidOperationException(
                $"{host} answered {status} for {new Uri(url).AbsolutePath}");
    }
}
