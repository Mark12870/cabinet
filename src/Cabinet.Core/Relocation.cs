using System.Runtime.InteropServices;

namespace Cabinet.Core;

public static class Relocation
{
    private const int CrossDevice = 18;

    public static void Move(string source, string destination)
    {
        if (Present(destination))
        {
            throw new IOException($"{destination} is already there");
        }

        if (Rename(source, destination) == 0)
        {
            return;
        }

        var error = Marshal.GetLastPInvokeError();
        if (error != CrossDevice)
        {
            throw Failed(source, destination, error);
        }

        using (var moving = Staging.Create(Path.GetDirectoryName(destination)!, "moving"))
        {
            var copy = Path.Combine(moving.Path, Path.GetFileName(destination));
            Copy(source, copy);
            Place(copy, destination);
        }

        using var moved = Staging.Create(Path.GetDirectoryName(source)!, "moved");
        Place(source, Path.Combine(moved.Path, Path.GetFileName(source)));
    }

    public static void Delete(string path)
    {
        if (IsDirectory(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else if (Present(path))
        {
            File.Delete(path);
        }
    }

    public static bool Present(string path) => Path.Exists(path) || new FileInfo(path).LinkTarget is not null;

    private static bool IsDirectory(string path) =>
        Directory.Exists(path) && new DirectoryInfo(path).LinkTarget is null;

    private static void Place(string source, string destination)
    {
        if (Rename(source, destination) != 0)
        {
            throw Failed(source, destination, Marshal.GetLastPInvokeError());
        }
    }

    private static IOException Failed(string source, string destination, int error) =>
        new($"cannot move {source} to {destination}: {Marshal.GetPInvokeErrorMessage(error)}");

    private static void Copy(string source, string destination)
    {
        if (new FileInfo(source).LinkTarget is { } target)
        {
            File.CreateSymbolicLink(destination, target);
        }
        else if (IsDirectory(source))
        {
            Directory.CreateDirectory(destination);

            foreach (var entry in Directory.EnumerateFileSystemEntries(source))
            {
                Copy(entry, Path.Combine(destination, Path.GetFileName(entry)));
            }
        }
        else
        {
            File.Copy(source, destination);
        }
    }

    [DllImport("libc", EntryPoint = "rename", SetLastError = true)]
    private static extern int Rename(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string source,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string destination);
}
