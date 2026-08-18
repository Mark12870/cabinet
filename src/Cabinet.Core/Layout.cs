namespace Cabinet.Core;

public sealed class Layout
{
    public const string AppId = "io.github.mark12870.cabinet";

    public const string BundledYabridgeDir = "/app/lib/yabridge";

    public const string Wine = "/app/bin/wine";

    public const string BundledRunner = "bundled";

    public const string BundledWineShare = "/app/share/wine";

    public const string RunnerMarker = ".cabinet-runner";

    public const string DxvkMarker = ".cabinet-dxvk";

    public Layout(
        string home,
        string runtimeDir,
        string? sandboxDataHome = null,
        string? hostAppFiles = null)
    {
        Home = home;
        RuntimeDir = runtimeDir;
        SandboxDataHome = sandboxDataHome ?? Path.Combine(home, ".var", "app", AppId, "data");
        HostAppFiles = hostAppFiles ?? DefaultHostAppFiles(home);
    }

    public static Layout FromEnvironment()
    {
        var home = Environment.GetEnvironmentVariable("HOME")
                   ?? throw new InvalidOperationException("HOME is not set");

        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")
                         ?? throw new InvalidOperationException("XDG_RUNTIME_DIR is not set");

        return new Layout(
            home,
            runtimeDir,
            Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
            HostAppFilesFromFlatpakInfo());
    }

    public string Home { get; }
    public string RuntimeDir { get; }
    public string SandboxDataHome { get; }

    public string HostAppFiles { get; }

    public string HostYabridgeDir => Path.Combine(HostAppFiles, "lib", "yabridge");

    public string ShimPath => Path.Combine(HostYabridgeDir, "cabinet-wine");

    public string SandboxYabridgeLink => Path.Combine(SandboxDataHome, "yabridge");

    public string PrefixesDir => Path.Combine(SandboxDataHome, "prefixes");

    public string SocketDir => Path.Combine(RuntimeDir, "yabridge");

    public string RunnersDir => Path.Combine(SandboxDataHome, "runners");

    public string RunnerPath(string name) => Path.Combine(RunnersDir, name);

    public string RunnerWine(string name) => Path.Combine(RunnerPath(name), "bin", "wine");

    public string PrefixPath(string name) => Path.Combine(PrefixesDir, name);

    public string PrefixRunnerFile(string name) =>
        Path.Combine(PrefixPath(name), RunnerMarker);

    public string PrefixDxvkFile(string name) =>
        Path.Combine(PrefixPath(name), DxvkMarker);

    public string PrefixSystem32(string name) =>
        Path.Combine(PrefixPath(name), "drive_c", "windows", "system32");

    public string PrefixSysWow64(string name) =>
        Path.Combine(PrefixPath(name), "drive_c", "windows", "syswow64");

    public string PrefixVst3Dir(string name) =>
        Path.Combine(PrefixPath(name), "drive_c", ProgramFiles64, "Common Files", "VST3");

    public IEnumerable<string> PrefixPluginDirs(string name)
    {
        var driveC = Path.Combine(PrefixPath(name), "drive_c");

        foreach (var programFiles in new[] { ProgramFiles64, ProgramFiles32 })
        {
            yield return Path.Combine(driveC, programFiles, "Common Files", "VST2");
            yield return Path.Combine(driveC, programFiles, "Common Files", "VST3");
            yield return Path.Combine(driveC, programFiles, "Common Files", "CLAP");
            yield return Path.Combine(driveC, programFiles, "VstPlugins");
        }
    }

    private const string ProgramFiles64 = "Program Files";
    private const string ProgramFiles32 = "Program Files (x86)";

    public string DawDataHome(string flatpakId) =>
        Path.Combine(Home, ".var", "app", flatpakId, "data");

    public string DawYabridgeLink(string flatpakId) =>
        Path.Combine(DawDataHome(flatpakId), "yabridge");

    private static string DefaultHostAppFiles(string home) => Path.Combine(
        home, ".local", "share", "flatpak", "app", AppId, "current", "active", "files");

    private static string? HostAppFilesFromFlatpakInfo()
    {
        if (!File.Exists("/.flatpak-info"))
        {
            return null;
        }

        var appPath = IniFile.Parse(File.ReadAllLines("/.flatpak-info"))
            .Get("Instance", "app-path");

        return appPath is null ? null : StableAlias(appPath) ?? appPath;
    }

    private static string? StableAlias(string appPath)
    {
        for (var dir = new DirectoryInfo(appPath); dir is not null; dir = dir.Parent)
        {
            if (dir.Name == AppId)
            {
                return Path.Combine(dir.FullName, "current", "active", "files");
            }
        }

        return null;
    }
}
