namespace Cabinet.Core;

public sealed class Underway : IDisposable
{
    private const int Attempts = 10;

    private static readonly TimeSpan Beat = TimeSpan.FromMilliseconds(50);

    private readonly FileStream file;

    private Underway(FileStream file, string? left)
    {
        this.file = file;
        Left = left;
    }

    public string? Left { get; }

    public static Underway? Begin(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                var file = new FileStream(
                    path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

                return new Underway(file, Text(file));
            }
            catch (IOException)
            {
                Thread.Sleep(Beat);
            }
        }

        return null;
    }

    public static string? Abandoned(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var file = new FileStream(
                path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            return Text(file);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static bool Marked(string path) => File.Exists(path) && new FileInfo(path).Length > 0;

    public void Note(string text)
    {
        file.SetLength(0);
        using var writer = new StreamWriter(file, leaveOpen: true);
        writer.Write(text);
        writer.Flush();
        file.Flush(flushToDisk: true);
    }

    public void Finish()
    {
        file.SetLength(0);
        file.Flush(flushToDisk: true);
        file.Dispose();
    }

    public void Dispose() => file.Dispose();

    private static string? Text(FileStream file)
    {
        file.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(file, leaveOpen: true);
        return reader.ReadToEnd() is { Length: > 0 } text ? text : null;
    }
}
