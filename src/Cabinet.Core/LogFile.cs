using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Cabinet.Core;

internal static class LogFile
{
    private const long MaximumBytes = 4 * 1024 * 1024;
    private const int WriteOnlyCreateAppendCloseOnExec = 0x1 | 0x40 | 0x400 | 0x80000;
    private const int ReadWriteByOwnerReadByOthers = 0x1A4;

    private static readonly Lock Appending = new();

    public static void Append(string path, string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");

        lock (Appending)
        {
            var descriptor = Open(path, WriteOnlyCreateAppendCloseOnExec, ReadWriteByOwnerReadByOthers);

            if (descriptor < 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), $"cannot open {path}");
            }

            try
            {
                for (var written = 0; written < bytes.Length;)
                {
                    var count = Write(descriptor, bytes[written..], bytes.Length - written);

                    if (count < 0)
                    {
                        throw new Win32Exception(Marshal.GetLastPInvokeError(), $"cannot write {path}");
                    }

                    written += (int)count;
                }
            }
            finally
            {
                Close(descriptor);
            }
        }
    }

    public static string? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var file = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var offset = Math.Max(0, file.Length - MaximumBytes);
        file.Seek(offset, SeekOrigin.Begin);

        var bytes = new byte[file.Length - offset];
        var read = file.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        var text = Encoding.UTF8.GetString(bytes, 0, read);

        if (offset > 0)
        {
            var end = text.IndexOf('\n');
            text = end >= 0 ? text[(end + 1)..] : "";
        }

        return text.Length > 0 ? text : null;
    }

    public static void Rotate(string path)
    {
        if (new FileInfo(path) is { Exists: true, Length: > MaximumBytes })
        {
            File.Move(path, path + ".1", overwrite: true);
        }
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, int mode);

    [DllImport("libc", EntryPoint = "write", SetLastError = true)]
    private static extern nint Write(int descriptor, byte[] bytes, nint count);

    [DllImport("libc", EntryPoint = "close")]
    private static extern int Close(int descriptor);
}
