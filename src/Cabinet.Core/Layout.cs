namespace Cabinet.Core;

/// <summary>
/// Every path Cabinet touches, resolved once.
/// </summary>
/// <remarks>
/// Cabinet keeps its own things inside its own Flatpak directory, the way Bottles does —
/// prefixes included. It copies nothing onto the host: the yabridge halves the DAW loads
/// are read straight out of the installed Flatpak's <c>files/</c>, which is world-readable
/// at a path flatpak keeps stable across updates. That is why there is no <c>setup</c>
/// step and why updating Cabinet updates yabridge with no further action.
/// </remarks>
public sealed class Layout
{
    public const string AppId = "io.github.mark12870.cabinet";

    /// <summary>Where the Flatpak carries yabridge, seen from inside the sandbox.</summary>
    public const string BundledYabridgeDir = "/app/lib/yabridge";

    /// <summary>
    /// Wine inside this sandbox, pinned rather than left to the environment.
    /// </summary>
    /// <remarks>
    /// A DAW's <c>WINELOADER</c> points at the shim, and <c>flatpak run</c> forwards that
    /// variable straight back in here — so anything Cabinet starts would exec the shim and
    /// try to re-enter its own sandbox. Cabinet <em>is</em> the far side of that boundary.
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

        // Same path inside and outside the sandbox, which is what lets the socket
        // directory be shared across the boundary at all. Always set inside a
        // Flatpak, and Cabinet runs nowhere else, so this is a hard requirement
        // rather than something to guess a fallback for.
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")
                         ?? throw new InvalidOperationException("XDG_RUNTIME_DIR is not set");

        return new Layout(
            home,
            runtimeDir,
            Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
            HostAppFilesFromFlatpakInfo(home));
    }

    public string Home { get; }
    public string RuntimeDir { get; }

    /// <summary>Cabinet's own <c>XDG_DATA_HOME</c>: everything it owns lives under this.</summary>
    public string SandboxDataHome { get; }

    /// <summary>
    /// The installed Flatpak's <c>files/</c>, as the <em>host</em> sees it.
    /// </summary>
    /// <remarks>
    /// Deliberately the <c>current/active</c> alias rather than the content-addressed path
    /// <c>/.flatpak-info</c> reports: the latter carries a commit hash and changes on every
    /// update, which would bake a dead path into a DAW's <c>flatpak override</c>.
    /// </remarks>
    public string HostAppFiles { get; }

    /// <summary>What a DAW loads: the chainloaders, libyabridge and the Wine-side hosts.</summary>
    public string HostYabridgeDir => Path.Combine(HostAppFiles, "lib", "yabridge");

    /// <summary>The shim, at the DAW's <c>WINELOADER</c>. Read in place, never copied.</summary>
    public string ShimPath => Path.Combine(HostYabridgeDir, "cabinet-wine");

    /// <summary>
    /// Where <c>yabridgectl</c> looks for yabridge, so a link to
    /// <see cref="HostYabridgeDir"/> has to exist here.
    /// </summary>
    /// <remarks>
    /// The obvious alternative, <c>yabridgectl set --path=</c>, cannot be used: in
    /// yabridge 5.1.1 every <c>set</c> invocation panics inside clap, because
    /// <c>--path-auto</c> is declared as taking a value and then read as a flag.
    /// </remarks>
    public string SandboxYabridgeLink => Path.Combine(SandboxDataHome, "yabridge");

    /// <summary>
    /// One Wine prefix per plugin, inside Cabinet's own data directory.
    /// </summary>
    /// <remarks>
    /// In scope on purpose, as Bottles keeps its bottles. The cost is real and worth
    /// knowing: <c>flatpak uninstall --delete-data</c> takes the plugin library with it.
    /// </remarks>
    public string PrefixesDir => Path.Combine(SandboxDataHome, "prefixes");

    /// <summary>yabridge's <c>YABRIDGE_TEMP_DIR</c>: its sockets, shared with the DAW.</summary>
    public string SocketDir => Path.Combine(RuntimeDir, "yabridge");

    public string PrefixPath(string name) => Path.Combine(PrefixesDir, name);

    /// <summary>
    /// The conventional VST3 directory inside a Wine prefix, and where a plugin that
    /// ships as a plain folder should be unpacked.
    /// </summary>
    public string PrefixVst3Dir(string name) =>
        Path.Combine(PrefixPath(name), "drive_c", ProgramFiles64, "Common Files", "VST3");

    /// <summary>
    /// Every conventional plugin location inside a prefix, both bitnesses.
    /// </summary>
    /// <remarks>
    /// A 64-bit installer writes under <c>Program Files</c> and a 32-bit one under
    /// <c>Program Files (x86)</c> — the same plugin, two directories, and registering
    /// only the first is why a 32-bit build would never be bridged.
    /// </remarks>
    public IEnumerable<string> PrefixPluginDirs(string name)
    {
        var driveC = Path.Combine(PrefixPath(name), "drive_c");

        foreach (var programFiles in new[] { ProgramFiles64, ProgramFiles32 })
        {
            yield return Path.Combine(driveC, programFiles, "Common Files", "VST3");
            yield return Path.Combine(driveC, programFiles, "Common Files", "CLAP");
            // VST2 has no standard location; this is the one every installer offers.
            yield return Path.Combine(driveC, programFiles, "VstPlugins");
        }
    }

    private const string ProgramFiles64 = "Program Files";
    private const string ProgramFiles32 = "Program Files (x86)";

    /// <summary>
    /// A Flatpak DAW's <c>XDG_DATA_HOME</c>. yabridge's chainloader has its search path
    /// compiled in — <c>/usr/lib</c>, <c>/usr/local/lib*</c> and
    /// <c>$XDG_DATA_HOME/yabridge</c>, with no environment override — so for a sandboxed
    /// DAW a link has to exist in exactly this directory and nowhere else.
    /// </summary>
    public string DawDataHome(string flatpakId) =>
        Path.Combine(Home, ".var", "app", flatpakId, "data");

    public string DawYabridgeLink(string flatpakId) =>
        Path.Combine(DawDataHome(flatpakId), "yabridge");

    private static string DefaultHostAppFiles(string home) => Path.Combine(
        home, ".local", "share", "flatpak", "app", AppId, "current", "active", "files");

    /// <summary>
    /// Resolves <see cref="HostAppFiles"/> from <c>/.flatpak-info</c>, which is the only
    /// thing that knows whether this is a user or a system installation.
    /// </summary>
    private static string? HostAppFilesFromFlatpakInfo(string home)
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
    /// Turns <c>…/app/&lt;id&gt;/&lt;arch&gt;/&lt;branch&gt;/&lt;commit&gt;/files</c> into
    /// <c>…/app/&lt;id&gt;/current/active/files</c>, the alias flatpak repoints on update.
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
