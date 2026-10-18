using System.Text;

namespace Cabinet.Core;

public sealed class WindowsInstaller(Layout layout, IProcessRunner runner)
{
    private const string SourceDir = "SourceDir";
    private const string Staged = ".cabinet-new";

    public ProcessResult Install(
        string prefix, string package, string commandLine, Action<string>? onOutput = null,
        string? claimedBy = null)
    {
        if (!Directory.Exists(layout.PrefixPath(prefix)))
        {
            throw new KeyNotFoundException($"no such prefix '{prefix}'");
        }

        var prefixes = new Prefixes(layout, runner);
        using var claim = claimedBy == layout.PrefixPath(prefix)
            ? null
            : prefixes.Guard(prefix, $"install {Path.GetFileName(package)} into {prefix}");

        var given = InstallerPackage.CommandLine(commandLine);
        var plan = InstallerPackage.Read(package).Plan(
            given,
            given.GetValueOrDefault(SourceDir) is { Length: > 0 } source
                ? source
                : WindowsPath(Path.GetDirectoryName(Path.GetFullPath(package))!));

        foreach (var file in plan.Files)
        {
            Place(layout.PrefixWindowsPath(prefix, file.Source), layout.PrefixWindowsPath(prefix, file.Target));
        }

        onOutput?.Invoke($"Placed {plan.Files.Count} files from {Path.GetFileName(package)} in {prefix}");

        var registry = layout.PrefixPackageRegistry(prefix);
        Directory.CreateDirectory(Path.GetDirectoryName(registry)!);
        File.WriteAllText(registry, Registry(plan.Values), Encoding.Unicode);

        return prefixes.Run(prefix, "wine", ["regedit", "/S", Layout.PackageRegistry], onOutput);
    }

    private static void Place(string source, string target)
    {
        var staged = target + Staged;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, staged, overwrite: true);
        File.Move(staged, target, overwrite: true);
    }

    private static string WindowsPath(string unixPath) => "Z:" + unixPath.Replace('/', '\\');

    private static string Registry(IEnumerable<PackagedValue> values)
    {
        var text = new StringBuilder("Windows Registry Editor Version 5.00\r\n");

        foreach (var key in values.GroupBy(value => value.Key))
        {
            text.Append($"\r\n[{key.Key}]\r\n");

            foreach (var value in key.Where(value => value.Value is not null || value.Number is not null))
            {
                var name = value.Name is { Length: > 0 } named ? $"\"{Escaped(named)}\"" : "@";
                text.Append(value.Number is { } number
                    ? $"{name}=dword:{number:x8}\r\n"
                    : $"{name}=\"{Escaped(value.Value!)}\"\r\n");
            }
        }

        return text.ToString();
    }

    private static string Escaped(string text) =>
        text.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
