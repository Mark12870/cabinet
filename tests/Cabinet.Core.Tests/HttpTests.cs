using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class HttpTests
{
    private const string Url = "https://example.invalid/a/b.yml";

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
}
