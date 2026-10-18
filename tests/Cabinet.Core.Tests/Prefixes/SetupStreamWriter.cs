using System.IO.Compression;
using System.Text;

namespace Cabinet.Core.Tests;

internal static class SetupStreamWriter
{
    private const int Chunk = 1024;
    private static readonly byte[] Seed = [0x13, 0x35, 0x86, 0x07];

    internal sealed record Stored(string Name, uint Flags, byte[] Payload);

    public static Stored Plain(string name, byte[] content) => new(name, 0, content);

    public static Stored Encoded(string name, byte[] content) => new(name, 4, Encode(name, content));

    public static Stored Deflated(string name, byte[] content) =>
        new(name, 6, Encode(name, Deflate(content)));

    public static byte[] Setup(params Stored[] files)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write("MZ"u8);
        writer.Write(new byte[126]);
        writer.Write("ISSetupStream\0"u8);
        writer.Write((ushort)files.Length);
        writer.Write(new byte[30]);

        foreach (var file in files)
        {
            var name = Encoding.Unicode.GetBytes(file.Name);
            writer.Write((uint)name.Length);
            writer.Write(file.Flags);
            writer.Write((ushort)0);
            writer.Write((uint)file.Payload.Length);
            writer.Write(new byte[8]);
            writer.Write((ushort)1);
            writer.Write(name);
            writer.Write(file.Payload);
        }

        writer.Flush();
        return stream.ToArray();
    }

    public static byte[] Content(int length) =>
        [.. Enumerable.Range(0, length).Select(at => (byte)(at * 7 % 251))];

    private static byte[] Encode(string name, byte[] plain)
    {
        var key = Encoding.UTF8.GetBytes(name).Select((value, at) => (byte)(value ^ Seed[at % Seed.Length])).ToArray();

        return [.. plain.Select((value, at) => Swap((byte)(~value ^ key[at % Chunk % key.Length])))];
    }

    private static byte Swap(byte value) => (byte)((value << 4) | (value >> 4));

    private static byte[] Deflate(byte[] plain)
    {
        using var packed = new MemoryStream();

        using (var zlib = new ZLibStream(packed, CompressionLevel.Optimal))
        {
            zlib.Write(plain);
        }

        return packed.ToArray();
    }
}
