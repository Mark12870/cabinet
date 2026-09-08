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

        using var file = File.OpenRead(path);
        var offset = Math.Max(0, file.Length - MaximumBytes);
        file.Seek(offset, SeekOrigin.Begin);

        using var reader = new StreamReader(file);
        var text = reader.ReadToEnd();

        if (offset > 0)
        {
            if (text.IndexOf('\n') is var end && end >= 0)
            {
                text = text[(end + 1)..];
            }

            File.WriteAllText(path, text);
        }

        return text.Length > 0 ? text : null;
    }
}
