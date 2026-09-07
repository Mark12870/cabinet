using System.Runtime.InteropServices;

namespace Cabinet.Core.Tests;

internal static class TestRoot
{
    private const int LockExclusive = 2;
    private static readonly FileStream TestLock = AcquireLock();

    public static string Create(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "test-data", name);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        return Directory.CreateDirectory(path).FullName;
    }

    private static FileStream AcquireLock()
    {
        var file = new FileStream(
            Path.Combine(Path.GetTempPath(), "cabinet-core-tests.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);

        if (flock(file.SafeFileHandle.DangerousGetHandle().ToInt32(), LockExclusive) != 0)
        {
            throw new IOException("could not lock the Core test suite");
        }

        return file;
    }

    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static extern int flock(int fileDescriptor, int operation);
}
