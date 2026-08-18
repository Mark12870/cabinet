using System.Security.Cryptography;

namespace Cabinet.Core;

public static class Checksum
{
    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string Md5(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(MD5.HashData(stream)).ToLowerInvariant();
    }

    public static void Expect(string path, string expected) => Match(path, Sha256(path), expected);

    public static void ExpectMd5(string path, string expected) => Match(path, Md5(path), expected);

    private static void Match(string path, string actual, string expected)
    {
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            throw new InvalidOperationException(
                $"{Path.GetFileName(path)} failed its checksum: expected {expected}, got {actual}");
        }
    }
}
