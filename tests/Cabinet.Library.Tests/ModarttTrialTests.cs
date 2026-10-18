using Cabinet.Core;

namespace Cabinet.Core.Tests;

public class ModarttTrialTests
{
    private const string Trial = "https://www.modartt.com/try?file=thing_trial_v1.tar.xz";

    private const string Page = """
        <a href="download?file=thing_trial_v1.tar.xz&amp;signature=1790878902:91ae15a0">
        """;

    private const string Cookies = "/staging/cookies.txt";

    private const string Link = "https://www.modartt.com/data/download/gen/dl1/thing_trial_v1.tar.xz";

    private static RecordingRunner Answering(string page, string reply)
    {
        var answers = new Queue<string>([page, reply]);
        return new RecordingRunner(outputs: _ => answers.Dequeue());
    }

    [Theory]
    [InlineData(Trial, true)]
    [InlineData("https://www.modartt.com/download?file=thing_trial_v1.tar.xz", false)]
    [InlineData("http://www.modartt.com/try?file=thing_trial_v1.tar.xz", false)]
    [InlineData("https://example.invalid/try?file=thing_trial_v1.tar.xz", false)]
    public void OnlyModarttsOwnTrialPageIsResolved(string url, bool served)
    {
        Assert.Equal(served, ModarttTrial.Serves(url));
    }

    [Fact]
    public void TheTrialPagesSignatureIsTradedForTheDownloadLink()
    {
        var runner = Answering(Page, $$$"""{"jsonrpc":"2.0","id":1,"result":{"url":"{{{Link}}}"}}""");

        Assert.Equal(Link, ModarttTrial.Resolve(new Http(runner), Trial, Cookies, TestContext.Current.CancellationToken));

        Assert.All(runner.Calls, call => Assert.Equal(
            ["-b", Cookies, "-c", Cookies],
            call.Arguments.SkipWhile(argument => argument != "-b").Take(4)));

        var request = runner.Calls[1].Arguments;
        Assert.Equal("https://www.modartt.com/api/0/download", request[^1]);
        Assert.Equal(
            """{"jsonrpc":"2.0","id":1,"method":"download","params":{"file":"thing_trial_v1.tar.xz","signature":"1790878902:91ae15a0","get":"url"}}""",
            request[request.ToList().IndexOf("--data-binary") + 1]);
    }

    [Fact]
    public void ARefusalSaysWhatModarttSaid()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => ModarttTrial.Resolve(
            new Http(Answering(Page, """{"jsonrpc":"2.0","id":1,"error":{"message":"Signature expired","code":0}}""")),
            Trial,
            Cookies,
            TestContext.Current.CancellationToken));

        Assert.Equal(
            "www.modartt.com would not hand out thing_trial_v1.tar.xz — Signature expired",
            refused.Message);
    }

    [Fact]
    public void ALinkOffModarttIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => ModarttTrial.Resolve(
            new Http(Answering(Page, """{"jsonrpc":"2.0","id":1,"result":{"url":"https://example.invalid/x"}}""")),
            Trial,
            Cookies,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public void AnAnswerThatIsNotJsonIsSaidPlainly()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => ModarttTrial.Resolve(
            new Http(Answering(Page, "<html>maintenance</html>")),
            Trial,
            Cookies,
            TestContext.Current.CancellationToken));

        Assert.Equal(
            "www.modartt.com would not hand out thing_trial_v1.tar.xz — no download link in its answer",
            refused.Message);
    }

    [Fact]
    public void AVersionTheTrialPageNoLongerOffersIsSaidSo()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => ModarttTrial.Resolve(
            new Http(Answering("<html></html>", "")), Trial, Cookies, TestContext.Current.CancellationToken));

        Assert.Equal(
            "www.modartt.com's trial page no longer offers thing_trial_v1.tar.xz",
            refused.Message);
    }
}
