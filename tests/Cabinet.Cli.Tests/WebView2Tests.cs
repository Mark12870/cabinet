using Cabinet.Core.Tests;

namespace Cabinet.Cli.Tests;

public sealed class WebView2Tests : IDisposable
{
    private readonly Cli cli;

    public WebView2Tests()
    {
        Cli? started = null;
        cli = new Cli(new RecordingRunner(args => WebView2Download.Serving(started!.Layout, "gadget")(args)));
        started = cli;
        Directory.CreateDirectory(Path.Combine(cli.Layout.PrefixPath("gadget"), "dosdevices"));
    }

    public void Dispose() => cli.Dispose();

    [Fact]
    public void SettingWebView2NeedsOnOrOff()
    {
        var outcome = cli.Run("set", "gadget", "webview2");

        Assert.Equal(2, outcome.Exit);
        Assert.Equal("cabinet: expected on or off\n", outcome.Error);
        Assert.Empty(cli.Runner.Ran);
    }

    [Fact]
    public void ShowAndListSayWhichWebView2APrefixHas()
    {
        var installed = cli.Run("set", "gadget", "webview2", "on");

        var prefix = Assert.Single(Parsed.Objects(cli.Run("list", "--json").Out));

        Assert.Contains($"gadget now draws web pages through WebView2 {WebView2Download.Version}.", installed.Out);
        Assert.Contains($"webview2          {WebView2Download.Version}", cli.Run("show", "gadget").Out);
        Assert.Equal(WebView2Download.Version, prefix.GetProperty("webview2").GetString());
    }

    [Fact]
    public void TurningWebView2OffUninstallsIt()
    {
        cli.Run("set", "gadget", "webview2", "on");

        var outcome = cli.Run("set", "gadget", "webview2", "off");

        Assert.Equal(0, outcome.Exit);
        Assert.Contains("gadget no longer holds WebView2.", outcome.Out);
        Assert.Contains("webview2          off", cli.Run("show", "gadget").Out);
    }
}
