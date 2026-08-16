namespace Cabinet.Core;

/// <summary>
/// What Cabinet needs in place before any command works. Runs itself.
/// </summary>
/// <remarks>
/// This replaces what used to be <c>cabinet setup</c>. There is nothing to export any
/// more — the DAW reads yabridge out of the installed Flatpak directly — so the only
/// thing left is a link inside Cabinet's own data directory, which is cheap enough to
/// make on every invocation rather than asking the user to remember a step.
/// </remarks>
public static class Bootstrap
{
    public static void Ensure(Layout layout)
    {
        Directory.CreateDirectory(layout.PrefixesDir);
        LinkYabridgeForYabridgectl(layout);
    }

    /// <summary>
    /// Points <c>yabridgectl</c> at the Flatpak's own copy of yabridge.
    /// </summary>
    /// <remarks>
    /// It searches its own <c>$XDG_DATA_HOME/yabridge</c> and nowhere useful otherwise,
    /// so without this every <c>sync</c> fails with "could not find
    /// libyabridge-chainloader-vst2.so". The link target is the <em>host</em> path rather
    /// than <c>/app/lib/yabridge</c> so that it resolves identically on both sides of the
    /// sandbox boundary — the chainloader hands those paths back to the shim.
    /// </remarks>
    private static void LinkYabridgeForYabridgectl(Layout layout)
    {
        var link = layout.SandboxYabridgeLink;
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);

        // LinkTarget rather than File.ResolveLinkTarget: it answers null for "absent" and
        // "not a link" instead of throwing on the first run, when neither exists yet.
        if (new DirectoryInfo(link).LinkTarget == layout.HostYabridgeDir)
        {
            return;
        }

        // Unconditional: a no-op when nothing is there, and it clears a dangling link,
        // which the usual Path.Exists guard would report as absent.
        File.Delete(link);
        File.CreateSymbolicLink(link, layout.HostYabridgeDir);
    }
}
