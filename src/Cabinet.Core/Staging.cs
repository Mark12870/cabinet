using System.Security.Cryptography;

namespace Cabinet.Core;

public sealed class Staging : IDisposable
{
    private const string Mark = Layout.StagingMarker;
    private const string Held = Layout.StagingLock;
    private const int Attempts = 3;

    private readonly FileStream held;

    private Staging(string path, FileStream held)
    {
        Path = path;
        this.held = held;
    }

    public string Path { get; }

    public static Staging Create(string parent, string what)
    {
        Directory.CreateDirectory(parent);

        for (var attempt = 1; ; attempt++)
        {
            var path = System.IO.Path.Combine(
                parent, $"{Mark}{what}-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}");

            try
            {
                var held = new FileStream(
                    path + Held, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                Directory.CreateDirectory(path);
                return new Staging(path, held);
            }
            catch (IOException) when (attempt < Attempts)
            {
            }
        }
    }

    public static bool Owns(string name) => name.StartsWith(Mark, StringComparison.Ordinal);

    public void Publish(string target) => Directory.Move(Path, target);

    public static void Sweep(string parent)
    {
        if (!Directory.Exists(parent))
        {
            return;
        }

        foreach (var lockFile in Directory.EnumerateFiles(parent, Mark + "*" + Held).ToList())
        {
            try
            {
                using var abandoned = new FileStream(
                    lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                Discard(lockFile[..^Held.Length]);
                File.Delete(lockFile);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public void Dispose()
    {
        try
        {
            Discard(Path);
            File.Delete(Path + Held);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            held.Dispose();
        }
    }

    private static void Discard(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
