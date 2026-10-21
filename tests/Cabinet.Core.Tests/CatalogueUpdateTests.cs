using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cabinet.Ci;

namespace Cabinet.Core.Tests;

public sealed class CatalogueUpdateTests
{
    [Fact]
    public async Task DiscoveryWritesOnlyNewStableCandidatesAndPreservesEntryContent()
    {
        using var fixture = new Fixture();
        var original = File.ReadAllText(fixture.EntryPath);
        using var client = new HttpClient(new StubHandler(request => Route(request.RequestUri!.Host, new Dictionary<string, Func<HttpResponseMessage>>
        {
            ["api.github.com"] = () => JsonResponse(ReleaseList()),
            ["github.com"] = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("installer payload"))
            }
        })));
        var service = fixture.Service(client);
        var output = Path.Combine(fixture.Root, "output");

        var candidates = await service.Discover(output, TestContext.Current.CancellationToken);

        var candidate = Assert.Single(candidates);
        Assert.Equal("plugin", candidate.Id);
        Assert.Equal("vendor/plugin.yml", candidate.Path);
        Assert.Equal("3.0.0", candidate.Version);
        Assert.Equal(original, File.ReadAllText(fixture.EntryPath));
        var candidateYaml = File.ReadAllText(Path.Combine(output, "plugin.yml"));
        Assert.Contains("Version: 3.0.0", candidateYaml);
        Assert.Contains("Url: https://github.com/acme/plugin/releases/download/v3.0.0/Plugin-3.0.0.zip", candidateYaml);
        Assert.Contains($"Sha256: {Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("installer payload")))}", candidateYaml);
        Assert.Contains("Summary: Preserved summary", candidateYaml);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "candidates.json")));
        Assert.Equal("plugin", manifest.RootElement[0].GetProperty("id").GetString());
        Assert.Equal("3.0.0", manifest.RootElement[0].GetProperty("version").GetString());
    }

    [Fact]
    public async Task MetadataCheckDoesNotDownloadAnArtifact()
    {
        using var fixture = new Fixture();
        var requests = new List<(string Method, string Host)>();
        using var client = new HttpClient(new StubHandler(request =>
        {
            requests.Add((request.Method.Method, request.RequestUri!.Host));
            return JsonResponse(ReleaseList());
        }));
        var output = Path.Combine(fixture.Root, "output");

        var candidates = await fixture.Service(client).Discover(output, TestContext.Current.CancellationToken,
            createCandidates: false);

        Assert.Empty(candidates);
        Assert.Equal(new[] { ("GET", "api.github.com") }, requests);
        Assert.False(File.Exists(Path.Combine(output, "plugin.yml")));
        var status = ReadCoverageEntry(output);
        Assert.Equal("newer", status.GetProperty("outcome").GetString());
        Assert.Equal("3.0.0", status.GetProperty("availableVersion").GetString());
    }

    [Fact]
    public async Task MixedClapAndVst3CurrentEntryRemainsDiscoverableWithoutAnArtifactDownload()
    {
        using var fixture = new Fixture();
        fixture.ConfigureEntry("3.0.0", "CLAP, VST3");
        var requests = new List<string>();
        using var client = new HttpClient(new StubHandler(request =>
        {
            requests.Add(request.RequestUri!.Host);
            return JsonResponse(ReleaseList());
        }));
        var output = Path.Combine(fixture.Root, "output");

        var candidates = await fixture.Service(client).Discover(output, TestContext.Current.CancellationToken);

        Assert.Empty(candidates);
        Assert.Equal(new[] { "api.github.com" }, requests);
        var status = ReadCoverageEntry(output);
        Assert.Equal("current", status.GetProperty("outcome").GetString());
        Assert.Contains("excluded runtime format: CLAP",
            status.GetProperty("automationBlockers").EnumerateArray().Select(item => item.GetString()!));
    }

    [Fact]
    public async Task UnsupportedCurrentVersionStillRecordsTheUpstreamObservation()
    {
        using var fixture = new Fixture();
        fixture.ConfigureEntry("3.0beta", "VST3");
        var requests = new List<string>();
        using var client = new HttpClient(new StubHandler(request =>
        {
            requests.Add(request.RequestUri!.Host);
            return JsonResponse(ReleaseList());
        }));
        var output = Path.Combine(fixture.Root, "output");

        var candidates = await fixture.Service(client).Discover(output, TestContext.Current.CancellationToken);

        Assert.Empty(candidates);
        Assert.Equal(new[] { "api.github.com" }, requests);
        var status = ReadCoverageEntry(output);
        Assert.Equal("observed", status.GetProperty("outcome").GetString());
        Assert.Equal("3.0.0", status.GetProperty("availableVersion").GetString());
        Assert.Contains("unsupported-version",
            status.GetProperty("automationBlockers").EnumerateArray().Select(item => item.GetString()!));
    }

    [Fact]
    public async Task MissingScenarioStillRecordsMetadataWithoutDownloadingAnArtifact()
    {
        using var fixture = new Fixture();
        fixture.RemoveScenarioMapping();
        var requests = new List<string>();
        using var client = new HttpClient(new StubHandler(request =>
        {
            requests.Add(request.RequestUri!.Host);
            return JsonResponse(ReleaseList());
        }));
        var output = Path.Combine(fixture.Root, "output");

        var candidates = await fixture.Service(client).Discover(output, TestContext.Current.CancellationToken);

        Assert.Empty(candidates);
        Assert.Equal(new[] { "api.github.com" }, requests);
        var status = ReadCoverageEntry(output);
        Assert.Equal("newer", status.GetProperty("outcome").GetString());
        Assert.Equal("3.0.0", status.GetProperty("availableVersion").GetString());
        Assert.Contains("runtime scenario missing",
            status.GetProperty("automationBlockers").EnumerateArray().Select(item => item.GetString()!));
    }

    [Fact]
    public async Task VendorPageDiscoveryNormalisesUnderscoreVersions()
    {
        using var fixture = new Fixture();
        fixture.ConfigureVendorPage(
            "https://vendor.example/plugins",
            "Plugin-(?<version>[0-9]+(?:_[0-9]+)*)\\.zip$",
            "vendor.example",
            "downloads.example");
        using var client = new HttpClient(new StubHandler(request => Route(request.RequestUri!.Host, new Dictionary<string, Func<HttpResponseMessage>>
        {
            ["vendor.example"] = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "<a href=\"https://downloads.example/Plugin-2_3_0.zip\">Download</a>",
                    Encoding.UTF8,
                    "text/html")
            },
            ["downloads.example"] = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("vendor installer"))
            }
        })));

        var candidates = await fixture.Service(client).Discover(Path.Combine(fixture.Root, "output"), TestContext.Current.CancellationToken);

        var candidate = Assert.Single(candidates);
        Assert.Equal("2.3.0", candidate.Version);
        Assert.Contains("Version: 2.3.0", File.ReadAllText(Path.Combine(fixture.Root, "output", "plugin.yml")));
        Assert.Contains("Url: https://downloads.example/Plugin-2_3_0.zip", File.ReadAllText(Path.Combine(fixture.Root, "output", "plugin.yml")));
    }

    [Fact]
    public async Task DiscoveryReportsRedirectsToDisallowedHostsBeforeRequestingThem()
    {
        using var fixture = new Fixture();
        fixture.ConfigureVendorPage(
            "https://vendor.example/plugins",
            "Plugin-(?<version>[0-9]+(?:_[0-9]+)*)\\.zip$",
            "vendor.example");
        var requestedHosts = new List<string>();
        using var client = new HttpClient(new StubHandler(request =>
        {
            requestedHosts.Add(request.RequestUri!.Host);
            return new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://untrusted.example/plugins") }
            };
        }));

        var output = Path.Combine(fixture.Root, "output");
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service(client).Discover(output, TestContext.Current.CancellationToken));

        Assert.Equal(new[] { "vendor.example" }, requestedHosts);
        AssertFailedCoverage(output, "URL is not an allowed HTTPS origin");
    }

    [Fact]
    public async Task DiscoveryDoesNotDownloadAssetsFromDisallowedHosts()
    {
        using var fixture = new Fixture();
        var requestedHosts = new List<string>();
        using var client = new HttpClient(new StubHandler(request =>
        {
            requestedHosts.Add(request.RequestUri!.Host);
            return JsonResponse(ReleaseList(
                "https://untrusted.example/acme/plugin/releases/download/v3.0.0/Plugin-3.0.0.zip"));
        }));

        var output = Path.Combine(fixture.Root, "output");
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service(client).Discover(output, TestContext.Current.CancellationToken));

        Assert.Equal(new[] { "api.github.com" }, requestedHosts);
        AssertFailedCoverage(output, "URL is not an allowed HTTPS origin");
    }

    [Fact]
    public async Task ExplicitlyBlockedEntriesAreReportedWithoutUpstreamRequests()
    {
        using var fixture = new Fixture();
        fixture.AddBlockedEntry();
        var requestedHosts = new List<string>();
        using var client = new HttpClient(new StubHandler(request =>
        {
            requestedHosts.Add(request.RequestUri!.Host);
            return JsonResponse(ReleaseList());
        }));
        var output = Path.Combine(fixture.Root, "output");

        var candidates = await fixture.Service(client).Discover(output, TestContext.Current.CancellationToken, createCandidates: false);

        Assert.Empty(candidates);
        Assert.Equal(new[] { "api.github.com" }, requestedHosts);
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "coverage.json")));
        var blocked = report.RootElement.GetProperty("entries").EnumerateArray()
            .Single(entry => entry.GetProperty("id").GetString() == "blocked-plugin");
        Assert.Equal("blocked", blocked.GetProperty("discovery").GetString());
        Assert.Equal("not-run", blocked.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task GitHubTokenIsForwardedOnlyToTheGitHubApi()
    {
        using var fixture = new Fixture();
        var requests = new List<(string Host, string? Authorization)>();
        using var client = new HttpClient(new StubHandler(request =>
        {
            requests.Add((request.RequestUri!.Host, request.Headers.Authorization?.ToString()));
            return Route(request.RequestUri.Host, new Dictionary<string, Func<HttpResponseMessage>>
            {
                ["api.github.com"] = () => JsonResponse(ReleaseList()),
                ["github.com"] = () => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes("installer payload"))
                }
            });
        }));

        var candidates = await fixture.Service(client, githubToken: "fixture-token")
            .Discover(Path.Combine(fixture.Root, "output"), TestContext.Current.CancellationToken);

        Assert.Single(candidates);
        Assert.Equal(
            new (string Host, string? Authorization)[]
            {
                ("api.github.com", "Bearer fixture-token"),
                ("github.com", null)
            },
            requests);
    }

    [Fact]
    public async Task DiscoveryIgnoresPrereleasesAndNonNumericReleaseTags()
    {
        using var fixture = new Fixture();
        using var client = new HttpClient(new StubHandler(_ => JsonResponse("""
        [
            {"draft":false,"prerelease":true,"tag_name":"v9.0.0","assets":[{"name":"Plugin-9.0.0.zip","browser_download_url":"https://github.com/acme/plugin/releases/download/v9.0.0/Plugin-9.0.0.zip"}]},
            {"draft":false,"prerelease":false,"tag_name":"Nightly","assets":[{"name":"Plugin-99.0.0.zip","browser_download_url":"https://github.com/acme/plugin/releases/download/Nightly/Plugin-99.0.0.zip"}]},
            {"draft":false,"prerelease":false,"tag_name":"v1.0.0","assets":[{"name":"Plugin-1.0.0.zip","browser_download_url":"https://github.com/acme/plugin/releases/download/v1.0.0/Plugin-1.0.0.zip"}]}
        ]
        """)));

        var candidates = await fixture.Service(client).Discover(Path.Combine(fixture.Root, "output"), TestContext.Current.CancellationToken);

        Assert.Empty(candidates);
    }

    [Theory]
    [InlineData("<input type='hidden' name='download_url' value='https://vendor.example/Plugin_5_0_0.zip'>")]
    [InlineData("<input value='https://vendor.example/Plugin_5_0_0.zip' name='download_url' type='hidden'>")]
    public async Task VendorDownloadInputsAreReadRegardlessOfAttributeOrder(string html)
    {
        using var fixture = new Fixture();
        fixture.ConfigureVendorPage("https://vendor.example/downloads/",
            "^https://vendor\\.example/Plugin_(?<version>[0-9]+_[0-9]+_[0-9]+)\\.zip$", "vendor.example");
        using var client = new HttpClient(new StubHandler(request => Route(request.RequestUri!.AbsolutePath, new Dictionary<string, Func<HttpResponseMessage>>
        {
            ["/downloads/"] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) }
        }, () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("installer payload") })));

        var candidates = await fixture.Service(client).Discover(Path.Combine(fixture.Root, "output"), TestContext.Current.CancellationToken);

        Assert.Equal("5.0.0", Assert.Single(candidates).Version);
    }

    [Fact]
    public async Task VendorPageVersionAndUnquotedCompactLinkProduceThePublishedVersion()
    {
        using var fixture = new Fixture();
        fixture.ConfigureVendorPageWithVersion("https://vendor.example/plugins",
            "^https://downloads\\.example/Plugin-(?<compact>[0-9]+)\\.zip$",
            "Version: (?<version>[0-9]+\\.[0-9]+\\.[0-9]+)", "vendor.example", "downloads.example");
        var requests = new List<string>();
        using var client = new HttpClient(new StubHandler(request =>
        {
            requests.Add(request.RequestUri!.Host);
            return Route(request.RequestUri.Host, new Dictionary<string, Func<HttpResponseMessage>>
            {
                ["vendor.example"] = () => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "Version: 2.3.0 <a href=https://downloads.example/Plugin-230.zip href=https://downloads.example/Plugin-999.zip>Download</a>",
                        Encoding.UTF8, "text/html")
                },
                ["downloads.example"] = () => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes("vendor installer"))
                }
            });
        }));
        var output = Path.Combine(fixture.Root, "output");

        var candidates = await fixture.Service(client).Discover(output, TestContext.Current.CancellationToken);

        Assert.Equal("2.3.0", Assert.Single(candidates).Version);
        Assert.Equal(new[] { "vendor.example", "downloads.example" }, requests);
        Assert.Contains("Url: https://downloads.example/Plugin-230.zip",
            File.ReadAllText(Path.Combine(output, "plugin.yml")));
    }

    [Fact]
    public async Task VendorPageRejectsCompactFilenameThatDisagreesWithPageVersion()
    {
        using var fixture = new Fixture();
        fixture.ConfigureVendorPageWithVersion("https://vendor.example/plugins",
            "^https://downloads\\.example/Plugin-(?<compact>[0-9]+)\\.zip$",
            "Version: (?<version>[0-9]+\\.[0-9]+\\.[0-9]+)", "vendor.example", "downloads.example");
        var requests = new List<string>();
        using var client = new HttpClient(new StubHandler(request =>
        {
            requests.Add(request.RequestUri!.Host);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "Version: 2.3.0 <a href=https://downloads.example/Plugin-231.zip>Download</a>",
                    Encoding.UTF8, "text/html")
            };
        }));
        var output = Path.Combine(fixture.Root, "output");

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service(client).Discover(output, TestContext.Current.CancellationToken));

        Assert.Equal(new[] { "vendor.example" }, requests);
        AssertFailedCoverage(output, "download filename does not match the published version");
    }

    [Fact]
    public async Task DocumentPublishedRelativePathMapsToTheConfiguredDownloadBase()
    {
        using var fixture = new Fixture();
        fixture.ConfigureSource(new
        {
            Type = "document",
            PageUrl = "https://vendor.example/releases",
            ReleaseRegex = "version=(?<version>[0-9.]+);path=(?<url>official/Plugin-[0-9.]+\\.zip)",
            DownloadRegex = "^https://cdn\\.example/files/Plugin-(?<version>[0-9.]+)\\.zip$",
            DownloadBase = "https://cdn.example/files/",
            RelativePrefix = "official/"
        }, "vendor.example", "cdn.example");
        var requests = new List<string>();
        using var client = new HttpClient(new StubHandler(request =>
        {
            requests.Add(request.RequestUri!.Host);
            return Route(request.RequestUri.Host, new Dictionary<string, Func<HttpResponseMessage>>
            {
                ["vendor.example"] = () => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("version=2.0.0;path=official/Plugin-2.0.0.zip")
                },
                ["cdn.example"] = () => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes("document installer"))
                }
            });
        }));
        var output = Path.Combine(fixture.Root, "output");

        var candidates = await fixture.Service(client).Discover(output, TestContext.Current.CancellationToken);

        Assert.Equal("2.0.0", Assert.Single(candidates).Version);
        Assert.Equal(new[] { "vendor.example", "cdn.example" }, requests);
        Assert.Contains("Url: https://cdn.example/files/Plugin-2.0.0.zip",
            File.ReadAllText(Path.Combine(output, "plugin.yml")));
    }

    [Fact]
    public async Task DocumentRejectsPublishedPathOutsideConfiguredPrefix()
    {
        using var fixture = new Fixture();
        fixture.ConfigureSource(new
        {
            Type = "document",
            PageUrl = "https://vendor.example/releases",
            ReleaseRegex = "version=(?<version>[0-9.]+);path=(?<url>other/Plugin-[0-9.]+\\.zip)",
            DownloadRegex = "^https://cdn\\.example/files/Plugin-(?<version>[0-9.]+)\\.zip$",
            DownloadBase = "https://cdn.example/files/",
            RelativePrefix = "official/"
        }, "vendor.example", "cdn.example");
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("version=2.0.0;path=other/Plugin-2.0.0.zip")
        }));
        var output = Path.Combine(fixture.Root, "output");

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service(client).Discover(output, TestContext.Current.CancellationToken));

        AssertFailedCoverage(output, "published download path does not match its configured prefix");
    }

    [Fact]
    public async Task RedirectSourceDownloadsStableTarget()
    {
        using var fixture = new Fixture();
        fixture.ConfigureSource(new
        {
            Type = "redirect",
            PageUrl = "https://redirect.example/latest",
            DownloadRegex = "^https://downloads\\.example/Plugin-(?<version>[0-9.]+)\\.zip$"
        }, "redirect.example", "downloads.example");
        var requests = new List<(string Method, string Host)>();
        using var client = new HttpClient(new StubHandler(request =>
        {
            requests.Add((request.Method.Method, request.RequestUri!.Host));
            return new Dictionary<HttpMethod, Func<HttpResponseMessage>>
            {
                [HttpMethod.Head] = () => new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers = { Location = new Uri("https://downloads.example/Plugin-2.0.0.zip") }
                },
                [HttpMethod.Get] = () => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes("redirect installer"))
                }
            }[request.Method]();
        }));

        var candidates = await fixture.Service(client).Discover(Path.Combine(fixture.Root, "output"), TestContext.Current.CancellationToken);

        Assert.Equal("2.0.0", Assert.Single(candidates).Version);
        Assert.Equal(new[] { ("HEAD", "redirect.example"), ("GET", "downloads.example") }, requests);
    }

    [Fact]
    public async Task RedirectSourceBlocksExpiringTargetWithoutRequestingTheArtifact()
    {
        using var fixture = new Fixture();
        fixture.ConfigureSource(new
        {
            Type = "redirect",
            PageUrl = "https://redirect.example/latest",
            DownloadRegex = "^https://downloads\\.example/Plugin-(?<version>[0-9.]+)\\.zip(?:\\?.*)?$"
        }, "redirect.example", "downloads.example");
        var requests = new List<(string Method, string Host)>();
        using var client = new HttpClient(new StubHandler(request =>
        {
            requests.Add((request.Method.Method, request.RequestUri!.Host));
            return new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://downloads.example/Plugin-2.0.0.zip?X-Amz-Signature=fixture") }
            };
        }));
        var output = Path.Combine(fixture.Root, "output");

        var candidates = await fixture.Service(client).Discover(output, TestContext.Current.CancellationToken);

        Assert.Empty(candidates);
        Assert.Equal(new[] { ("HEAD", "redirect.example") }, requests);
        var status = ReadCoverageEntry(output);
        Assert.Equal("newer", status.GetProperty("outcome").GetString());
        Assert.Contains("temporary-download-url",
            status.GetProperty("automationBlockers").EnumerateArray().Select(item => item.GetString()!));
    }

    [Fact]
    public async Task DebianIndexSelectsExactAmd64PackageAndComparesNumericRevision()
    {
        using var fixture = new Fixture();
        var payload = Encoding.UTF8.GetBytes("debian installer");
        var checksum = Convert.ToHexStringLower(SHA256.HashData(payload));
        fixture.ConfigureSource(DebianSource(), "debian.example", "cdn.example");
        var requests = new List<string>();
        using var client = new HttpClient(new StubHandler(request =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            return ByHost(request, new Dictionary<string, byte[]>
            {
                ["debian.example"] = DebianIndex(checksum, DebianAlternates),
                ["cdn.example"] = payload
            });
        }));
        var output = Path.Combine(fixture.Root, "output");

        var candidates = await fixture.Service(client).Discover(output, TestContext.Current.CancellationToken);

        Assert.Equal("2.0.0-10", Assert.Single(candidates).Version);
        Assert.Equal(new[]
        {
            "https://debian.example/Packages.gz",
            "https://cdn.example/pool/plugin_2.0.0-10_amd64.deb"
        }, requests);
        var candidate = File.ReadAllText(Path.Combine(output, "plugin.yml"));
        Assert.Contains("Version: 2.0.0-10", candidate);
        Assert.Contains($"Sha256: {checksum}", candidate);
    }

    [Fact]
    public async Task DebianDiscoveryFailsWhenDownloadedHashDoesNotMatchTheIndex()
    {
        using var fixture = new Fixture();
        fixture.ConfigureSource(DebianSource(), "debian.example", "cdn.example");
        using var client = new HttpClient(new StubHandler(request => ByHost(request, new Dictionary<string, byte[]>
        {
            ["debian.example"] = DebianIndex(new string('0', 64), ""),
            ["cdn.example"] = Encoding.UTF8.GetBytes("debian installer")
        })));
        var output = Path.Combine(fixture.Root, "output");

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service(client).Discover(output, TestContext.Current.CancellationToken));

        AssertFailedCoverage(output, "download hash does not match the release metadata");
    }

    [Fact]
    public async Task CancellationPropagatesAfterWritingFailedCoverage()
    {
        using var fixture = new Fixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var client = new HttpClient(new StubHandler(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }));
        var output = Path.Combine(fixture.Root, "output");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service(client)
            .Discover(output, cancellation.Token));

        var status = ReadCoverageEntry(output);
        Assert.Equal("failed", status.GetProperty("outcome").GetString());
        Assert.Equal("upstream check cancelled", status.GetProperty("error").GetString());
        Assert.Contains("upstream check cancelled", File.ReadAllText(Path.Combine(output, "coverage.md")));
        Assert.True(File.Exists(Path.Combine(output, "coverage.md")));
        Assert.True(File.Exists(Path.Combine(output, "candidates.json")));
    }

    [Fact]
    public void ValidationReturnsOnlyMappedScenarioAfterCheckingTheTrustedBase()
    {
        using var fixture = new Fixture();
        var baseDirectory = fixture.WriteBaseline();
        fixture.WriteCandidate("1.1.0", "1.1.0");

        var scenario = fixture.Service(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))))
            .Validate(baseDirectory, "plugin");

        Assert.Equal("Cabinet.Runtime.Tests.Scenarios.PluginScenario", scenario);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("0.9.0")]
    public void ValidationRejectsVersionsThatAreNotNewerThanTheTrustedBase(string version)
    {
        using var fixture = new Fixture();
        var baseDirectory = fixture.WriteBaseline();
        fixture.WriteCandidate(version, version);
        var service = fixture.Service(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));

        Assert.Throws<InvalidDataException>(() => service.Validate(baseDirectory, "plugin"));
    }

    [Fact]
    public void ValidationRejectsCandidateUrlVersionsThatDoNotMatchTheEntryVersion()
    {
        using var fixture = new Fixture();
        var baseDirectory = fixture.WriteBaseline();
        fixture.WriteCandidate("1.1.0", "1.0.0");
        var service = fixture.Service(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));

        Assert.Throws<InvalidDataException>(() => service.Validate(baseDirectory, "plugin"));
    }

    [Fact]
    public void ValidationRejectsChangesOutsideTheUpdateFields()
    {
        using var fixture = new Fixture();
        var baseDirectory = fixture.WriteBaseline();
        fixture.WriteCandidate("1.1.0", "1.1.0", "Changed");
        var service = fixture.Service(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));

        Assert.Throws<InvalidDataException>(() => service.Validate(baseDirectory, "plugin"));
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static string ReleaseList(string assetUrl = "https://github.com/acme/plugin/releases/download/v3.0.0/Plugin-3.0.0.zip") =>
        $$"""
        [
            {"draft":false,"prerelease":false,"tag_name":"v3.0.0","assets":[{"name":"Plugin-3.0.0.zip","browser_download_url":"{{assetUrl}}"}]},
            {"draft":false,"prerelease":true,"tag_name":"v9.0.0","assets":[{"name":"Plugin-9.0.0.zip","browser_download_url":"https://github.com/acme/plugin/releases/download/v9.0.0/Plugin-9.0.0.zip"}]},
            {"draft":false,"prerelease":false,"tag_name":"Nightly","assets":[{"name":"Plugin-99.0.0.zip","browser_download_url":"https://github.com/acme/plugin/releases/download/Nightly/Plugin-99.0.0.zip"}]},
            {"draft":false,"prerelease":false,"tag_name":"v2.0.0","assets":[{"name":"Plugin-2.0.0.zip","browser_download_url":"https://github.com/acme/plugin/releases/download/v2.0.0/Plugin-2.0.0.zip"}]}
        ]
        """;

    private static JsonElement ReadCoverageEntry(string output)
    {
        using var coverage = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "coverage.json")));
        return coverage.RootElement.GetProperty("entries")[0].Clone();
    }

    private static void AssertFailedCoverage(string output, string error)
    {
        var status = ReadCoverageEntry(output);
        Assert.Equal("failed", status.GetProperty("outcome").GetString());
        Assert.Contains(error, status.GetProperty("error").GetString()!);
        Assert.Contains(error, File.ReadAllText(Path.Combine(output, "coverage.md")));
    }

    private static object DebianSource() => new
    {
        Type = "debian",
        PageUrl = "https://debian.example/Packages.gz",
        Package = "plugin",
        DownloadBase = "https://cdn.example/",
        DownloadRegex = "^https://cdn\\.example/pool/plugin_(?<version>[0-9.]+-[0-9]+)_amd64\\.deb$"
    };

    private static HttpResponseMessage Route(string key, Dictionary<string, Func<HttpResponseMessage>> routes) =>
        Route(key, routes, () => new HttpResponseMessage(HttpStatusCode.NotFound));

    private static HttpResponseMessage Route(string key, Dictionary<string, Func<HttpResponseMessage>> routes,
        Func<HttpResponseMessage> fallback) =>
        routes.GetValueOrDefault(key, fallback)();

    private static HttpResponseMessage ByHost(HttpRequestMessage request, Dictionary<string, byte[]> bodies) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(bodies[request.RequestUri!.Host]) };

    private const string DebianAlternates = """
        Package: plugin
        Architecture: i386
        Version: 9.0.0-99
        Filename: pool/plugin_9.0.0-99_i386.deb
        SHA256: 0000000000000000000000000000000000000000000000000000000000000000

        Package: plugin-extra
        Architecture: amd64
        Version: 99.0.0-99
        Filename: pool/plugin_99.0.0-99_amd64.deb
        SHA256: 0000000000000000000000000000000000000000000000000000000000000000

        Package: plugin
        Architecture: amd64
        Version: 2.0.0-9
        Filename: pool/plugin_2.0.0-9_amd64.deb
        SHA256: 0000000000000000000000000000000000000000000000000000000000000000


        """;

    private static byte[] DebianIndex(string checksum, string alternates)
    {
        var document = alternates + $$"""
            Package: plugin
            Architecture: amd64
            Version: 2.0.0-10
            Filename: pool/plugin_2.0.0-10_amd64.deb
            SHA256: {{checksum}}
            """;
        using var compressed = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(compressed,
            System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)))
        {
            writer.Write(document);
        }
        return compressed.ToArray();
    }

    private sealed class Fixture : IDisposable
    {
        private const string Entry = "Name: Test Plugin\nKind: native\nSource: download\nVersion: 1.0.0\nFormats: VST3\nSummary: Preserved summary\nUrl: https://github.com/acme/plugin/releases/download/v1.0.0/Plugin-1.0.0.zip\nSha256: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n";

        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"cabinet-catalogue-update-{Guid.NewGuid():N}");
            EntryPath = Path.Combine(Root, "data", "library", "vendor", "plugin.yml");
            Directory.CreateDirectory(Path.GetDirectoryName(EntryPath)!);
            Directory.CreateDirectory(Path.Combine(Root, "tests", "Cabinet.Runtime.Tests", "Scenarios"));
            File.WriteAllText(EntryPath, Entry);
            File.WriteAllText(Path.Combine(Root, "tests", "Cabinet.Runtime.Tests", "Scenarios", "PluginScenario.cs"),
                "internal sealed class PluginScenario { private const string Id = \"plugin\"; private static readonly Dictionary<string, string> Bridges = new() { [\"VST3\"] = \"Plugin.vst3\", }; }");
            File.WriteAllText(Path.Combine(Root, "tests", "Cabinet.Runtime.Tests", "Scenarios", "ScenarioHarness.cs"),
                "private static readonly Dictionary<string, (string Extension, string Carla)> Formats = new() { [\"VST3\"] = (\".vst3\", \"vst3\"), };");
            File.WriteAllText(Path.Combine(Root, "tests", "Cabinet.Runtime.Tests", "Scenarios", "InstalledEntry.cs"),
                "public static TheoryData<string> Formats(string id) => [.. Find(id).Formats.Where(format => format != \"CLAP\")];");
            ConfigureGithub("github.com");
        }

        public string Root { get; }
        public string EntryPath { get; }
        private string ConfigurationPath => Path.Combine(Root, "data", "catalogue-updates.json");

        public void ConfigureGithub(params string[] allowedHosts) => WriteConfiguration(
            allowedHosts,
            new
            {
                Type = "github",
                Repository = "acme/plugin",
                VersionRegex = "^v?(?<version>[0-9]+(?:\\.[0-9]+)*)$",
                AssetRegex = "^Plugin-(?<version>[0-9]+(?:\\.[0-9]+)*)\\.zip$"
            });

        public void ConfigureVendorPage(string pageUrl, string downloadRegex, params string[] allowedHosts) => WriteConfiguration(
            allowedHosts,
            new
            {
                Type = "vendor-page",
                PageUrl = pageUrl,
                DownloadRegex = downloadRegex
            });

        public void ConfigureVendorPageWithVersion(string pageUrl, string downloadRegex, string versionRegex,
            params string[] allowedHosts) => WriteConfiguration(
                allowedHosts,
                new
                {
                    Type = "vendor-page",
                    PageUrl = pageUrl,
                    DownloadRegex = downloadRegex,
                    VersionRegex = versionRegex
                });

        public void ConfigureSource(object source, params string[] allowedHosts) => WriteConfiguration(allowedHosts, source);

        public void AddBlockedEntry()
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(EntryPath)!, "blocked-plugin.yml"),
                "Name: Blocked plugin\nSource: download\nVersion: 1.0.0\nFormats: VST3\n");
            using var configuration = JsonDocument.Parse(File.ReadAllText(ConfigurationPath));
            File.WriteAllText(ConfigurationPath, JsonSerializer.Serialize(new
            {
                entries = configuration.RootElement.GetProperty("Entries").Clone(),
                blocked = new[] { new { id = "blocked-plugin", reason = "publisher does not expose a versioned download" } }
            }));
        }

        public void ConfigureEntry(string version, string formats)
        {
            var entry = File.ReadAllText(EntryPath)
                .Replace("Version: 1.0.0", $"Version: {version}", StringComparison.Ordinal)
                .Replace("Url: https://github.com/acme/plugin/releases/download/v1.0.0/Plugin-1.0.0.zip",
                    $"Url: https://github.com/acme/plugin/releases/download/v{version}/Plugin-{version}.zip",
                    StringComparison.Ordinal)
                .Replace("Formats: VST3", $"Formats: {formats}", StringComparison.Ordinal);
            File.WriteAllText(EntryPath, entry);
        }

        public void RemoveScenarioMapping() => File.WriteAllText(
            Path.Combine(Root, "tests", "Cabinet.Runtime.Tests", "Scenarios", "PluginScenario.cs"),
            "internal sealed class PluginScenario { private static readonly Dictionary<string, string> Bridges = new() { [\"VST3\"] = \"Plugin.vst3\", }; }");

        public CatalogueUpdateService Service(HttpClient client, string? githubToken = null) =>
            new(client, Root, ConfigurationPath, githubToken: githubToken);

        public string WriteBaseline()
        {
            var baseDirectory = Path.Combine(Root, "base");
            var baseEntry = Path.Combine(baseDirectory, "vendor", "plugin.yml");
            Directory.CreateDirectory(Path.GetDirectoryName(baseEntry)!);
            File.WriteAllText(baseEntry, Entry);
            return baseDirectory;
        }

        public void WriteCandidate(string version, string urlVersion, string summary = "Preserved summary")
        {
            var candidate = Entry
                .Replace("Version: 1.0.0", $"Version: {version}", StringComparison.Ordinal)
                .Replace("Url: https://github.com/acme/plugin/releases/download/v1.0.0/Plugin-1.0.0.zip",
                    $"Url: https://github.com/acme/plugin/releases/download/v{urlVersion}/Plugin-{urlVersion}.zip",
                    StringComparison.Ordinal)
                .Replace(new string('a', 64), new string('b', 64), StringComparison.Ordinal)
                .Replace("Summary: Preserved summary", $"Summary: {summary}", StringComparison.Ordinal);
            File.WriteAllText(EntryPath, candidate);
        }

        public void Dispose() => Directory.Delete(Root, true);

        private void WriteConfiguration(string[] allowedHosts, object source)
        {
            File.WriteAllText(ConfigurationPath, JsonSerializer.Serialize(new
            {
                Entries = new[]
                {
                    new
                    {
                        Id = "plugin",
                        Path = "vendor/plugin.yml",
                        Scenario = "Cabinet.Runtime.Tests.Scenarios.PluginScenario",
                        AllowedDownloadHosts = allowedHosts,
                        Source = source
                    }
                }
            }));
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = respond(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }
}
