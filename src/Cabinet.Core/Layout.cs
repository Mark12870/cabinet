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

    public Layout(string home, string runtimeDir)
    {
        Home = home;
        RuntimeDir = runtimeDir;
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

        return new Layout(home, runtimeDir);
    }

    public string Home { get; }
    public string RuntimeDir { get; }

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

    /// <summary>The conventional VST3 directory inside a Wine prefix.</summary>
    public string PrefixVst3Dir(string name) =>
        Path.Combine(PrefixPath(name), "drive_c", "Program Files", "Common Files", "VST3");

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
