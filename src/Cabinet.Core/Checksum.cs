using System.Security.Cryptography;

namespace Cabinet.Core;

public static class Checksum
{
    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static void Expect(string path, string expected)
    {
        var actual = Sha256(path);

        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            throw new InvalidOperationException(
                $"{Path.GetFileName(path)} failed its checksum: expected {expected}, got {actual}");
        }
    }
}
