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

    public string Install(string prefix)
    {
        if (!Directory.Exists(Path.Combine(layout.PrefixPath(prefix), "dosdevices")))
        {
            throw new DirectoryNotFoundException(
                $"no initialised prefix '{prefix}' — make one with `cabinet new {prefix}`");
        }

        var staging = Path.Combine(Path.GetTempPath(), "cabinet-dxvk");

        try
        {
            Unpack(Download(staging), staging);
            Copy(staging, "x64", layout.PrefixSystem32(prefix));
            Copy(staging, "x32", layout.PrefixSysWow64(prefix));
            Override(prefix);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }

        File.WriteAllText(layout.PrefixDxvkFile(prefix), Version + Environment.NewLine);
        return Version;
    }

    private string Download(string directory)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, Archive);

        var fetched = runner.Run("curl", ["-fL", "--retry", "2", "-o", target, Url]);
        if (!fetched.Ok)
        {
            throw new InvalidOperationException($"could not download {Url}");
        }

        Checksum.Expect(target, Sha256);
        return target;
    }

    private void Unpack(string tarball, string directory)
    {
        var result = runner.Run(
            "tar", ["-xf", tarball, "--strip-components=1", "-C", directory]);

        if (!result.Ok)
        {
            throw new InvalidOperationException($"could not unpack {tarball}");
        }
    }

    private static void Copy(string staging, string architecture, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var library in Libraries)
        {
            var source = Path.Combine(staging, architecture, library + ".dll");

            if (!File.Exists(source))
            {
                throw new FileNotFoundException(
                    $"{Archive} carries no {architecture}/{library}.dll", source);
            }

            File.Copy(source, Path.Combine(destination, library + ".dll"), overwrite: true);
        }
    }

    private void Override(string prefix)
    {
        var prefixes = new Prefixes(layout, runner);

        foreach (var library in Libraries)
        {
            var result = prefixes.Run(
                prefix,
                "wine",
                ["reg", "add", @"HKCU\Software\Wine\DllOverrides", "/v", library, "/d", "native",
                    "/f"]);

            if (!result.Ok)
            {
                throw new InvalidOperationException(
                    $"could not point {library} at DXVK in '{prefix}'");
            }
        }
    }
}
