using System.Diagnostics;
using System.Text;

namespace Cabinet.Core.Tests;

internal static class SessionFiles
{
    public static IDisposable HeldByAPlugin(string busy)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(busy)!);

        var holding = Process.Start(new ProcessStartInfo("python3")
        {
            ArgumentList = { "-c", Holder, busy },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
        })!;

        Assert.Equal("held", holding.StandardOutput.ReadLine());

        return new Plugin(holding);
    }

    private const string Holder =
        "import fcntl, os, struct, sys\n"
        + "fd = os.open(sys.argv[1], os.O_RDWR | os.O_CREAT, 0o600)\n"
        + "fcntl.fcntl(fd, fcntl.F_SETLK, struct.pack('hhqql', fcntl.F_RDLCK, 0, 0, 0, 0))\n"
        + "print('held', flush=True)\n"
        + "sys.stdin.readline()\n";

    private sealed class Plugin(Process holding) : IDisposable
    {
        public void Dispose()
        {
            holding.Kill(entireProcessTree: true);
            holding.WaitForExit();
            holding.Dispose();
        }
    }

    public static string Key(string seed)
    {
        var hash = 0xcbf2_9ce4_8422_2325;

        foreach (var byteValue in Encoding.UTF8.GetBytes(seed))
        {
            hash ^= byteValue;
            hash *= 0x0000_0100_0000_01b3;
        }

        return $"cabinet-{hash:x16}";
    }

    public static string Printed(IReadOnlyDictionary<string, string> environment)
    {
        var directory = Value(environment, "YABRIDGE_TEMP_DIR") ?? Path.GetTempPath();
        var key = Key(Value(environment, "WINEPREFIX") ?? "");

        return string.Join(
            '\n',
            Named("socket", ".sock"),
            Named("lock", ".lock"),
            Named("busy", ".busy"),
            Named("apps", ".apps"),
            Named("change", ".change"),
            Named("record", ".session"),
            Named("log", ".log"));

        string Named(string label, string extension) =>
            $"{label} {Path.Combine(directory, key + extension)}";
    }

    public static SessionPaths Of(Layout layout, string prefix) =>
        SessionPaths.Parse(Printed(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["YABRIDGE_TEMP_DIR"] = layout.SocketDir,
            ["WINEPREFIX"] = layout.PrefixPath(prefix),
        }));

    private static string? Value(IReadOnlyDictionary<string, string> environment, string key) =>
        environment.TryGetValue(key, out var value) && value.Length > 0 ? value : null;
}
