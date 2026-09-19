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

    public static string TrustBoundary(string dawId) =>
        $"--talk-name=org.freedesktop.Flatpak lets {dawId} run any command on your host. {dawId} also "
        + "loads, natively, whatever is in ~/.vst3, ~/.vst, ~/.clap and ~/.lv2, and the Windows installers "
        + "and plugins Cabinet runs can write there, so enrolling it trusts them as much as it trusts "
        + $"{dawId}. They cannot write into any other Flatpak app's data, {dawId}'s included.";

    public static string NotAnAppId(string id) => $"{id} is not a Flatpak application id";

    public static bool IsAppId(string id)
    {
        var parts = id.Split('.');

        return parts.Length >= 3
               && parts.Select((part, index) => (part, last: index == parts.Length - 1))
                   .All(segment => segment.part.Length > 0
                                   && !char.IsAsciiDigit(segment.part[0])
                                   && segment.part.All(c => char.IsAsciiLetterOrDigit(c)
                                                            || c == '_'
                                                            || c == '-' && segment.last));
    }

    public static IReadOnlyList<string> EnsureNativeScanLinks(Layout layout)
    {
        var conflicts = ScanLinkConflicts(layout);

        if (conflicts.Count > 0)
        {
            return conflicts;
        }

        foreach (var directory in Layout.BridgedScanDirectories)
        {
            var link = layout.WindowsScanDir(directory);
            var target = layout.BridgeOutputDir(directory);

            Directory.CreateDirectory(target);
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);

            if (LinkTarget(link) is null)
            {
                File.CreateSymbolicLink(link, target);
            }
        }

        return [];
    }

    public static IReadOnlyList<string> ScanLinkConflicts(Layout layout) =>
        Layout.BridgedScanDirectories
            .Select(directory => ScanLinkConflict(layout, directory))
            .OfType<string>()
            .ToList();

    private static string? ScanLinkConflict(Layout layout, string directory)
    {
        var parent = layout.CabinetScanDir(directory);

        if (LinkTarget(parent) is not null || File.Exists(parent))
        {
            return parent;
        }

        var link = layout.WindowsScanDir(directory);
        var target = LinkTarget(link);

        return target is not null && target != layout.BridgeOutputDir(directory)
               || target is null && Exists(link)
            ? link
            : null;
    }

    public static void MoveLegacyScanLinks(Layout layout)
    {
        foreach (var directory in Layout.BridgedScanDirectories)
        {
            var parent = layout.CabinetScanDir(directory);

            if (LinkTarget(parent) == layout.BridgeOutputDir(directory))
            {
                File.Delete(parent);
                Directory.CreateDirectory(parent);
                File.CreateSymbolicLink(layout.WindowsScanDir(directory), layout.BridgeOutputDir(directory));
            }
        }

        foreach (var extension in Layout.PluginExtensions)
        {
            var scan = layout.ScanDir(extension);
            var native = layout.NativeScanDir(extension);

            if (scan == native || !Directory.Exists(scan))
            {
                continue;
            }

            foreach (var link in Directory.EnumerateFileSystemEntries(scan).ToList())
            {
                var moved = Path.Combine(native, Path.GetFileName(link));

                if (LinkTarget(link) is not { } target
                    || !Path.GetFullPath(target, scan).StartsWith(
                        layout.NativeDir + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    || Exists(moved))
                {
                    continue;
                }

                Directory.CreateDirectory(native);
                File.CreateSymbolicLink(moved, Path.GetFullPath(target, scan));
                File.Delete(link);
            }
        }
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
