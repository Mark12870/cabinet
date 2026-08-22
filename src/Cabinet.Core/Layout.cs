namespace Cabinet.Core;

public sealed class Layout
{
    public const string AppId = "io.github.mark12870.cabinet";

    public const string BundledYabridgeDir = "/app/lib/yabridge";

    public const string Wine = "/app/bin/wine";

    public const string Gui = "/app/bin/cabinet-gui";

    public const string BundledRunner = "bundled";

    public const string BundledWineShare = "/app/share/wine";

    public const string MetainfoPath = "/app/share/metainfo/" + AppId + ".metainfo.xml";

    public const string RunnerMarker = ".cabinet-runner";

    public const string DxvkMarker = ".cabinet-dxvk";

    public const string DxvkBackupDir = ".cabinet-dxvk-backup";

    public const string SyncMarker = ".cabinet-sync";

    public const string EnvMarker = ".cabinet-env";

    public const string PluginsMarker = ".cabinet-plugins";

    public const string BundledLibraryDir = "/app/share/cabinet/library";

    public static readonly IReadOnlyList<string> PluginExtensions =
        [".vst3", ".clap", ".lv2", ".so"];

    public static readonly IReadOnlyList<string> ScanDirectories =
        [".vst3", ".clap", ".lv2", ".vst"];

    public Layout(
        string home,
        string runtimeDir,
        string? sandboxDataHome = null,
        string? hostAppFiles = null,
        string? libraryDir = null)
    {
        Home = home;
        RuntimeDir = runtimeDir;
        SandboxDataHome = sandboxDataHome ?? Path.Combine(home, ".var", "app", AppId, "data");
        HostAppFiles = hostAppFiles ?? DefaultHostAppFiles(home);
        LibraryDir = libraryDir ?? BundledLibraryDir;
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

    public static IniFile FlatpakInfo => Info.Value;

    public string Home { get; }
    public string RuntimeDir { get; }
    public string SandboxDataHome { get; }

    public string HostAppFiles { get; }

    public string LibraryDir { get; }

    public string HostYabridgeDir => Path.Combine(HostAppFiles, "lib", "yabridge");

    public string ShimPath => Path.Combine(HostYabridgeDir, "cabinet-wine");

    public string DeployFile =>
        Path.Combine(Path.GetDirectoryName(HostAppFiles) ?? HostAppFiles, "deploy");

    public string? RepoConfig =>
        InstallRoot is { } root ? Path.Combine(root, "repo", "config") : null;

    public string SandboxYabridgeLink => Path.Combine(SandboxDataHome, "yabridge");

    public string PrefixesDir => Path.Combine(SandboxDataHome, "prefixes");

    public string SocketDir => Path.Combine(RuntimeDir, "yabridge");

    public string RunnersDir => Path.Combine(SandboxDataHome, "runners");

    public string NativeDir => Path.Combine(SandboxDataHome, "native");

    public string NativePath(string name) => Path.Combine(NativeDir, name);

    public string LibraryScript(string vendor, string name) =>
        Path.Combine(LibraryDir, vendor, name);

    public string? LibraryIcon(string vendor, string id) => Media(vendor, id + ".png");

    public string? LibraryLogo(string vendor) => Media(vendor, "logo.png");

    public string? LibraryScreenshot(string vendor, string id) => Media(vendor, id + ".jpg");

    private string? Media(string vendor, string name)
    {
        var path = Path.Combine(LibraryDir, vendor, name);

        return File.Exists(path) ? path : null;
    }

    public string DataPath(string relative) => Path.Combine(Home, relative);

    public string ScanDir(string extension) => Path.Combine(Home, extension switch
    {
        ".vst3" => ".vst3",
        ".clap" => ".clap",
        ".lv2" => ".lv2",
        ".so" => ".vst",
        _ => throw new ArgumentException($"not a plugin extension: '{extension}'", nameof(extension)),
    });

    public string RunnerPath(string name) => Path.Combine(RunnersDir, name);

    public string RunnerWine(string name) => Path.Combine(RunnerPath(name), "bin", "wine");

    public string PrefixPath(string name) => Path.Combine(PrefixesDir, name);

    public string PrefixRunnerFile(string name) =>
        Path.Combine(PrefixPath(name), RunnerMarker);

    public string PrefixDxvkFile(string name) =>
        Path.Combine(PrefixPath(name), DxvkMarker);

    public string PrefixDxvkBackupDir(string name) =>
        Path.Combine(PrefixPath(name), DxvkBackupDir);

    public string PrefixDxvkBackup(string name, string windowsDir) =>
        Path.Combine(PrefixDxvkBackupDir(name), windowsDir);

    public string PrefixSyncFile(string name) =>
        Path.Combine(PrefixPath(name), SyncMarker);

    public string PrefixEnvFile(string name) =>
        Path.Combine(PrefixPath(name), EnvMarker);

    public string PrefixPluginsFile(string name) =>
        Path.Combine(PrefixPath(name), PluginsMarker);

    public string PrefixSystemReg(string name) =>
        Path.Combine(PrefixPath(name), "system.reg");

    public string PrefixUserReg(string name) =>
        Path.Combine(PrefixPath(name), "user.reg");

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
            yield return Path.Combine(driveC, programFiles, "Steinberg", "VstPlugins");
        }
    }

    private const string ProgramFiles64 = "Program Files";
    private const string ProgramFiles32 = "Program Files (x86)";

    public string DawDataHome(string flatpakId) =>
        Path.Combine(Home, ".var", "app", flatpakId, "data");

    public string DawYabridgeLink(string flatpakId) =>
        Path.Combine(DawDataHome(flatpakId), "yabridge");

    private string? InstallRoot
    {
        get
        {
            for (var dir = new DirectoryInfo(HostAppFiles); dir is not null; dir = dir.Parent)
            {
                if (dir.Name == "app" && dir.Parent is not null)
                {
                    return dir.Parent.FullName;
                }
            }

            return null;
        }
    }

    private static string DefaultHostAppFiles(string home) => Path.Combine(
        home, ".local", "share", "flatpak", "app", AppId, "current", "active", "files");

    private static readonly Lazy<IniFile> Info = new(() =>
        File.Exists("/.flatpak-info")
            ? IniFile.Parse(File.ReadAllLines("/.flatpak-info"))
            : IniFile.Empty);

    private static string? HostAppFilesFromFlatpakInfo()
    {
        var appPath = FlatpakInfo.Get("Instance", "app-path");

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
