namespace Cabinet.Core;

public sealed record ManagerPlugin(string Name, IReadOnlyList<string> Formats);

public sealed partial class Library
{
    private static readonly byte[] Vst2Entry = "VSTPluginMain"u8.ToArray();

    public IReadOnlyList<ManagerPlugin> PluginsOf(LibraryEntry manager) =>
        [.. Plugins(Where(manager))
            .GroupBy(bundle => Path.GetFileNameWithoutExtension(bundle), StringComparer.OrdinalIgnoreCase)
            .OrderBy(plugin => plugin.Key, StringComparer.OrdinalIgnoreCase)
            .Select(plugin => new ManagerPlugin(
                plugin.Key,
                [.. plugin.Select(Format).Distinct().Order(StringComparer.Ordinal)]))];

    public void RemovePlugin(LibraryEntry manager, string name, Action<string>? onOutput)
    {
        var where = Where(manager);
        var bundles = Plugins(where)
            .Where(bundle => string.Equals(
                Path.GetFileNameWithoutExtension(bundle), name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (bundles.Count == 0)
        {
            throw new InvalidOperationException($"There is no plugin called {name} in {where}");
        }

        var prefixes = new Prefixes(layout, runner);
        using var guard = prefixes.Guard(where, $"remove {name}");

        foreach (var uninstaller in Uninstallers(where).Where(one => !IsWine(one.Name) && Names(one.Name, name)))
        {
            onOutput?.Invoke($"Uninstalling {uninstaller.Name}…");
            prefixes.RunJoined(where, ["cmd", "/c", Quietly(uninstaller.Command)], onOutput);
        }

        foreach (var bundle in bundles)
        {
            foreach (var file in WithItsContent(bundle).Where(Path.Exists))
            {
                if (Directory.Exists(file))
                {
                    Directory.Delete(file, recursive: true);
                }
                else
                {
                    File.Delete(file);
                }

                onOutput?.Invoke($"  removed {Path.GetFileName(file)}");
            }
        }

        Bridge(prefixes, onOutput);
        onOutput?.Invoke($"{name} is gone from your DAW; {manager.Name} can install it again.");
    }

    public static string Format(string bundle) =>
        Path.GetExtension(bundle).ToLowerInvariant() switch
        {
            ".vst3" => "VST3",
            ".clap" => "CLAP",
            _ => "VST2",
        };

    private IEnumerable<string> Plugins(string prefix) =>
        Bundled(prefix).Where(bundle => Format(bundle) != "VST2" || Exports(bundle, Vst2Entry));

    private static bool Exports(string file, byte[] symbol) =>
        File.ReadAllBytes(file).AsSpan().IndexOf(symbol) >= 0;

    private static IEnumerable<string> WithItsContent(string bundle) =>
        Format(bundle) == "VST2"
            ? Directory.EnumerateFiles(Path.GetDirectoryName(bundle)!, Path.GetFileNameWithoutExtension(bundle) + ".*")
            : [bundle];

    private static string Quietly(string command) =>
        command.Contains("unins", StringComparison.OrdinalIgnoreCase)
            ? command + " /VERYSILENT /SUPPRESSMSGBOXES /NORESTART"
            : command.Contains("msiexec", StringComparison.OrdinalIgnoreCase)
                ? command + " /qn"
                : command;
}
