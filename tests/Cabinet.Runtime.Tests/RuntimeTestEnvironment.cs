using System.Diagnostics;

namespace Cabinet.Runtime.Tests;

internal static class RuntimeTestEnvironment
{
    public static string Root =>
        Environment.GetEnvironmentVariable("CABINET_RUNTIME_ROOT")
        ?? Path.Combine(
            Environment.GetEnvironmentVariable("XDG_CACHE_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"),
            "cabinet-rt");

    public static string Home => Path.Combine(Root, "home");

    public static string RuntimeDirectory =>
        Path.Combine(Root, "runtime");

    public static string SocketDirectory =>
        Path.Combine(
            Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")
            ?? throw new InvalidOperationException("XDG_RUNTIME_DIR is not set"),
            "yabridge",
            "c");

    public static string TemporaryDirectory => Path.Combine(Root, "tmp");

    public static string DataHome => Path.Combine(Home, ".local", "share");

    public static string ConfigHome => Path.Combine(Home, ".config");

    public static string CacheHome => Path.Combine(Home, ".cache");

    public static string FlatpakUserDirectory =>
        Path.Combine(DataHome, "flatpak");

    public static string Backend => Configuration("backend");

    public static string Toolbox => Configuration("toolbox");

    public static void Apply(ProcessStartInfo info)
    {
        info.Environment["HOME"] = Home;
        info.Environment["XDG_RUNTIME_DIR"] = RuntimeDirectory;
        info.Environment["XDG_DATA_HOME"] = DataHome;
        info.Environment["XDG_CONFIG_HOME"] = ConfigHome;
        info.Environment["XDG_CACHE_HOME"] = CacheHome;
        info.Environment["FLATPAK_USER_DIR"] = FlatpakUserDirectory;
        info.Environment["FLATPAK_SYSTEM_DIRS"] = "/var/lib/flatpak";
        info.Environment["CABINET_RUNTIME_SOCKET_DIRECTORY"] = SocketDirectory;
        info.Environment["CABINET_RUNTIME_ROOT"] = Root;
        info.Environment["CABINET_RUNTIME_BACKEND"] = Backend;
        info.Environment["CABINET_RUNTIME_TOOLBOX"] = Toolbox;
    }

    public static void AddEnvironmentArguments(ICollection<string> arguments)
    {
        arguments.Add($"HOME={Home}");
        arguments.Add($"XDG_RUNTIME_DIR={RuntimeDirectory}");
        arguments.Add($"XDG_DATA_HOME={DataHome}");
        arguments.Add($"XDG_CONFIG_HOME={ConfigHome}");
        arguments.Add($"XDG_CACHE_HOME={CacheHome}");
        arguments.Add($"FLATPAK_USER_DIR={FlatpakUserDirectory}");
        arguments.Add("FLATPAK_SYSTEM_DIRS=/var/lib/flatpak");
        arguments.Add($"CABINET_RUNTIME_SOCKET_DIRECTORY={SocketDirectory}");
        arguments.Add($"CABINET_RUNTIME_ROOT={Root}");
        arguments.Add($"CABINET_RUNTIME_BACKEND={Backend}");
        arguments.Add($"CABINET_RUNTIME_TOOLBOX={Toolbox}");
    }

    private static string Configuration(string key)
    {
        var path = Path.Combine(Root, "config");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("run scripts/setup-carla-tests.sh first");
        }

        var prefix = key + "=";
        var value = File.ReadLines(path)
            .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
            .Select(line => line[prefix.Length..])
            .FirstOrDefault();

        return value ?? throw new InvalidOperationException($"runtime configuration has no {key}");
    }
}
