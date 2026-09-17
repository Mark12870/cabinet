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
        $"--filesystem={layout.BridgeHome}:ro",
        "--talk-name=org.freedesktop.Flatpak",
        $"--env=WINELOADER={layout.ShimPath}",
        $"--env=YABRIDGE_TEMP_DIR={layout.SocketDir}",
        $"--env=YABRIDGE_DEBUG_FILE={layout.RuntimeLogPath}",
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

    public static IReadOnlyList<string> EnsureNativeScanLinks(Layout layout)
    {
        var links = Layout.BridgedScanDirectories
            .Select(directory => (Link: layout.CabinetScanDir(directory),
                Target: layout.BridgeOutputDir(directory)))
            .ToList();
        var conflicts = links
            .Where(entry =>
            {
                var target = LinkTarget(entry.Link);
                return target is not null && target != entry.Target
                       || target is null && Exists(entry.Link);
            })
            .Select(entry => entry.Link)
            .ToList();

        if (conflicts.Count > 0)
        {
            return conflicts;
        }

        foreach (var (link, target) in links)
        {
            Directory.CreateDirectory(target);
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);

            if (LinkTarget(link) is null)
            {
                File.CreateSymbolicLink(link, target);
            }
        }

        return [];
    }

    public static void RemoveLegacyNativeLinks(Layout layout)
    {
        if (!Directory.Exists(layout.NativeYabridgeDir)
            || !Directory.Exists(layout.HostYabridgeDir))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(layout.HostYabridgeDir))
        {
            var link = Path.Combine(layout.NativeYabridgeDir, Path.GetFileName(file));
            var target = LinkTarget(link);
            if (target is not null
                && Path.GetFullPath(target, layout.NativeYabridgeDir) == Path.GetFullPath(file))
            {
                File.Delete(link);
            }
        }
    }

    public static IReadOnlyList<string> PublishNative(Layout layout)
    {
        RemoveLegacyNativeLinks(layout);
        return EnsureNativeScanLinks(layout);
    }

    private static string? LinkTarget(string path) =>
        new FileInfo(path).LinkTarget ?? new DirectoryInfo(path).LinkTarget;

    private static bool Exists(string path) =>
        File.Exists(path) || Directory.Exists(path) || LinkTarget(path) is not null;

    private static string Quote(string argument) =>
        argument.Any(char.IsWhiteSpace) ? $"'{argument}'" : argument;
}
