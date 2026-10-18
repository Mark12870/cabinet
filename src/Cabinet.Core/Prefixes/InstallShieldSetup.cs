using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Cabinet.Core;

public static class InstallShieldSetup
{
    private const int Chunk = 1024;
    private const uint Compressed = 2;
    private const uint Encoded = 4;

    private static ReadOnlySpan<byte> Marker => "ISSetupStream\0"u8;

    private static ReadOnlySpan<byte> Seed => [0x13, 0x35, 0x86, 0x07];

    private const int StreamHeader = 2 + 4 + 8 + 2 + 16;
    private const int FileHeader = 4 + 4 + 2 + 4 + 8 + 2;

    public static void Extract(string setup, string name, string target)
    {
        var data = File.ReadAllBytes(setup);
        var start = data.AsSpan().LastIndexOf(Marker);

        if (start < 0)
        {
            throw new InvalidDataException(
                $"{Path.GetFileName(setup)} is not an InstallShield setup — it carries no ISSetupStream");
        }

        var at = start + Marker.Length;
        var count = BinaryPrimitives.ReadUInt16LittleEndian(Slice(setup, data, at, StreamHeader));
        at += StreamHeader;

        for (var file = 0; file < count; file++)
        {
            var header = Slice(setup, data, at, FileHeader);
            var nameLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header));
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header[10..]));
            at += FileHeader;

            var stored = Encoding.Unicode.GetString(Slice(setup, data, at, nameLength));
            at += nameLength;
            var payload = Slice(setup, data, at, size);
            at += size;

            if (stored == name)
            {
                Write(Decoded(payload, stored, flags), flags, target);
                return;
            }
        }

        throw new FileNotFoundException($"{Path.GetFileName(setup)} carries no {name}", name);
    }

    private static byte[] Key(string name)
    {
        var key = Encoding.UTF8.GetBytes(name);

        for (var at = 0; at < key.Length; at++)
        {
            key[at] ^= Seed[at % Seed.Length];
        }

        return key;
    }

    private static byte SwapNibbles(byte value) => (byte)((value << 4) | (value >> 4));

    private static byte[] Decoded(ReadOnlySpan<byte> payload, string name, uint flags)
    {
        var plain = payload.ToArray();

        if ((flags & Encoded) == 0)
        {
            return plain;
        }

        var key = Key(name);

        for (var at = 0; at < plain.Length; at++)
        {
            plain[at] = (byte)~(key[at % Chunk % key.Length] ^ SwapNibbles(plain[at]));
        }

        return plain;
    }

    private static void Write(byte[] plain, uint flags, string target)
    {
        using var output = File.Create(target);

        if ((flags & Compressed) == 0)
        {
            output.Write(plain);
            return;
        }

        using var inflated = new ZLibStream(new MemoryStream(plain), CompressionMode.Decompress);
        inflated.CopyTo(output);
    }

    private static ReadOnlySpan<byte> Slice(string setup, byte[] data, int at, int length) =>
        length >= 0 && at <= data.Length - length
            ? data.AsSpan(at, length)
            : throw new InvalidDataException(
                $"{Path.GetFileName(setup)} ends inside its ISSetupStream");
}
