using System.Security.Cryptography;
using System.Text.Json;

namespace Cabinet.Core;

public sealed record RunnerRelease(string Version, string Asset, string Url, bool BreaksEditors)
{
    public string Name => Runners.DeriveName(Asset);
}

public sealed class RunnerIndex(IProcessRunner runner)
{
    public const string Recommended = "9.21";

    private const string ReleasesUrl =
        "https://api.github.com/repos/Kron4ek/Wine-Builds/releases?per_page=100";

    public IReadOnlyList<RunnerRelease> Available() => ParseReleases(Fetch(ReleasesUrl));

    public RunnerRelease Find(string version) =>
        Available().FirstOrDefault(release => release.Version == version)
        ?? throw new InvalidOperationException(
            $"no {AssetFor(version)} upstream — `cabinet runners available` lists what there is");

    public string Download(RunnerRelease release, string directory)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, Path.GetFileName(release.Asset));

        var fetched = runner.Run("curl", ["-fL", "--retry", "2", "-o", target, release.Url]);
        if (!fetched.Ok)
        {
            throw new InvalidOperationException($"could not download {release.Url}");
        }

        var expected = ChecksumFor(Fetch(SumsUrlFor(release)), release.Asset)
                       ?? throw new InvalidOperationException(
                           $"{release.Asset} is not listed in sha256sums.txt");

        var actual = Sha256(target);
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(target);
            throw new InvalidOperationException(
                $"{release.Asset} failed its checksum: expected {expected}, got {actual}");
        }

        return target;
    }

    public static string AssetFor(string version) => $"wine-{version}-staging-tkg-amd64.tar.xz";

    public static bool BreaksEditors(string version)
    {
        var parts = version.Split('.');

        if (parts.Length < 2 || !int.TryParse(parts[0], out var major)
                             || !int.TryParse(parts[1], out var minor))
        {
            return false;
        }

        return major > 9 || (major == 9 && minor >= 22);
    }

    public static IReadOnlyList<RunnerRelease> ParseReleases(string json)
    {
        var releases = new List<RunnerRelease>();
        using var document = JsonDocument.Parse(json);

        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (!element.TryGetProperty("tag_name", out var tag)
                || tag.GetString() is not { Length: > 0 } version
                || !element.TryGetProperty("assets", out var assets))
            {
                continue;
            }

            var wanted = AssetFor(version);

            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out var name) && name.GetString() == wanted
                    && asset.TryGetProperty("browser_download_url", out var url)
                    && url.GetString() is { Length: > 0 } href)
                {
                    releases.Add(new RunnerRelease(version, wanted, href, BreaksEditors(version)));
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

    private static string SumsUrlFor(RunnerRelease release) =>
        release.Url[..(release.Url.LastIndexOf('/') + 1)] + "sha256sums.txt";

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private string Fetch(string url)
    {
        var result = runner.Run("curl", ["-fsSL", url]);

        return result.Ok
            ? result.Stdout
            : throw new InvalidOperationException($"could not reach {url}");
    }
}
