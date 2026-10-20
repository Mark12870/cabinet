using Cabinet.Core;

namespace Cabinet.Core.Tests;

public static class WebView2Download
{
    public const string Version = "141.0.3537.71";

    public static Action<IReadOnlyList<string>> Serving(Layout layout, string prefix) => args =>
    {
        if (args is [.., "-o", var target, WebView2.Url])
        {
            File.WriteAllText(target, "setup");
        }

        if (args is [.., var setup, "/silent", "/install"] && setup.EndsWith(WebView2.Setup, StringComparison.Ordinal))
        {
            Installed(layout, prefix);
        }

        if (args is [.., "--uninstall", "--msedgewebview", "--system-level", "--force-uninstall"])
        {
            Directory.Delete(layout.PrefixWebView2Dir(prefix), recursive: true);
        }
    };

    public static void Installed(Layout layout, string prefix, string version = Version)
    {
        var application = Path.Combine(layout.PrefixWebView2Dir(prefix), version);
        Directory.CreateDirectory(application);
        File.WriteAllText(Path.Combine(application, WebView2.Program), "");
    }
}
