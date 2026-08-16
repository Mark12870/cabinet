namespace Cabinet.Core;

/// <summary>
/// What a Flatpak DAW needs before it can bridge anything.
/// </summary>
/// <remarks>
/// The override is <em>printed</em>, never applied. <c>--talk-name=org.freedesktop.Flatpak</c>
/// lets the DAW run arbitrary commands on the host, which is the user's decision to make.
/// </remarks>
public static class Enrolment
{
    public static IReadOnlyList<string> OverrideArguments(string dawId, Layout layout) =>
    [
        "override",
        "--user",
        dawId,
        // yabridge's audio buffers are shm_open(), and `--device=all` does not cover this.
        "--device=shm",
        "--filesystem=xdg-run/yabridge:create",
        // The chainloader and the shim. Both grants are needed because flatpak masks
        // ~/.local/share/flatpak and another app's ~/.var/app even under --filesystem=home.
        $"--filesystem={layout.HostAppFiles}:ro",
        // The plugins: yabridgectl's bundles symlink into the prefix, and libyabridge walks
        // up from the .dll for `dosdevices`, both in the DAW's process. Wine does the
        // writing from Cabinet's own sandbox, so read-only is enough.
        $"--filesystem={layout.PrefixesDir}:ro",
        "--talk-name=org.freedesktop.Flatpak",
        $"--env=WINELOADER={layout.ShimPath}",
        // Spelled out: `flatpak override` does no variable expansion.
        $"--env=YABRIDGE_TEMP_DIR={layout.SocketDir}",
        "--env=YABRIDGE_NO_WATCHDOG=1",
    ];

    public static string OverrideCommand(string dawId, Layout layout) =>
        "flatpak " + string.Join(' ', OverrideArguments(dawId, layout).Select(Quote));

    /// <summary>
    /// Runs the shim's self-test inside the DAW's own sandbox, which may be an older
    /// runtime than the shim was built against and is not otherwise checkable from here.
    /// </summary>
    public static string SelfTestCommand(string dawId, Layout layout) =>
        $"flatpak run --command={Quote(layout.ShimPath)} {dawId} --cabinet-self-test";

    private static string Quote(string argument) =>
        argument.Any(char.IsWhiteSpace) ? $"'{argument}'" : argument;
}
