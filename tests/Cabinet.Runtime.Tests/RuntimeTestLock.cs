using System.Runtime.InteropServices;

namespace Cabinet.Runtime.Tests;

internal sealed class RuntimeTestLock : IDisposable
{
    private const int LockExclusive = 2;
    private const int LockUnlock = 8;
    private readonly FileStream file;

    private RuntimeTestLock()
    {
        Directory.CreateDirectory(RuntimeTestEnvironment.SocketDirectory);
        file = new(
            Path.Combine(RuntimeTestEnvironment.SocketDirectory, "runtime-tests.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);

        if (flock(file.SafeFileHandle.DangerousGetHandle().ToInt32(), LockExclusive) != 0)
        {
            throw new IOException("could not lock the runtime test suite");
        }
    }

    public static RuntimeTestLock Acquire() => new();

    public void Dispose()
    {
        flock(file.SafeFileHandle.DangerousGetHandle().ToInt32(), LockUnlock);
        file.Dispose();
    }

    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static extern int flock(int fileDescriptor, int operation);
}
