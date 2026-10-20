namespace Cabinet.Core;

public sealed class WebView2(Layout layout, IProcessRunner runner)
{
    private readonly Http http = new(runner);

    public const string Url = "https://go.microsoft.com/fwlink/?linkid=2124701";

    public const string Setup = "MicrosoftEdgeWebView2RuntimeInstallerX64.exe";

    public const string Program = "msedgewebview2.exe";

    public const string WindowsVersion = @"HKCU\Software\Wine\Version";

    public const string LowestWindows = "win10";

    public string? InstalledIn(string prefix)
    {
        var runtime = layout.PrefixWebView2Dir(prefix);

        return Directory.Exists(runtime)
            ? Directory.EnumerateDirectories(runtime)
                .Where(version => File.Exists(Path.Combine(version, Program)))
                .Select(Path.GetFileName)
                .Where(version => Version.TryParse(version, out _))
                .OrderBy(version => Version.Parse(version!))
                .LastOrDefault()
            : null;
    }

    public string Install(
        string prefix, Action<string>? onOutput = null, Action<double>? onProgress = null)
    {
        Initialised(prefix);

        var prefixes = new Prefixes(layout, runner);
        using var claim = prefixes.Claim(prefix, $"put WebView2 into {prefix}");

        using (var staging = Staging.Create(layout.TempDir, "webview2"))
        {
            var setup = Path.Combine(staging.Path, Setup);
            onOutput?.Invoke(Checksum.Unverifiable(Url));
            http.ToFile(Url, setup, onOutput, onProgress);

            onOutput?.Invoke("Installing the Microsoft Edge WebView2 Runtime");
            var registry = new PrefixRegistry(layout, runner);
            var chosen = registry.Lookup(prefix, @"Software\Wine", "Version");
            registry.Set(prefix, WindowsVersion, LowestWindows);
            ProcessResult installed;

            try
            {
                installed = prefixes.Run(prefix, WindowsPath(setup), ["/silent", "/install"], onOutput);
                prefixes.Run(prefix, "wineserver", ["-k"]);
            }
            finally
            {
                if (chosen is null)
                {
                    registry.Remove(prefix, WindowsVersion);
                }
                else
                {
                    registry.Set(prefix, WindowsVersion, chosen);
                }
            }

            if (!installed.Ok)
            {
                throw new InvalidOperationException(
                    $"The WebView2 Runtime installer exited with {installed.ExitCode} in '{prefix}'");
            }
        }

        var version = InstalledIn(prefix)
            ?? throw new InvalidOperationException(
                $"The WebView2 Runtime installer finished, but left no {Program} in '{prefix}'");

        onOutput?.Invoke($"{prefix} now draws web pages through WebView2 {version}.");
        return version;
    }

    public void Remove(string prefix, Action<string>? onOutput = null)
    {
        Initialised(prefix);

        var version = InstalledIn(prefix)
            ?? throw new InvalidOperationException(
                $"'{prefix}' holds no WebView2 Runtime — there is nothing to take out");

        var prefixes = new Prefixes(layout, runner);
        using var claim = prefixes.Claim(prefix, $"take WebView2 out of {prefix}");

        var uninstaller = $@"{Layout.WebView2Dir}\{version}\Installer\setup.exe";
        var removed = prefixes.Run(
            prefix, uninstaller, ["--uninstall", "--msedgewebview", "--system-level", "--force-uninstall"], onOutput);
        prefixes.Run(prefix, "wineserver", ["-k"]);

        if (InstalledIn(prefix) is not null)
        {
            throw new InvalidOperationException(
                $"The WebView2 Runtime uninstaller exited with {removed.ExitCode} and left WebView2 in '{prefix}'");
        }

        onOutput?.Invoke($"{prefix} no longer holds WebView2.");
    }

    private void Initialised(string prefix)
    {
        if (!Directory.Exists(Path.Combine(layout.PrefixPath(prefix), "dosdevices")))
        {
            throw new DirectoryNotFoundException(
                $"no initialised prefix '{prefix}' — create it first");
        }
    }

    private static string WindowsPath(string unixPath) => "Z:" + unixPath.Replace('/', '\\');
}
