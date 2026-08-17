namespace Cabinet.Core;

public static class Bootstrap
{
    public static void Ensure(Layout layout)
    {
        Directory.CreateDirectory(layout.PrefixesDir);
        LinkYabridgeForYabridgectl(layout);
    }

    private static void LinkYabridgeForYabridgectl(Layout layout)
    {
        var link = layout.SandboxYabridgeLink;
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);

        if (new DirectoryInfo(link).LinkTarget == layout.HostYabridgeDir)
        {
            return;
        }

        File.Delete(link);
        File.CreateSymbolicLink(link, layout.HostYabridgeDir);
    }
}
