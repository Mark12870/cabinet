using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class HttpTests : IDisposable
{
    private const string Url = "https://example.invalid/a/b.yml";

    private readonly string root = Directory.CreateTempSubdirectory("cabinet").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Fetched()
    {
        var path = Path.Combine(root, "b.yml");
        File.WriteAllText(path, "");
        return path;
    }

    private static Http Answering(string status, string body) =>
        new(new StubRunner(
            new ProcessResult(0, body, $"HTTP/2 {status} \ncontent-type: text/plain\n")));

    [Fact]
    public void ABodyComesBackWhenTheStatusIsFine()
    {
        Assert.Equal("hello", Answering("200", "hello").Text(Url));
    }

    [Fact]
    public void AnHttpFailureNamesTheStatusRatherThanTheUrl()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => Answering("429", "").Text(Url));

        Assert.Equal("example.invalid answered 429 for /a/b.yml", refused.Message);
    }

    [Fact]
    public void TheLastStatusOfARedirectChainIsTheOneThatCounts()
    {
        var runner = new StubRunner(new ProcessResult(0, "body", "HTTP/2 302 \nHTTP/2 200 \n"));

        Assert.Equal("body", new Http(runner).Text(Url));
    }

    [Fact]
    public void AnUnreachableHostSaysWhatCurlSaid()
    {
        var runner = new StubRunner(
            new ProcessResult(6, "", "curl: (6) Could not resolve host: example.invalid\n"));

        var offline = Assert.Throws<InvalidOperationException>(() => new Http(runner).Text(Url));

        Assert.Equal(
            "could not reach example.invalid — curl: (6) Could not resolve host: example.invalid",
            offline.Message);
    }

    [Fact]
    public void ADownloadAsksCurlForTheMeterRatherThanSilence()
    {
        var runner = new StreamingRunner();

        new Http(runner).ToFile(Url, Fetched());

        Assert.Contains("--progress-bar", runner.LastArguments);
        Assert.DoesNotContain("-sS", runner.LastArguments);
    }

    [Fact]
    public void MeterUpdatesBecomeFractionsAndNeverReachTheLog()
    {
        var fractions = new List<double>();
        var lines = new List<string>();

        Downloading("###   35.9%", "no meter here", "##### 100.0%")
            .ToFile(Url, Fetched(), lines.Add, fractions.Add);

        Assert.Equal(2, fractions.Count);
        Assert.Equal(0.359, fractions[0], 3);
        Assert.Equal(1, fractions[1], 3);
        Assert.Contains("no meter here", lines);
        Assert.DoesNotContain(lines, line => line.EndsWith('%'));
    }

    [Fact]
    public void WithNothingToDrawTheMeterIsSaidOncePerTenPercent()
    {
        var lines = new List<string>();

        Downloading("# 5.0%", "## 12.0%", "### 19.9%", "#### 20.4%")
            .ToFile(Url, Fetched(), lines.Add);

        Assert.Equal(
            ["Downloading… 10%", "Downloading… 20%"],
            lines.Where(line => line.StartsWith("Downloading… ", StringComparison.Ordinal)));
    }

    [Fact]
    public void CurlsOwnDrawingNeverReachesTheLog()
    {
        var lines = new List<string>();

        Downloading("", "#=#=#", "##O#- #", "### 40.0%")
            .ToFile(Url, Fetched(), lines.Add);

        Assert.DoesNotContain("#=#=#", lines);
        Assert.DoesNotContain("##O#- #", lines);
        Assert.DoesNotContain("", lines);
    }

    private static Http Downloading(params string[] lines) => new(new StreamingRunner(lines));
}
