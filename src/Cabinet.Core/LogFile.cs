using System.Text;

namespace Cabinet.Core;

internal static class LogFile
{
    private const long MaximumBytes = 4 * 1024 * 1024;

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
}
