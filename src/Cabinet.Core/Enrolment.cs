namespace Cabinet.Core;

/// <summary>
/// What a Flatpak DAW needs before it can bridge anything.
/// </summary>
/// <remarks>
/// The override is <em>printed</em>, never applied. <c>--talk-name=org.freedesktop.Flatpak</c>
/// lets the DAW run arbitrary commands on the host, which is a real weakening of its
/// sandbox and the user's decision to make — not a side effect of running enrol.
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
        // The socket directory, at the same path on both sides of the boundary.
        "--filesystem=xdg-run/yabridge:create",
        // So the shim can reach the host to start the Wine sandbox.
        "--talk-name=org.freedesktop.Flatpak",
        $"--env=WINELOADER={layout.ShimPath}",
        // Spelled out: `flatpak override` does no variable expansion.
        $"--env=YABRIDGE_TEMP_DIR={layout.SocketDir}",
        "--env=YABRIDGE_NO_WATCHDOG=1",
    ];

    /// <summary>The same thing as one pasteable line.</summary>
    public static string OverrideCommand(string dawId, Layout layout) =>
        "flatpak " + string.Join(' ', OverrideArguments(dawId, layout).Select(Quote));

    private static string Quote(string argument) =>
        argument.Any(char.IsWhiteSpace) ? $"'{argument}'" : argument;
}
