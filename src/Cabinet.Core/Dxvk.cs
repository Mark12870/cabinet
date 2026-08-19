namespace Cabinet.Core;

public sealed class Dxvk(Layout layout, IProcessRunner runner)
{
    public const string Version = "2.7.1";

    public const string Sha256 =
        "d85ce7c79f57ecd765aaa1b9e7007cb875e6fde9f6d331df799bce73d513ce87";

    public static readonly IReadOnlyList<string> Libraries =
        ["d3d8", "d3d9", "d3d10core", "d3d11", "dxgi"];

    public static string Archive => $"dxvk-{Version}.tar.gz";

    public static string Url =>
        $"https://github.com/doitsujin/dxvk/releases/download/v{Version}/{Archive}";

    public string? InstalledIn(string prefix)
    {
        var marker = layout.PrefixDxvkFile(prefix);

        return File.Exists(marker) && File.ReadAllText(marker).Trim() is { Length: > 0 } recorded
            ? recorded
            : null;
    }

    public string Install(string prefix, Action<string>? onOutput = null)
    {
        Initialised(prefix);

        var staging = Path.Combine(Path.GetTempPath(), "cabinet-dxvk");

        try
        {
            Unpack(Download(staging, onOutput), staging, onOutput);
            Copy(staging, "x64", layout.PrefixSystem32(prefix), Backups(prefix, System32), onOutput);
            Copy(staging, "x32", layout.PrefixSysWow64(prefix), Backups(prefix, SysWow64), onOutput);
            Override(prefix, onOutput);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }

        File.WriteAllText(layout.PrefixDxvkFile(prefix), Version + Environment.NewLine);
        onOutput?.Invoke($"{prefix} now renders through DXVK {Version}.");
        return Version;
    }

    public void Remove(string prefix, Action<string>? onOutput = null)
    {
        Initialised(prefix);

        var complete =
            Restore(layout.PrefixSystem32(prefix), Backups(prefix, System32), System32, onOutput)
            & Restore(layout.PrefixSysWow64(prefix), Backups(prefix, SysWow64), SysWow64, onOutput);

        foreach (var library in Libraries)
        {
            onOutput?.Invoke($"{library}: back to Wine's own");
            Reg(prefix, ["delete", OverridesKey, "/v", library, "/f"], library);
        }

        if (Directory.Exists(layout.PrefixDxvkBackupDir(prefix)))
        {
            Directory.Delete(layout.PrefixDxvkBackupDir(prefix), recursive: true);
        }

        File.Delete(layout.PrefixDxvkFile(prefix));

        if (!complete)
        {
            onOutput?.Invoke("Some had no backup — asking Wine to put its own back.");
            var result = new Prefixes(layout, runner).Run(prefix, "wineboot", ["-u"], onOutput);

            if (!result.Ok)
            {
                throw new InvalidOperationException(
                    $"wineboot could not restore Direct3D in '{prefix}'");
            }
        }

        onOutput?.Invoke($"{prefix} renders through Wine's own Direct3D again.");
    }

    private const string System32 = "system32";
    private const string SysWow64 = "syswow64";

    private void Initialised(string prefix)
    {
        if (!Directory.Exists(Path.Combine(layout.PrefixPath(prefix), "dosdevices")))
        {
            throw new DirectoryNotFoundException(
                $"no initialised prefix '{prefix}' — make one with `cabinet new {prefix}`");
        }
    }

    private string Download(string directory, Action<string>? onOutput)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, Archive);

        onOutput?.Invoke($"Downloading {Url}");
        var fetched = runner.Run(
            "curl", ["-fL", "-sS", "--retry", "2", "-o", target, Url], onOutput: onOutput);

        if (!fetched.Ok)
        {
            throw new InvalidOperationException($"could not download {Url}");
        }

        onOutput?.Invoke($"Downloaded {new FileInfo(target).Length / 1024 / 1024} MB");
        onOutput?.Invoke($"Checking sha256 {Sha256[..12]}…");
        Checksum.Expect(target, Sha256);
        return target;
    }

    private void Unpack(string tarball, string directory, Action<string>? onOutput)
    {
        onOutput?.Invoke($"Unpacking {Path.GetFileName(tarball)}");
        var result = runner.Run(
            "tar", ["-xf", tarball, "--strip-components=1", "-C", directory],
            onOutput: onOutput);

        if (!result.Ok)
        {
            throw new InvalidOperationException($"could not unpack {tarball}");
        }
    }

    private static void Copy(
        string staging, string architecture, string destination, string backups,
        Action<string>? onOutput)
    {
        onOutput?.Invoke($"Copying {architecture} into {Path.GetFileName(destination)}");

        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(backups);

        foreach (var library in Libraries)
        {
            var source = Path.Combine(staging, architecture, library + ".dll");

            if (!File.Exists(source))
            {
                throw new FileNotFoundException(
                    $"{Archive} carries no {architecture}/{library}.dll", source);
            }

            var replaced = Path.Combine(destination, library + ".dll");
            var backup = Path.Combine(backups, library + ".dll");

            if (File.Exists(replaced) && !File.Exists(backup))
            {
                File.Move(replaced, backup);
                onOutput?.Invoke($"  {library}.dll  (Wine's kept for putting back)");
            }
            else
            {
                onOutput?.Invoke($"  {library}.dll");
            }

            File.Copy(source, replaced, overwrite: true);
        }
    }

    private static bool Restore(
        string destination, string backups, string windowsDir, Action<string>? onOutput)
    {
        onOutput?.Invoke($"Putting {windowsDir} back");
        var complete = true;

        foreach (var library in Libraries)
        {
            var installed = Path.Combine(destination, library + ".dll");
            var backup = Path.Combine(backups, library + ".dll");

            if (File.Exists(backup))
            {
                File.Move(backup, installed, overwrite: true);
                onOutput?.Invoke($"  {library}.dll  (Wine's own, restored)");
            }
            else if (File.Exists(installed))
            {
                File.Delete(installed);
                complete = false;
                onOutput?.Invoke($"  {library}.dll  (no backup — Wine will put its own back)");
            }
        }

        return complete;
    }

    private string Backups(string prefix, string windowsDir) =>
        layout.PrefixDxvkBackup(prefix, windowsDir);

    private const string OverridesKey = @"HKCU\Software\Wine\DllOverrides";

    private void Override(string prefix, Action<string>? onOutput)
    {
        foreach (var library in Libraries)
        {
            onOutput?.Invoke($"{library}: native");
            Reg(prefix, ["add", OverridesKey, "/v", library, "/d", "native", "/f"], library);
        }
    }

    private void Reg(string prefix, IReadOnlyList<string> arguments, string library)
    {
        var result = new Prefixes(layout, runner).Run(prefix, "wine", ["reg", .. arguments]);

        if (!result.Ok)
        {
            throw new InvalidOperationException(
                $"could not point {library} at its DLL in '{prefix}'");
        }
    }
}
