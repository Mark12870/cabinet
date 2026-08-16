namespace Cabinet.Core;

/// <summary>
/// What Cabinet needs in place before any command works, made on every invocation.
/// </summary>
/// <remarks>
/// This is what is left of <c>cabinet setup</c>. Nothing is exported any more, so a
/// remembered setup step would buy nothing.
/// </remarks>
public static class Bootstrap
{
    public static void Ensure(Layout layout)
    {
        Directory.CreateDirectory(layout.PrefixesDir);
        LinkYabridgeForYabridgectl(layout);
    }

    /// <remarks>
    /// The target is the host path rather than <c>/app/lib/yabridge</c> so that it resolves
    /// on both sides of the boundary — the chainloader hands those paths back to the shim.
    /// </remarks>
    private static void LinkYabridgeForYabridgectl(Layout layout)
    {
        var link = layout.SandboxYabridgeLink;
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);

        // LinkTarget, not File.ResolveLinkTarget: it answers null rather than throwing
        // when nothing is there yet.
        if (new DirectoryInfo(link).LinkTarget == layout.HostYabridgeDir)
        {
            return;
        }

        File.Delete(link);
        File.CreateSymbolicLink(link, layout.HostYabridgeDir);
    }
}
