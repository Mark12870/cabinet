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
        $"--talk-name={Layout.BridgeBusName}",
        $"--env=YABRIDGE_TEMP_DIR={layout.SocketDir}",
    ];

    public static string OverrideCommand(string dawId, Layout layout) =>
        "flatpak " + string.Join(' ', OverrideArguments(dawId, layout).Select(Quote));

    public static string SelfTestCommand(string dawId, Layout layout) =>
        $"flatpak run --command={Quote(layout.ShimPath)} {dawId} --cabinet-self-test";

    public static string TrustBoundary(string dawId) =>
        $"--talk-name={Layout.BridgeBusName} lets {dawId} start Cabinet's Wine, which runs in Cabinet's "
        + $"sandbox, not on your host. {dawId} also loads, natively, whatever is in ~/.vst3, ~/.vst, ~/.clap "
        + "and ~/.lv2, and the Windows installers and plugins Cabinet runs can write there, so enrolling it "
        + "trusts them as much as it trusts "
        + $"{dawId}. They cannot write into any other Flatpak app's data, {dawId}'s included.";

    public static string HostCommandGrant(string dawId, bool bridged) =>
        $"--talk-name={Layout.HostCommandBusName} lets {dawId} run any command on your host; "
        + (bridged ? "Cabinet no longer needs it, and " : "enrol it again, then ")
        + $"`flatpak override --user --no-talk-name={Layout.HostCommandBusName} {dawId}` takes it away";

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
        foreach (var directory in Layout.BridgedScanDirectories)
        {
            if (ScanLinkConflict(layout, directory) is null)
            {
                PlaceBridgeOutput(layout, directory);
            }
            else
            {
                KeepBridgeOutputInCabinet(layout, directory);
            }
        }

        return ScanLinkConflicts(layout);
    }

    public static void MoveBridgeOutputIntoScanDirectories(Layout layout)
    {
        foreach (var directory in Layout.BridgedScanDirectories
                     .Where(directory => ScanLinkConflict(layout, directory) is null))
        {
            var windows = layout.WindowsScanDir(directory);
            var output = layout.BridgeOutputDir(directory);

            if (LinkTarget(windows) == output || !Exists(windows) && IsRealDirectory(output))
            {
                Attempt(() => PlaceBridgeOutput(layout, directory));
            }
        }
    }

    private static void KeepBridgeOutputInCabinet(Layout layout, string directory)
    {
        var output = layout.BridgeOutputDir(directory);

        if (LinkTarget(output) is not null)
        {
            File.Delete(output);
        }

        Directory.CreateDirectory(output);
    }

    private static void PlaceBridgeOutput(Layout layout, string directory)
    {
        var windows = layout.WindowsScanDir(directory);
        var output = layout.BridgeOutputDir(directory);

        if (LinkTarget(windows) == output)
        {
            File.Delete(windows);
        }

        if (!Directory.Exists(windows) && IsRealDirectory(output))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(windows)!);
            Relocation.Move(output, windows);
        }

        Directory.CreateDirectory(windows);

        if (LinkTarget(output) != windows)
        {
            Relocation.Delete(output);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.CreateSymbolicLink(output, windows);
        }
    }

    public static void MoveNativeIntoScanDirectories(Layout layout)
    {
        if (!Directory.Exists(layout.NativeDir))
        {
            return;
        }

        foreach (var root in Directory.EnumerateDirectories(layout.NativeDir)
                     .Where(root => Layout.IsName(Path.GetFileName(root)))
                     .ToList())
        {
            using var underway = Underway.Begin(layout.NativeInstalling(Path.GetFileName(root)));

            if (underway is not null && underway.Left is null)
            {
                Attempt(() => MoveNativeRoot(layout, root));
            }
        }
    }

    public static IReadOnlyList<string> UnmovedNative(Layout layout) =>
        Directory.Exists(layout.NativeDir)
            ? Directory.EnumerateDirectories(layout.NativeDir)
                .Where(root => Layout.IsName(Path.GetFileName(root)))
                .SelectMany(Library.Bundles)
                .Where(bundle => LinkTarget(bundle) is null && LegacyPublished(layout, bundle))
                .Select(layout.NativePlacement)
                .Order(StringComparer.Ordinal)
                .ToList()
            : [];

    private static void MoveNativeRoot(Layout layout, string root)
    {
        foreach (var bundle in Library.Bundles(root).Where(bundle => LinkTarget(bundle) is null).ToList())
        {
            if (LegacyPublished(layout, bundle) && !ForeignCabinetScanDir(layout, Path.GetExtension(bundle)))
            {
                var placed = layout.NativePlacement(bundle);
                File.Delete(placed);
                Attempt(() => Relocation.Move(bundle, placed));

                if (!Relocation.Present(placed))
                {
                    File.CreateSymbolicLink(placed, bundle);
                }
                else if (!Relocation.Present(bundle))
                {
                    File.CreateSymbolicLink(bundle, placed);
                }
            }
        }

        var moved = Library.Bundles(root)
            .Where(bundle => Resolved(bundle) == layout.NativePlacement(bundle))
            .ToDictionary(bundle => bundle, layout.NativePlacement, StringComparer.Ordinal);

        foreach (var bundle in Library.Bundles(root).Where(bundle => LegacyPublished(layout, bundle)).ToList())
        {
            if (Library.Repointed(bundle, moved) is { } target)
            {
                var placed = layout.NativePlacement(bundle);
                File.Delete(placed);
                File.CreateSymbolicLink(placed, target);
                File.Delete(bundle);
                File.CreateSymbolicLink(bundle, placed);
            }
        }
    }

    private static bool LegacyPublished(Layout layout, string bundle) =>
        Resolved(layout.NativePlacement(bundle)) == bundle;

    private static bool ForeignCabinetScanDir(Layout layout, string extension)
    {
        var parent = layout.CabinetScanDir(extension);

        return extension != ".lv2" && (LinkTarget(parent) is not null || File.Exists(parent));
    }

    private static string? Resolved(string link) =>
        LinkTarget(link) is { } target ? Path.GetFullPath(target, Path.GetDirectoryName(link)!) : null;

    private static void Attempt(Action step)
    {
        try
        {
            step();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
        }
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

        var windows = layout.WindowsScanDir(directory);
        var target = LinkTarget(windows);

        return target is not null && target != layout.BridgeOutputDir(directory) || File.Exists(windows)
            ? windows
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

    private static bool IsRealDirectory(string path) => Directory.Exists(path) && LinkTarget(path) is null;

    private static bool Exists(string path) =>
        File.Exists(path) || Directory.Exists(path) || LinkTarget(path) is not null;

    private static string Quote(string argument) =>
        argument.Any(char.IsWhiteSpace) ? $"'{argument}'" : argument;
}
