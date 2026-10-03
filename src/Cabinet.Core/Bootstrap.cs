namespace Cabinet.Core;

public static class Bootstrap
{
    public static void Ensure(Layout layout)
    {
        Directory.CreateDirectory(layout.PrefixesDir);
        Directory.CreateDirectory(layout.SocketDir);
        LogFile.Rotate(layout.RuntimeLogPath);
        LinkYabridgeForYabridgectl(layout);
        Enrolment.MoveLegacyScanLinks(layout);

        foreach (var extension in Layout.PluginExtensions)
        {
            Staging.Sweep(layout.NativeScanDir(extension));
        }

        foreach (var directory in Layout.BridgedScanDirectories)
        {
            Staging.Sweep(layout.CabinetScanDir(directory));
        }

        Enrolment.MoveBridgeOutputIntoScanDirectories(layout);
        Enrolment.MoveNativeIntoScanDirectories(layout);

        foreach (var parent in new[]
                 {
                     layout.TempDir, layout.RunnersDir, layout.PrefixesDir, layout.NativeDir,
                 })
        {
            Staging.Sweep(parent);
        }
    }

    private static void LinkYabridgeForYabridgectl(Layout layout)
    {
        Link(layout.SandboxYabridgeLink, layout.HostYabridgeDir);

        Directory.CreateDirectory(layout.BridgeDataHome);
        Link(layout.BridgeYabridgeLink, layout.BundledYabridgeDir);
    }

    private static void Link(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);

        if (new DirectoryInfo(link).LinkTarget == target)
        {
            return;
        }

        File.Delete(link);
        File.CreateSymbolicLink(link, target);
    }
}
