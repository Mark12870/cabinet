using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Cabinet.Core;

public sealed partial class Ilok(Layout layout, IProcessRunner runner)
{
    private readonly Http http = new(runner);

    public const string Url =
        "https://installers.ilok.com/iloklicensemanager/LicenseSupportInstallerWin64.zip";

    public const string Setup = "License Support Win64.exe";

    public const string Package = "PACE License Support Win64.msi";

    public const string ProductCode = "{218BEA78-4687-418e-A876-96B307B3133D}";

    public const string Service = "PaceLicenseDServices";

    public const string Manager =
        @"C:\Program Files (x86)\iLok License Manager\iLok License Manager.exe";

    public string? InstalledIn(string prefix)
    {
        var marker = layout.PrefixIlokFile(prefix);

        return File.Exists(marker) && File.ReadAllText(marker).Trim() is { Length: > 0 } recorded
            ? recorded
            : null;
    }

    public string Install(
        string prefix, Action<string>? onOutput = null, Action<double>? onProgress = null)
    {
        Initialised(prefix);

        var prefixes = new Prefixes(layout, runner);
        using var claim = prefixes.Claim(prefix, $"put iLok into {prefix}");
        string version;

        using (var staging = Staging.Create(layout.TempDir, "ilok"))
        {
            var setup = Unpack(Download(staging.Path, onOutput, onProgress), staging.Path, onOutput);
            version = VersionOf(setup);

            var package = Path.Combine(staging.Path, Package);
            onOutput?.Invoke($"Taking {Package} out of {Setup}");
            InstallShieldSetup.Extract(setup, Package, package);

            onOutput?.Invoke($"Installing PACE License Support {version}");
            var installed = prefixes.Run(prefix, "msiexec", ["/i", WindowsPath(package), "/qn", "NODRIVER=1"], onOutput);
            var service = installed.Ok ? prefixes.Run(prefix, "sc", ["query", Service]) : installed;
            prefixes.Run(prefix, "wineserver", ["-k"]);

            if (!installed.Ok)
            {
                throw new InvalidOperationException(
                    $"Windows Installer could not install PACE License Support {version} into "
                    + $"'{prefix}' — msiexec exited with {installed.ExitCode}");
            }

            File.WriteAllText(layout.PrefixIlokFile(prefix), version + Environment.NewLine);

            if (!service.Stdout.Contains("RUNNING", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"PACE License Support {version} is in '{prefix}', but its licence service "
                    + $"{Service} is not running");
            }
        }

        onOutput?.Invoke($"{prefix} now licenses through iLok {version}.");
        return version;
    }

    public void Remove(string prefix, Action<string>? onOutput = null)
    {
        Initialised(prefix);

        if (InstalledIn(prefix) is null)
        {
            throw new InvalidOperationException(
                $"'{prefix}' does not license through iLok — Cabinet installed nothing to take out");
        }

        var prefixes = new Prefixes(layout, runner);
        using var claim = prefixes.Claim(prefix, $"take iLok out of {prefix}");

        var removed = prefixes.Run(prefix, "msiexec", ["/x", ProductCode, "/qn"], onOutput);
        prefixes.Run(prefix, "wineserver", ["-k"]);

        if (!removed.Ok)
        {
            throw new InvalidOperationException(
                $"Windows Installer could not take PACE License Support out of '{prefix}' — "
                + $"msiexec exited with {removed.ExitCode}");
        }

        File.Delete(layout.PrefixIlokFile(prefix));
        onOutput?.Invoke($"{prefix} no longer licenses through iLok.");
    }

    public ProcessResult Open(string prefix, Action<string>? onOutput = null)
    {
        Initialised(prefix);

        if (InstalledIn(prefix) is null)
        {
            throw new InvalidOperationException(
                $"'{prefix}' does not license through iLok — turn iLok on first");
        }

        return new Prefixes(layout, runner).Start(
            prefix, layout.PrefixWindowsPath(prefix, Manager), onOutput);
    }

    private void Initialised(string prefix)
    {
        if (!Directory.Exists(Path.Combine(layout.PrefixPath(prefix), "dosdevices")))
        {
            throw new DirectoryNotFoundException(
                $"no initialised prefix '{prefix}' — create it first");
        }
    }

    private string Download(
        string directory, Action<string>? onOutput, Action<double>? onProgress)
    {
        var target = Path.Combine(directory, Path.GetFileName(new Uri(Url).AbsolutePath));
        onOutput?.Invoke(Checksum.Unverifiable(Url));
        http.ToFile(Url, target, onOutput, onProgress);
        return target;
    }

    private static string Unpack(string archive, string directory, Action<string>? onOutput)
    {
        onOutput?.Invoke($"Unpacking {Path.GetFileName(archive)}");

        var unpacked = Path.Combine(directory, "unpacked");
        ZipFile.ExtractToDirectory(archive, unpacked);

        return Directory.EnumerateFiles(unpacked, Setup, SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new FileNotFoundException(
                $"{Path.GetFileName(archive)} carries no {Setup}", Setup);
    }

    private static string VersionOf(string setup)
    {
        var folder = Path.GetFileName(Path.GetDirectoryName(setup)!);

        return FolderVersion().Match(folder) is { Success: true } found
            ? found.Groups["version"].Value
            : throw new InvalidDataException(
                $"{Setup} sits in '{folder}', which names no version");
    }

    private static string WindowsPath(string unixPath) => "Z:" + unixPath.Replace('/', '\\');

    [GeneratedRegex(@"_v(?<version>\d+(?:\.\d+)+)(?:_|$)")]
    private static partial Regex FolderVersion();
}
