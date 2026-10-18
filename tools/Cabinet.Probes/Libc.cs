using System.Runtime.InteropServices;

namespace Cabinet.Probes;

internal static partial class Libc
{
    private const int ReadWrite = 0x2;
    private const int Create = 0x40;
    private const int SetLock = 6;
    private const short ReadLock = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct Flock
    {
        public short Type;
        public short Whence;
        public long Start;
        public long Length;
        public int Pid;
    }

    public static void Export(string name, string value)
    {
        if (setenv(name, value, 1) != 0)
        {
            throw new InvalidOperationException($"setenv {name} failed");
        }
    }

    public static string? Take(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        unsetenv(name);
        Environment.SetEnvironmentVariable(name, null);
        return value;
    }

    public static void HoldReadLock(string path)
    {
        var descriptor = open(path, ReadWrite | Create, 0x180);

        if (descriptor < 0)
        {
            throw new IOException($"cannot open {path}: errno {Marshal.GetLastPInvokeError()}");
        }

        var request = new Flock { Type = ReadLock };

        if (fcntl(descriptor, SetLock, ref request) != 0)
        {
            throw new IOException($"cannot lock {path}: errno {Marshal.GetLastPInvokeError()}");
        }
    }

    public static void Leave(int status)
    {
        Console.Out.Flush();
        Console.Error.Flush();
        _exit(status);
    }

    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int setenv(string name, string value, int overwrite);

    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int unsetenv(string name);

    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int open(string path, int flags, int mode);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int fcntl(int descriptor, int command, ref Flock request);

    [LibraryImport("libc")]
    private static partial void _exit(int status);
}
