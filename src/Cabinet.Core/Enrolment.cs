namespace Cabinet.Core;

public static class Enrolment
{
    public static IReadOnlyList<string> OverrideArguments(string dawId, Layout layout) =>
    [
        "override",
        "--user",
        dawId,
        "--device=shm",
        "--filesystem=xdg-run/yabridge:create",
        $"--filesystem={layout.HostAppFiles}:ro",
        $"--filesystem={layout.PrefixesDir}:ro",
        $"--filesystem={layout.NativeDir}:ro",
        "--talk-name=org.freedesktop.Flatpak",
        $"--env=WINELOADER={layout.ShimPath}",
        $"--env=YABRIDGE_TEMP_DIR={layout.SocketDir}",
        "--env=YABRIDGE_NO_WATCHDOG=1",
    ];

    public static string OverrideCommand(string dawId, Layout layout) =>
        "flatpak " + string.Join(' ', OverrideArguments(dawId, layout).Select(Quote));

    public static string SelfTestCommand(string dawId, Layout layout) =>
        $"flatpak run --command={Quote(layout.ShimPath)} {dawId} --cabinet-self-test";

    public static string Link(string dawId, Layout layout)
    {
        var dataHome = layout.DawDataHome(dawId);

        if (!Directory.Exists(dataHome))
        {
            throw new DirectoryNotFoundException(
                $"{dataHome} does not exist — is {dawId} installed?");
        }

        var link = layout.DawYabridgeLink(dawId);

        if (Path.Exists(link))
        {
            File.Delete(link);
        }

        File.CreateSymbolicLink(link, layout.HostYabridgeDir);
        return link;
    }

    private static string Quote(string argument) =>
        argument.Any(char.IsWhiteSpace) ? $"'{argument}'" : argument;
}
