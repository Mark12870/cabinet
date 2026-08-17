namespace Cabinet.Core;

/// <summary>
/// Every path Cabinet touches, resolved once.
/// </summary>
/// <remarks>
/// Cabinet owns nothing outside its own Flatpak directory, prefixes included. It copies
/// nothing onto the host either: the DAW reads yabridge straight out of the installed
/// Flatpak's <c>files/</c>, which is why there is no setup step.
/// </remarks>
public sealed class Layout
{
    public const string AppId = "io.github.mark12870.cabinet";

    public const string BundledYabridgeDir = "/app/lib/yabridge";

    /// <remarks>
    /// Pinned because a DAW's <c>WINELOADER</c> points at the shim and <c>flatpak run</c>
    /// forwards it back in here, so anything Cabinet starts would try to re-enter its own
    /// sandbox.
    /// </remarks>
    public const string Wine = "/app/bin/wine";

    public Layout(
        string home,
        string runtimeDir,
        string? sandboxDataHome = null,
        string? hostAppFiles = null)
    {
        Home = home;
        RuntimeDir = runtimeDir;
        SandboxDataHome = sandboxDataHome ?? Path.Combine(home, ".var", "app", AppId, "data");
        HostAppFiles = hostAppFiles ?? DefaultHostAppFiles(home);
    }

    public static Layout FromEnvironment()
    {
        var home = Environment.GetEnvironmentVariable("HOME")
                   ?? throw new InvalidOperationException("HOME is not set");

        // Same path inside and outside every sandbox, which is what makes the socket
        // directory shareable at all.
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")
                         ?? throw new InvalidOperationException("XDG_RUNTIME_DIR is not set");

        return new Layout(
            home,
            runtimeDir,
            Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
            HostAppFilesFromFlatpakInfo());
    }

    public string Home { get; }
    public string RuntimeDir { get; }
    public string SandboxDataHome { get; }

    /// <summary>The installed Flatpak's <c>files/</c>, as the host sees it.</summary>
    public string HostAppFiles { get; }

    public string HostYabridgeDir => Path.Combine(HostAppFiles, "lib", "yabridge");

    public string ShimPath => Path.Combine(HostYabridgeDir, "cabinet-wine");

    /// <remarks>
    /// yabridgectl only searches here, and <c>yabridgectl set --path=</c> cannot be used
    /// instead: in yabridge 5.1.1 every <c>set</c> invocation panics inside clap.
    /// </remarks>
    public string SandboxYabridgeLink => Path.Combine(SandboxDataHome, "yabridge");

    /// <remarks>
    /// In scope, as Bottles keeps its bottles — so <c>flatpak uninstall --delete-data</c>
    /// takes the plugin library with it, and an enrolled DAW needs an explicit grant to
    /// read this.
    /// </remarks>
    public string PrefixesDir => Path.Combine(SandboxDataHome, "prefixes");

    public string SocketDir => Path.Combine(RuntimeDir, "yabridge");

    public string PrefixPath(string name) => Path.Combine(PrefixesDir, name);

    /// <summary>Also where a plugin shipping as a plain folder should be unpacked.</summary>
    public string PrefixVst3Dir(string name) =>
        Path.Combine(PrefixPath(name), "drive_c", ProgramFiles64, "Common Files", "VST3");

    /// <remarks>
    /// Both bitnesses: a 32-bit installer writes under <c>Program Files (x86)</c>, and
    /// registering only the 64-bit locations leaves those plugins unbridged silently.
    /// </remarks>
    public IEnumerable<string> PrefixPluginDirs(string name)
    {
        var driveC = Path.Combine(PrefixPath(name), "drive_c");

        foreach (var programFiles in new[] { ProgramFiles64, ProgramFiles32 })
        {
            // `VstPlugins` is not the only VST2 convention: Aalto installs to
            // `Common Files\VST2`, and an unregistered location fails silently — `sync`
            // reports success and the plugin simply never appears.
            yield return Path.Combine(driveC, programFiles, "Common Files", "VST2");
            yield return Path.Combine(driveC, programFiles, "Common Files", "VST3");
            yield return Path.Combine(driveC, programFiles, "Common Files", "CLAP");
            yield return Path.Combine(driveC, programFiles, "VstPlugins");
        }
    }

    private const string ProgramFiles64 = "Program Files";
    private const string ProgramFiles32 = "Program Files (x86)";

    public string DawDataHome(string flatpakId) =>
        Path.Combine(Home, ".var", "app", flatpakId, "data");

    /// <remarks>
    /// The chainloader's search path is compiled in — <c>/usr/lib</c>,
    /// <c>/usr/local/lib*</c> and <c>$XDG_DATA_HOME/yabridge</c>, no environment override —
    /// so for a sandboxed DAW the link has to be exactly here.
    /// </remarks>
    public string DawYabridgeLink(string flatpakId) =>
        Path.Combine(DawDataHome(flatpakId), "yabridge");

    private static string DefaultHostAppFiles(string home) => Path.Combine(
        home, ".local", "share", "flatpak", "app", AppId, "current", "active", "files");

    private static string? HostAppFilesFromFlatpakInfo()
    {
        if (!File.Exists("/.flatpak-info"))
        {
            return null;
        }

        var appPath = IniFile.Parse(File.ReadAllLines("/.flatpak-info"))
            .Get("Instance", "app-path");

        return appPath is null ? null : StableAlias(appPath) ?? appPath;
    }

    /// <summary>
    /// Rewrites the content-addressed <c>app-path</c> to the <c>current/active</c> alias
    /// flatpak repoints on update — the reported one carries a commit hash, so baking it
    /// into a DAW's override would break at the next release.
    /// </summary>
    private static string? StableAlias(string appPath)
    {
        for (var dir = new DirectoryInfo(appPath); dir is not null; dir = dir.Parent)
        {
            if (dir.Name == AppId)
            {
                return Path.Combine(dir.FullName, "current", "active", "files");
            }
        }

        return null;
    }
}
