using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cabinet.Ci;

internal sealed class CatalogueUpdateService(HttpClient client, string repository,
    Action<string>? report = null, string? githubToken = null)
{
    private const string ScenarioNamespace = "Cabinet.Runtime.Tests.Scenarios.";
    private const long MaximumDownloadSize = 1_073_741_824;
    private const int MaximumDocumentSize = 16_777_216;

    public async Task<IReadOnlyList<CatalogueUpdateCandidate>> Discover(string outputDirectory,
        CancellationToken cancellationToken = default, bool createCandidates = true)
    {
        var entries = ReadConfiguration();
        var coverage = CatalogueUpdateCoverage.Read(repository);
        var output = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);

        foreach (var entry in entries)
        {
            File.Delete(Path.Combine(output, entry.Id + ".yml"));
        }

        var candidates = new List<CatalogueUpdateCandidate>();
        foreach (var entry in entries)
        {
            var status = coverage.Single(item => item.Id == entry.Id);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Path != status.Path)
                {
                    throw new InvalidDataException("configured path does not match the catalogue entry");
                }
                var current = File.ReadAllText(ResolveLibraryPath(entry.Path));
                if (ReadField(current, "Source") != "download")
                {
                    throw new InvalidDataException("only pinned download entries can be updated");
                }
                var currentVersion = ReadField(current, "Version");
                var source = ParseSource(entry.Source);
                var release = source.Type switch
                {
                    "github" => await GithubRelease(source, entry.AllowedDownloadHosts, cancellationToken),
                    "vendor-page" => await VendorRelease(source, entry.AllowedDownloadHosts, cancellationToken),
                    "document" => await DocumentRelease(source, entry.AllowedDownloadHosts, cancellationToken),
                    "redirect" => await RedirectRelease(source, entry.AllowedDownloadHosts, cancellationToken),
                    "debian" => await DebianRelease(source, entry.AllowedDownloadHosts, cancellationToken),
                    _ => throw new NotSupportedException($"unsupported source type '{source.Type}'")
                };

                if (release is null)
                {
                    throw new InvalidDataException("no matching stable upstream download found");
                }

                ValidateDownloadUrl(entry, source, release.Url, release.Version, allowTemporary: true);
                status.AvailableVersion = release.Version;
                if (TemporaryDownload(release.Url))
                {
                    status.AutomationBlockers = [.. status.AutomationBlockers, "temporary-download-url"];
                }
                if (status.AutomationBlockers.Contains("unsupported-version"))
                {
                    status.Outcome = "observed";
                    continue;
                }

                currentVersion = NormaliseVersion(currentVersion);
                if (CompareVersions(release.Version, currentVersion) <= 0)
                {
                    status.Outcome = "current";
                    continue;
                }

                status.Outcome = "newer";
                if (status.AutomationBlockers.Length > 0 || !createCandidates)
                {
                    continue;
                }

                ValidateMapping(entry);
                var hash = await HashDownload(release.Url, entry.AllowedDownloadHosts, cancellationToken);
                if (release.Hash is not null && !string.Equals(release.Hash, hash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("download hash does not match the release metadata");
                }
                var candidateText = UpdateFields(current, release.Version, release.Url, hash);
                File.WriteAllText(Path.Combine(output, entry.Id + ".yml"), candidateText, new UTF8Encoding(false));
                candidates.Add(new(entry.Id, entry.Path, release.Version));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                status.Outcome = "failed";
                status.Error = "upstream check cancelled";
                WriteCandidates(Path.Combine(output, "candidates.json"), candidates);
                CatalogueUpdateCoverage.Write(output, coverage);
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                status.Outcome = "failed";
                status.Error = exception.Message;
                report?.Invoke($"catalogue update skipped {entry.Id}: {exception.Message}");
            }
        }

        WriteCandidates(Path.Combine(output, "candidates.json"), candidates.OrderBy(c => c.Id, StringComparer.Ordinal));
        CatalogueUpdateCoverage.Write(output, coverage);
        return candidates;
    }

    public string Validate(string baseDirectory, string id)
    {
        var validated = ValidateEntry(baseDirectory, id);
        var baseMetainfo = File.ReadAllText(ResolveUnder(Path.GetFullPath(baseDirectory), CatalogueRelease.MetainfoPath));
        var candidateMetainfo = File.ReadAllText(ResolveUnder(repository, CatalogueRelease.MetainfoPath));
        var date = CatalogueRelease.ReadCandidateDate(candidateMetainfo);
        var expectedMetainfo = CatalogueRelease.Generate(baseMetainfo, ReadField(validated.Baseline, "Name"),
            validated.Version, date);
        if (candidateMetainfo != expectedMetainfo)
        {
            throw new InvalidDataException("metainfo release does not match the trusted base and catalogue update");
        }

        return validated.Entry.Scenario;
    }

    public void GenerateRelease(string baseDirectory, string id, DateOnly date)
    {
        var validated = ValidateEntry(baseDirectory, id);
        var baselineMetainfo = File.ReadAllText(ResolveUnder(Path.GetFullPath(baseDirectory), CatalogueRelease.MetainfoPath));
        var candidateMetainfo = CatalogueRelease.Generate(baselineMetainfo, ReadField(validated.Baseline, "Name"),
            validated.Version, date);
        File.WriteAllText(ResolveUnder(repository, CatalogueRelease.MetainfoPath), candidateMetainfo, new UTF8Encoding(false));
    }

    private ValidatedCatalogueEntry ValidateEntry(string baseDirectory, string id)
    {
        var entries = ReadConfiguration();
        var entry = entries.SingleOrDefault(e => e.Id == id)
            ?? throw new InvalidDataException($"catalogue update ID '{id}' is not configured");
        ValidateMapping(entry);
        var status = CatalogueUpdateCoverage.Read(repository).Single(item => item.Id == id);
        if (status.AutomationBlockers.Length > 0)
        {
            throw new InvalidDataException("entry does not have complete scenario coverage: " + string.Join(", ", status.AutomationBlockers));
        }

        var baselineRoot = Path.GetFullPath(baseDirectory);
        var baseText = File.ReadAllText(ResolveUnder(Path.Combine(baselineRoot, "data", "library"), entry.Path));
        var currentText = File.ReadAllText(ResolveLibraryPath(entry.Path));
        var baselineVersion = NormaliseVersion(ReadField(baseText, "Version"));
        var currentVersion = NormaliseVersion(ReadField(currentText, "Version"));

        if (CompareVersions(currentVersion, baselineVersion) <= 0)
        {
            throw new InvalidDataException("candidate version is not newer than the trusted base");
        }

        if (!OnlyUpdateFieldsChanged(baseText, currentText))
        {
            throw new InvalidDataException("entry changes fields other than Version, Url and Sha256");
        }

        var source = ParseSource(entry.Source);
        var url = ReadField(currentText, "Url");
        ValidateDownloadUrl(entry, source, url, currentVersion);

        if (!Regex.IsMatch(ReadField(currentText, "Sha256"), "^[a-f0-9]{64}$", RegexOptions.CultureInvariant))
        {
            throw new InvalidDataException("Sha256 is not a pinned lowercase SHA-256 hash");
        }

        return new(entry, baseText, currentVersion);
    }

    private CatalogueUpdateEntry[] ReadConfiguration()
    {
        var config = CatalogueUpdateConfiguration.Read(repository);
        if (config.Entries is not { Length: > 0 })
        {
            throw new InvalidDataException("catalogue update configuration has no entries");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var entries = new CatalogueUpdateEntry[config.Entries.Length];
        for (var index = 0; index < config.Entries.Length; index++)
        {
            var entry = config.Entries[index];
            var id = entry?.Id ?? "";
            if (entry is null || !Regex.IsMatch(id, "^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)
                || !ids.Add(id))
            {
                throw new InvalidDataException("catalogue update IDs must be unique lowercase catalogue IDs");
            }

            if (entry.AllowedDownloadHosts is null || entry.AllowedDownloadHosts.Length == 0
                || entry.AllowedDownloadHosts.Any(host => !Regex.IsMatch(host ?? "",
                    "^[a-z0-9.-]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            {
                throw new InvalidDataException($"{entry.Id} has no valid allowed download hosts");
            }

            entries[index] = entry;
        }

        return entries;
    }

    private void ValidateMapping(CatalogueUpdateEntry entry)
    {
        var libraryPath = ResolveLibraryPath(entry.Path);
        if (!Path.GetFileName(libraryPath).Equals(entry.Id + ".yml", StringComparison.Ordinal))
        {
            throw new InvalidDataException("configured entry path does not match its ID");
        }

        var className = entry.Scenario.StartsWith(ScenarioNamespace, StringComparison.Ordinal)
            ? entry.Scenario[ScenarioNamespace.Length..]
            : "";
        if (!Regex.IsMatch(className, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
        {
            throw new InvalidDataException("configured scenario class is invalid");
        }

        var scenarioPath = Path.Combine(repository, "tests", "Cabinet.Runtime.Tests", "Scenarios", className + ".cs");
        if (!File.Exists(scenarioPath)
            || !Regex.IsMatch(File.ReadAllText(scenarioPath),
                $"const\\s+string\\s+Id\\s*=\\s*\"{Regex.Escape(entry.Id)}\";", RegexOptions.CultureInvariant))
        {
            throw new InvalidDataException("configured scenario does not map to the catalogue ID");
        }
    }

    private string ResolveLibraryPath(string path) => ResolveUnder(Path.Combine(repository, "data", "library"), path);

    private static string ResolveUnder(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)
            || relativePath.Split('/', '\\').Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException("configured entry path must be a safe relative path");
        }

        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(fullRoot, StringComparison.Ordinal))
        {
            throw new InvalidDataException("configured entry path escapes its root");
        }

        return fullPath;
    }

    private static CatalogueUpdateSource ParseSource(CatalogueUpdateSource? source)
    {
        if (source is null || string.IsNullOrWhiteSpace(source.Type))
        {
            throw new InvalidDataException("source configuration is missing");
        }

        if (source.Type == "github")
        {
            if (!Regex.IsMatch(source.Repository ?? "", "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant)
                || string.IsNullOrEmpty(source.VersionRegex) || string.IsNullOrEmpty(source.AssetRegex))
            {
                throw new InvalidDataException("GitHub source configuration is invalid");
            }
        }
        else if (source.Type is "vendor-page" or "document" or "redirect" or "debian")
        {
            if (!Uri.TryCreate(source.PageUrl, UriKind.Absolute, out var page)
                || page.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(source.DownloadRegex)
                || source.Type == "document" && string.IsNullOrEmpty(source.ReleaseRegex))
            {
                throw new InvalidDataException("vendor page source configuration is invalid");
            }

            if (source.Type == "debian" && (string.IsNullOrWhiteSpace(source.Package)
                || !Uri.TryCreate(source.DownloadBase, UriKind.Absolute, out var downloadBase)
                || downloadBase.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidDataException("Debian index source configuration is invalid");
            }
        }

        return source;
    }

    private async Task<Release?> GithubRelease(CatalogueUpdateSource source, string[] allowedHosts,
        CancellationToken cancellationToken)
    {
        var versionRegex = CreateRegex(source.VersionRegex!);
        var assetRegex = CreateRegex(source.AssetRegex!);
        var releases = new List<Release>();

        for (var page = 1; page <= 20; page++)
        {
            var endpoint = new Uri($"https://api.github.com/repos/{source.Repository}/releases?per_page=100&page={page}");
            using var response = await Send(endpoint, ["api.github.com"], cancellationToken);
            var body = await ReadDocument(response, cancellationToken);
            using var document = JsonDocument.Parse(body);
            var pageReleases = document.RootElement.EnumerateArray().ToArray();

            foreach (var item in pageReleases)
            {
                if (item.GetProperty("draft").GetBoolean() || item.GetProperty("prerelease").GetBoolean())
                {
                    continue;
                }

                var tag = item.GetProperty("tag_name").GetString() ?? "";
                var tagMatch = versionRegex.Match(tag);
                if (!tagMatch.Success || !tagMatch.Groups["version"].Success)
                {
                    continue;
                }

                var version = NormaliseVersion(tagMatch.Groups["version"].Value);
                var assets = item.GetProperty("assets").EnumerateArray()
                    .Where(asset => assetRegex.IsMatch(asset.GetProperty("name").GetString() ?? ""))
                    .ToArray();
                if (assets.Length > 1)
                {
                    throw new InvalidDataException($"GitHub release {tag} has multiple matching assets");
                }

                if (assets.Length == 1)
                {
                    var asset = assets[0];
                    var url = asset.GetProperty("browser_download_url").GetString() ?? "";
                    var assetMatch = assetRegex.Match(asset.GetProperty("name").GetString() ?? "");
                    if (assetMatch.Groups["version"].Success
                        && CompareVersions(NormaliseVersion(assetMatch.Groups["version"].Value), version) != 0)
                    {
                        continue;
                    }

                    ValidateOrigin(url, allowedHosts);
                    releases.Add(new(version, url));
                }
            }

            if (pageReleases.Length < 100)
            {
                break;
            }

            if (page == 20)
            {
                throw new InvalidDataException("GitHub release history exceeded the discovery limit");
            }
        }

        return SelectNewest(releases);
    }

    private async Task<Release?> VendorRelease(CatalogueUpdateSource source, string[] allowedHosts,
        CancellationToken cancellationToken)
    {
        ValidateOrigin(source.PageUrl!, allowedHosts);
        using var response = await Send(new Uri(source.PageUrl!), allowedHosts, cancellationToken);
        var html = Encoding.UTF8.GetString(await ReadDocument(response, cancellationToken));
        var pageVersion = source.VersionRegex is { Length: > 0 }
            ? CreateRegex(source.VersionRegex).Matches(WebUtility.HtmlDecode(html))
                .Select(MatchedVersion).Distinct(StringComparer.Ordinal).Single()
            : null;
        var links = DownloadLinks(html)
            .Select(link => Uri.TryCreate(response.RequestMessage?.RequestUri ?? new Uri(source.PageUrl!), link, out var uri) ? uri : null)
            .Where(uri => uri is not null)
            .Select(uri => uri!)
            .Select(uri => (Uri: uri, Match: CreateRegex(source.DownloadRegex!).Match(uri.AbsoluteUri)))
            .Where(pair => pair.Match.Success)
            .Select(pair => new Release(pageVersion ?? MatchedVersion(pair.Match), pair.Uri.AbsoluteUri))
            .ToList();

        foreach (var link in links)
        {
            ValidateOrigin(link.Url, allowedHosts);
        }

        return SelectNewest(links);
    }

    private async Task<Release?> RedirectRelease(CatalogueUpdateSource source, string[] allowedHosts,
        CancellationToken cancellationToken)
    {
        var pattern = CreateRegex(source.DownloadRegex!);
        using var response = await Send(new Uri(source.PageUrl!), allowedHosts, cancellationToken, HttpMethod.Head, pattern);
        var uri = response.RequestMessage!.RequestUri!;
        var url = response.Headers.Location is { } location ? new Uri(uri, location).AbsoluteUri : uri.AbsoluteUri;
        var match = CreateRegex(source.DownloadRegex!).Match(url);
        return match.Success ? new Release(MatchedVersion(match), url) : null;
    }

    private async Task<Release?> DebianRelease(CatalogueUpdateSource source, string[] allowedHosts,
        CancellationToken cancellationToken)
    {
        using var response = await Send(new Uri(source.PageUrl!), allowedHosts, cancellationToken);
        if (response.Content.Headers.ContentLength is > MaximumDocumentSize)
        {
            throw new InvalidDataException("Debian index exceeds the compressed size limit");
        }
        await using var compressed = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var expanded = new GZipStream(compressed, CompressionMode.Decompress);
        var document = Encoding.UTF8.GetString(await ReadLimited(expanded, 67_108_864, cancellationToken))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var packages = document.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Where(stanza => stanza.StartsWith($"Package: {source.Package}\n", StringComparison.Ordinal))
            .Where(stanza => ReadField(stanza, "Architecture") == "amd64")
            .Select(stanza =>
            {
                var filename = ReadField(stanza, "Filename");
                if (Path.IsPathRooted(filename) || filename.Split('/').Any(part => part is "." or ".."))
                {
                    throw new InvalidDataException("Debian package filename is not a safe relative path");
                }
                var url = new Uri(new Uri(source.DownloadBase!), filename).AbsoluteUri;
                ValidateOrigin(url, allowedHosts);
                var version = NormaliseVersion(ReadField(stanza, "Version"));
                var urlVersion = MatchedVersion(CreateRegex(source.DownloadRegex!).Match(url));
                if (CompareVersions(version, urlVersion) != 0)
                {
                    throw new InvalidDataException("Debian filename does not match the indexed version");
                }
                return new Release(version, url, ReadField(stanza, "SHA256"));
            });
        return SelectNewest(packages);
    }

    private async Task<Release?> DocumentRelease(CatalogueUpdateSource source, string[] allowedHosts,
        CancellationToken cancellationToken)
    {
        ValidateOrigin(source.PageUrl!, allowedHosts);
        using var response = await Send(new Uri(source.PageUrl!), allowedHosts, cancellationToken);
        var document = Encoding.UTF8.GetString(await ReadDocument(response, cancellationToken)).Replace("\\/", "/", StringComparison.Ordinal);
        var releases = CreateRegex(source.ReleaseRegex!).Matches(document).Select(match =>
        {
            var url = WebUtility.HtmlDecode(match.Groups["url"].Value);
            if (source.RelativePrefix is { Length: > 0 } prefix && source.DownloadBase is { Length: > 0 } root)
            {
                if (!url.StartsWith(prefix, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("published download path does not match its configured prefix");
                }
                url = new Uri(new Uri(root), url[prefix.Length..]).AbsoluteUri;
            }
            ValidateOrigin(url, allowedHosts);
            return new Release(MatchedVersion(match), url);
        });
        return SelectNewest(releases);
    }

    private static string MatchedVersion(Match match)
    {
        var version = match.Groups["version"].Success ? match.Groups["version"].Value
            : string.Join('.', new[] { "major", "minor", "patch" }.Where(name => match.Groups[name].Success)
                .Select(name => match.Groups[name].Value));
        return NormaliseVersion(version);
    }

    private static IEnumerable<string> DownloadLinks(string html)
    {
        foreach (Match tag in CreateRegex("<(?<tag>a|input)\\b(?<attributes>[^>]*)>", RegexOptions.IgnoreCase).Matches(html))
        {
            var attributes = CreateRegex("(?<name>[\\w-]+)\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^\\s\"'=<>`]+))")
                .Matches(tag.Groups["attributes"].Value)
                .GroupBy(match => match.Groups["name"].Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Groups["value"].Value,
                    StringComparer.OrdinalIgnoreCase);
            var link = tag.Groups["tag"].Value.Equals("a", StringComparison.OrdinalIgnoreCase)
                ? attributes.GetValueOrDefault("href")
                : attributes.GetValueOrDefault("name") == "download_url" ? attributes.GetValueOrDefault("value") : null;

            if (link is not null)
            {
                yield return WebUtility.HtmlDecode(link);
            }
        }
    }

    private async Task<string> HashDownload(string url, string[] allowedHosts, CancellationToken cancellationToken)
    {
        ValidateOrigin(url, allowedHosts);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var response = await Send(new Uri(url), allowedHosts, timeout.Token);
        if (response.Content.Headers.ContentLength is > MaximumDownloadSize)
        {
            throw new InvalidDataException("installer exceeds the download size limit");
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        var buffer = new byte[81_920];
        long total = 0;
        while (true)
        {
            var count = await stream.ReadAsync(buffer, timeout.Token);
            if (count == 0)
            {
                break;
            }

            total += count;
            if (total > MaximumDownloadSize)
            {
                throw new InvalidDataException("installer exceeds the download size limit");
            }

            hash.AppendData(buffer, 0, count);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private async Task<HttpResponseMessage> Send(Uri url, IReadOnlyCollection<string> allowedHosts,
        CancellationToken cancellationToken, HttpMethod? method = null, Regex? stopAtRedirect = null)
    {
        for (var redirect = 0; redirect <= 5; redirect++)
        {
            ValidateOrigin(url.AbsoluteUri, allowedHosts);
            using var request = new HttpRequestMessage(method ?? HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Cabinet-Catalogue-Updater");
            request.Headers.Accept.ParseAdd("*/*");
            if (url.Host == "api.github.com" && !string.IsNullOrEmpty(githubToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", githubToken);
            }
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect
                or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                if (location is null || redirect == 5)
                {
                    response.Dispose();
                    throw new HttpRequestException("invalid or excessive HTTP redirect");
                }

                var next = location.IsAbsoluteUri ? location : new Uri(url, location);
                try
                {
                    ValidateOrigin(next.AbsoluteUri, allowedHosts);
                }
                catch
                {
                    response.Dispose();
                    throw;
                }
                if (stopAtRedirect?.IsMatch(next.AbsoluteUri) == true)
                {
                    return response;
                }
                response.Dispose();
                url = next;
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var status = response.StatusCode;
                response.Dispose();
                throw new HttpRequestException($"HTTP request returned {(int)status}");
            }

            return response;
        }

        throw new HttpRequestException("excessive HTTP redirects");
    }

    private static async Task<byte[]> ReadDocument(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > MaximumDocumentSize)
        {
            throw new InvalidDataException("source document exceeds the size limit");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await ReadLimited(source, MaximumDocumentSize, cancellationToken);
    }

    private static async Task<byte[]> ReadLimited(Stream source, int maximum, CancellationToken cancellationToken)
    {
        using var document = new MemoryStream();
        var buffer = new byte[81_920];
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (document.Length + count > maximum)
            {
                throw new InvalidDataException("source document exceeds the size limit");
            }

            document.Write(buffer, 0, count);
        }

        return document.ToArray();
    }

    private static void ValidateDownloadUrl(CatalogueUpdateEntry entry, CatalogueUpdateSource source, string url, string version,
        bool allowTemporary = false)
    {
        ValidateOrigin(url, entry.AllowedDownloadHosts);
        if (!allowTemporary && TemporaryDownload(url))
        {
            throw new InvalidDataException("expiring signed downloads cannot be pinned in the catalogue");
        }
        var uri = new Uri(url);
        if (source.Type == "github")
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.UnescapeDataString).ToArray();
            var repository = source.Repository!.Split('/');
            if (uri.Host != "github.com" || segments.Length != 6
                || segments[0] != repository[0] || segments[1] != repository[1]
                || segments[2] != "releases" || segments[3] != "download"
                || !CreateRegex(source.VersionRegex!).Match(segments[4]).Groups["version"].Success
                || !CreateRegex(source.AssetRegex!).IsMatch(segments[5]))
            {
                throw new InvalidDataException("download URL does not match the configured GitHub release");
            }

            var tagMatch = CreateRegex(source.VersionRegex!).Match(segments[4]);
            var urlVersion = NormaliseVersion(tagMatch.Groups["version"].Value);
            var assetMatch = CreateRegex(source.AssetRegex!).Match(segments[5]);
            if (CompareVersions(urlVersion, version) != 0
                || assetMatch.Groups["version"].Success
                && CompareVersions(NormaliseVersion(assetMatch.Groups["version"].Value), version) != 0)
            {
                throw new InvalidDataException("download URL release version does not match the entry");
            }
        }
        else if (source.Type is "vendor-page" or "document" or "redirect" or "debian")
        {
            var match = CreateRegex(source.DownloadRegex!).Match(uri.AbsoluteUri);
            if (!match.Success)
            {
                throw new InvalidDataException("download URL does not match the configured vendor source");
            }

            var urlVersion = source.VersionRegex is { Length: > 0 } && match.Groups["compact"].Success
                ? version : MatchedVersion(match);
            if (match.Groups["compact"].Success && match.Groups["compact"].Value != version.Replace(".", "", StringComparison.Ordinal))
            {
                throw new InvalidDataException("download filename does not match the published version");
            }
            if (CompareVersions(urlVersion, version) != 0)
            {
                throw new InvalidDataException("download URL version does not match the entry");
            }
        }
        else
        {
            throw new NotSupportedException($"unsupported source type '{source.Type}'");
        }
    }

    private static void ValidateOrigin(string value, IReadOnlyCollection<string> allowedHosts)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || uri.UserInfo.Length != 0 || uri.Port != 443 || !allowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("URL is not an allowed HTTPS origin");
        }
    }

    private static bool TemporaryDownload(string url) =>
        Regex.IsMatch(new Uri(url).Query, "(?:[?&])(?:X-Amz-Signature|X-Amz-Credential|AWSAccessKeyId|Signature)=",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Release? SelectNewest(IEnumerable<Release> releases)
    {
        var ordered = releases.OrderByDescending(release => release.Version,
            Comparer<string>.Create(CompareVersions)).ToArray();
        if (ordered.Length == 0)
        {
            return null;
        }

        var newest = ordered[0];
        if (ordered.Skip(1).Any(other => CompareVersions(other.Version, newest.Version) == 0
            && other.Url != newest.Url))
        {
            throw new InvalidDataException($"multiple downloads match version {newest.Version}");
        }

        return newest;
    }

    private static Regex CreateRegex(string pattern, RegexOptions options = RegexOptions.CultureInvariant) =>
        new(pattern, options, TimeSpan.FromSeconds(2));

    private static string NormaliseVersion(string value)
    {
        var version = value.Replace('_', '.');
        EnsureNumericVersion(version);
        return version;
    }

    private static void EnsureNumericVersion(string value)
    {
        if (!Regex.IsMatch(value, "^(?:0|[1-9][0-9]*)(?:\\.(?:0|[1-9][0-9]*))*(?:-[0-9]+)?\\z", RegexOptions.CultureInvariant))
        {
            throw new InvalidDataException($"version '{value}' is not a stable numeric version");
        }
    }

    private static int CompareVersions(string left, string right)
    {
        var leftVersion = left.Split('-');
        var rightVersion = right.Split('-');
        var leftParts = leftVersion[0].Split('.').Select(part => BigInteger.Parse(part, CultureInfo.InvariantCulture)).ToArray();
        var rightParts = rightVersion[0].Split('.').Select(part => BigInteger.Parse(part, CultureInfo.InvariantCulture)).ToArray();
        for (var index = 0; index < Math.Max(leftParts.Length, rightParts.Length); index++)
        {
            var comparison = (index < leftParts.Length ? leftParts[index] : BigInteger.Zero)
                .CompareTo(index < rightParts.Length ? rightParts[index] : BigInteger.Zero);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        var leftRevision = leftVersion.Length == 2 ? BigInteger.Parse(leftVersion[1], CultureInfo.InvariantCulture) : BigInteger.Zero;
        var rightRevision = rightVersion.Length == 2 ? BigInteger.Parse(rightVersion[1], CultureInfo.InvariantCulture) : BigInteger.Zero;
        return leftRevision.CompareTo(rightRevision);
    }

    private static string ReadField(string content, string name)
    {
        var matches = Regex.Matches(content, $"^{name}:[ \\t]*(.+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        if (matches.Count != 1)
        {
            throw new InvalidDataException($"entry must contain exactly one {name} field");
        }

        return matches[0].Groups[1].Value.Trim();
    }

    private static string UpdateFields(string content, string version, string url, string hash)
    {
        foreach (var (name, value) in new[] { ("Version", version), ("Url", url), ("Sha256", hash) })
        {
            var matches = Regex.Matches(content, $"^{name}:[^\\r\\n]*", RegexOptions.Multiline | RegexOptions.CultureInvariant);
            if (matches.Count != 1)
            {
                throw new InvalidDataException($"entry must contain exactly one {name} field");
            }

            var match = matches[0];
            content = string.Concat(content.AsSpan(0, match.Index), $"{name}: {value}", content.AsSpan(match.Index + match.Length));
        }

        return content;
    }

    private static bool OnlyUpdateFieldsChanged(string baseline, string candidate)
    {
        foreach (var name in new[] { "Version", "Url", "Sha256" })
        {
            if (Regex.Matches(baseline, $"^{name}:[^\\r\\n]*", RegexOptions.Multiline | RegexOptions.CultureInvariant).Count != 1
                || Regex.Matches(candidate, $"^{name}:[^\\r\\n]*", RegexOptions.Multiline | RegexOptions.CultureInvariant).Count != 1)
            {
                throw new InvalidDataException($"entry must contain exactly one {name} field");
            }

            baseline = Regex.Replace(baseline, $"^{name}:[^\\r\\n]*", $"{name}: <updated>", RegexOptions.Multiline | RegexOptions.CultureInvariant);
            candidate = Regex.Replace(candidate, $"^{name}:[^\\r\\n]*", $"{name}: <updated>", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        }

        return baseline == candidate;
    }

    private static void WriteCandidates(string path, IEnumerable<CatalogueUpdateCandidate> candidates)
    {
        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartArray();
        foreach (var candidate in candidates)
        {
            writer.WriteStartObject();
            writer.WriteString("id", candidate.Id);
            writer.WriteString("path", candidate.Path);
            writer.WriteString("version", candidate.Version);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private sealed record ValidatedCatalogueEntry(CatalogueUpdateEntry Entry, string Baseline, string Version);

    private sealed record Release(string Version, string Url, string? Hash = null);
}

internal sealed record CatalogueUpdateCandidate(string Id, string Path, string Version);
