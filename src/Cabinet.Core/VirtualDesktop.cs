namespace Cabinet.Core;

public sealed class VirtualDesktop(Layout layout, IProcessRunner runner)
{
    private const string Size = "1920x1080";
    private const string ExplorerKey = @"HKCU\Software\Wine\Explorer";
    private const string DesktopsKey = @"HKCU\Software\Wine\Explorer\Desktops";
    private const string ExplorerPath = @"Software\Wine\Explorer";
    private const string DesktopsPath = @"Software\Wine\Explorer\Desktops";
    private const string DesktopName = "Default";

    public bool EnabledIn(string prefix)
    {
        var registry = new PrefixRegistry(layout);

        return registry.Lookup(prefix, ExplorerPath, "Desktop") is { Length: > 0 } named
            && registry.Lookup(prefix, DesktopsPath, named) is { Length: > 0 };
    }

    public void Set(string prefix, Action<string>? onOutput)
    {
        Ensure(Reg(prefix, ["add", DesktopsKey, "/v", DesktopName, "/d", Size, "/f"]), prefix);
        Ensure(
            Reg(prefix, ["add", ExplorerKey, "/v", "Desktop", "/d", DesktopName, "/f"]), prefix);

        onOutput?.Invoke($"{prefix} draws its windows on a desktop of its own.");
    }

    public void Unset(string prefix, Action<string>? onOutput)
    {
        Reg(prefix, ["delete", ExplorerKey, "/v", "Desktop", "/f"]);
        Reg(prefix, ["delete", DesktopsKey, "/v", DesktopName, "/f"]);

        onOutput?.Invoke($"{prefix} puts its windows straight on your desktop again.");
    }

    private static void Ensure(ProcessResult result, string prefix)
    {
        if (!result.Ok)
        {
            throw new InvalidOperationException(
                $"could not set the virtual desktop in '{prefix}'");
        }
    }

    private ProcessResult Reg(string prefix, IReadOnlyList<string> arguments) =>
        new Prefixes(layout, runner).RunJoined(prefix, ["reg", .. arguments]);
}
