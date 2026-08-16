namespace Cabinet.Core;

/// <summary>
/// Every path Cabinet touches, resolved once.
/// </summary>
/// <remarks>
/// The subtlety worth knowing: Cabinet itself runs inside its own Flatpak, where
/// <c>XDG_DATA_HOME</c> points at <c>~/.var/app/io.github.mark12870.cabinet/data</c>.
/// Almost nothing Cabinet writes belongs there — the yabridge halves it exports are
/// loaded by the DAW, which lives outside the sandbox — so the host locations are
/// built from <c>$HOME</c> literally and never from <c>XDG_DATA_HOME</c>.
/// </remarks>
public sealed class Layout
{
    public const string AppId = "io.github.mark12870.cabinet";

    /// <summary>Where the Flatpak carries the yabridge release it was built with.</summary>
    public const string BundledYabridgeDir = "/app/lib/yabridge";

    /// <summary>
    /// Wine inside this sandbox, pinned rather than left to the environment.
    /// </summary>
    /// <remarks>
    /// <c>setup</c> puts <c>WINELOADER=…/cabinet-wine</c> in the login session for
    /// natively installed DAWs, and <c>flatpak run</c> forwards it straight back in
    /// here — so anything Cabinet starts would exec the shim and try to re-enter its
    /// own sandbox. Cabinet <em>is</em> the far side of that boundary; it runs Wine.
    /// </remarks>
    public const string Wine = "/app/bin/wine";

    public Layout(string home, string runtimeDir, string? sandboxDataHome = null)
    {
        Home = home;
        RuntimeDir = runtimeDir;
        SandboxDataHome = sandboxDataHome
                          ?? Path.Combine(home, ".var", "app", AppId, "data");
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
            home, runtimeDir, Environment.GetEnvironmentVariable("XDG_DATA_HOME"));
    }

    public string Home { get; }
    public string RuntimeDir { get; }

    /// <summary>
    /// Cabinet's own <c>XDG_DATA_HOME</c>, inside the sandbox — the one place a
    /// sandbox-local path is what is wanted rather than a host one.
    /// </summary>
    public string SandboxDataHome { get; }

    /// <summary>
    /// Where <c>yabridgectl</c> looks for yabridge, and therefore a link to
    /// <see cref="YabridgeDir"/> that <c>cabinet setup</c> has to create.
    /// </summary>
    /// <remarks>
    /// The obvious alternative, <c>yabridgectl set --path=</c>, cannot be used: in
    /// yabridge 5.1.1 every <c>set</c> invocation panics inside clap, because
    /// <c>--path-auto</c> is declared as taking a value and then read as a flag.
    /// Linking is also the same mechanism <c>enrol</c> already uses for a DAW.
    /// </remarks>
    public string SandboxYabridgeLink => Path.Combine(SandboxDataHome, "yabridge");

    /// <summary>Deliberately literal, not <c>XDG_DATA_HOME</c>. See the class remarks.</summary>
    public string HostDataHome => Path.Combine(Home, ".local", "share");

    /// <summary>Where the DAW-side yabridge halves are exported to.</summary>
    public string YabridgeDir => Path.Combine(HostDataHome, "yabridge");

    /// <summary>
    /// One Wine prefix per plugin lives here. Not under <c>~/.var/app</c>, so
    /// <c>flatpak uninstall --delete-data</c> cannot take a plugin library with it.
    /// </summary>
    public string PrefixesDir => Path.Combine(HostDataHome, "cabinet", "prefixes");

    public string ShimPath => Path.Combine(Home, ".local", "bin", "cabinet-wine");

    /// <summary>yabridge's <c>YABRIDGE_TEMP_DIR</c>: its sockets, shared with the DAW.</summary>
    public string SocketDir => Path.Combine(RuntimeDir, "yabridge");

    public string EnvironmentDFile =>
        Path.Combine(Home, ".config", "environment.d", "50-cabinet.conf");

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
    /// A Flatpak DAW's <c>XDG_DATA_HOME</c>. yabridge's chainloader looks for
    /// <c>yabridge-host.exe</c> there, so a link has to exist in it — reaching
    /// <c>~/.local/share/yabridge</c> is not enough for a sandboxed DAW.
    /// </summary>
    public string DawDataHome(string flatpakId) =>
        Path.Combine(Home, ".var", "app", flatpakId, "data");

    public string DawYabridgeLink(string flatpakId) =>
        Path.Combine(DawDataHome(flatpakId), "yabridge");
}
