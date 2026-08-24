namespace Cabinet.Core;

public sealed class VirtualDesktop(Layout layout, IProcessRunner runner)
{
    private const string ExplorerKey = @"HKCU\Software\Wine\Explorer";
    private const string DesktopsKey = @"HKCU\Software\Wine\Explorer\Desktops";
    private const string ExplorerPath = @"Software\Wine\Explorer";
    private const string DesktopsPath = @"Software\Wine\Explorer\Desktops";
    private const string DesktopName = "Default";

    public static string ParseSize(string text)
    {
        var parts = text.Trim().ToLowerInvariant().Split('x');

        if (parts.Length == 2
            && int.TryParse(parts[0], out var width) && width > 0
            && int.TryParse(parts[1], out var height) && height > 0)
        {
            return $"{width}x{height}";
        }

        throw new ArgumentException(
            $"not a desktop size: '{text}' — expected <width>x<height>, or off");
    }

    public string? SizeIn(string prefix)
    {
        var registry = new PrefixRegistry(layout);

        return registry.Lookup(prefix, ExplorerPath, "Desktop") is { Length: > 0 } named
            ? registry.Lookup(prefix, DesktopsPath, named)
            : null;
    }

    public void Set(string prefix, string size, Action<string>? onOutput)
    {
        var wanted = ParseSize(size);

        Ensure(Reg(prefix, ["add", DesktopsKey, "/v", DesktopName, "/d", wanted, "/f"]), prefix);
        Ensure(
            Reg(prefix, ["add", ExplorerKey, "/v", "Desktop", "/d", DesktopName, "/f"]), prefix);

        onOutput?.Invoke($"{prefix} draws its windows on a {wanted} desktop of its own.");
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
        new Prefixes(layout, runner).Run(prefix, "wine", ["reg", .. arguments]);
}
