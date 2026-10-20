using Cabinet.Core;

namespace Cabinet.Core.Tests;

public sealed class WebView2Tests : IDisposable
{
    private readonly string root = TestRoot.Create("webview2");

    public void Dispose() => Directory.Delete(root, recursive: true);

    private Layout Layout => new(root, Path.Combine(root, "run"), Path.Combine(root, "data"));

    private WebView2 Subject => new(Layout, new UnusedRunner());

    private RecordingRunner Serving => new(WebView2Download.Serving(Layout, "gadget"));

    [Fact]
    public void APrefixReportsNoWebView2UntilItsRuntimeIsThere()
    {
        Booted("gadget");

        Assert.Null(Subject.InstalledIn("gadget"));
    }

    [Fact]
    public void TheNewestRuntimeFolderHoldingTheProgramIsTheVersion()
    {
        Booted("gadget");
        WebView2Download.Installed(Layout, "gadget", "99.0.1150.30");
        WebView2Download.Installed(Layout, "gadget", "141.0.3537.71");
        Directory.CreateDirectory(Path.Combine(Layout.PrefixWebView2Dir("gadget"), "150.0.1.1"));

        Assert.Equal("141.0.3537.71", Subject.InstalledIn("gadget"));
    }

    [Fact]
    public void APrefixThatWasNeverBootedIsRefused()
    {
        Directory.CreateDirectory(Layout.PrefixPath("gadget"));

        Assert.Throws<DirectoryNotFoundException>(() => Subject.Install("gadget"));
        Assert.Throws<DirectoryNotFoundException>(() => Subject.Remove("gadget"));
    }

    [Fact]
    public void TheStandaloneRuntimeIsInstalledSilentlyFromMicrosoft()
    {
        Booted("gadget");
        var recorder = Serving;

        var version = new WebView2(Layout, recorder).Install("gadget");

        var curl = Assert.Single(recorder.Calls, call => call.File == "curl");
        var setup = Assert.Single(recorder.Ran, call => call.Arguments is [.., "/silent", "/install"]);
        Assert.Equal(WebView2.Url, curl.Arguments[^1]);
        Assert.StartsWith("Z:" + Layout.TempDir.Replace('/', '\\'), setup.Arguments[^3]);
        Assert.Equal(Layout.PrefixPath("gadget"), setup.Environment["WINEPREFIX"]);
        Assert.Equal(WebView2Download.Version, version);
    }

    [Fact]
    public void TheInstallerSeesWindows10AndThePrefixGetsItsOwnVersionBack()
    {
        Booted("gadget");
        File.WriteAllText(Layout.PrefixUserReg("gadget"), "[Software\\\\Wine] 1\n\"Version\"=\"win7\"\n");
        var recorder = Serving;

        new WebView2(Layout, recorder).Install("gadget");

        var changes = recorder.Ran.Where(call => call.Arguments is [Prefixes.JoinMode, "reg", "add", ..]).Select(call => call.Arguments[^2]).ToList();
        var setup = recorder.Ran.ToList().FindIndex(call => call.Arguments is [.., "/silent", "/install"]);
        var lowered = recorder.Ran.ToList().FindIndex(call => call.Arguments is [Prefixes.JoinMode, "reg", "add", .., "win10", "/f"]);
        Assert.Equal(["win10", "win7"], changes);
        Assert.InRange(lowered, 0, setup);
    }

    [Fact]
    public void APrefixWithNoWindowsVersionOfItsOwnLosesTheInstallersAgain()
    {
        Booted("gadget");
        var recorder = Serving;

        new WebView2(Layout, recorder).Install("gadget");

        Assert.Single(recorder.Ran, call => call.Arguments is [Prefixes.JoinMode, "reg", "delete", @"HKCU\Software\Wine", "/v", "Version", "/f"]);
    }

    [Fact]
    public void AnInstallerThatLeavesNoRuntimeFails()
    {
        Booted("gadget");

        var failure = Assert.Throws<InvalidOperationException>(
            () => new WebView2(Layout, new RecordingRunner()).Install("gadget"));

        Assert.Contains("left no msedgewebview2.exe in 'gadget'", failure.Message);
    }

    [Fact]
    public void RemovingRunsTheUninstallerOfTheInstalledVersion()
    {
        Booted("gadget");
        WebView2Download.Installed(Layout, "gadget");
        var recorder = Serving;

        new WebView2(Layout, recorder).Remove("gadget");

        var uninstall = Assert.Single(recorder.Ran, call => call.Arguments is [_, "--uninstall", ..]);
        Assert.Equal($@"{Layout.WebView2Dir}\{WebView2Download.Version}\Installer\setup.exe", uninstall.Arguments[0]);
        Assert.Null(Subject.InstalledIn("gadget"));
    }

    [Fact]
    public void RemovingAPrefixWithoutWebView2IsRefused()
    {
        Booted("gadget");

        Assert.Throws<InvalidOperationException>(() => Subject.Remove("gadget"));
    }

    private void Booted(string name) =>
        Directory.CreateDirectory(Path.Combine(Layout.PrefixPath(name), "dosdevices"));
}
