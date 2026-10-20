using System.Buffers.Binary;
using System.Globalization;

namespace Cabinet.Core;

public static class PortableExecutable
{
    private const ushort DosMagic = 0x5A4D;
    private const uint Signature = 0x00004550;
    private const int NewHeader = 0x3C;
    private const ushort SixtyFourBit = 0x20B;
    private const int ResourceEntry = 2;
    private const int SectionSize = 40;
    private const uint VersionResource = 16;
    private const uint Subdirectory = 0x80000000;
    private const uint FixedSignature = 0xFEEF04BD;
    private const int FixedInfo = 40;

    public static string? Version(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            return Version(file);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException
                                       or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? Version(FileStream file)
    {
        if (U16(Read(file, 0, 2), 0) != DosMagic)
        {
            return null;
        }

        var header = (long)U32(Read(file, NewHeader, 4), 0);
        var coff = Read(file, header, 24);

        if (U32(coff, 0) != Signature)
        {
            return null;
        }

        var sections = U16(coff, 6);
        var optionalSize = U16(coff, 20);
        var optional = Read(file, header + 24, optionalSize);
        var directories = U16(optional, 0) == SixtyFourBit ? 112 : 96;
        var resources = U32(optional, directories + ResourceEntry * 8);
        var table = Read(file, header + 24 + optionalSize, sections * SectionSize);

        long Offset(uint address)
        {
            for (var at = 0; at < table.Length; at += SectionSize)
            {
                var start = U32(table, at + 12);
                var length = Math.Max(U32(table, at + 8), U32(table, at + 16));

                if (address >= start && address - start < length)
                {
                    return U32(table, at + 20) + (address - start);
                }
            }

            throw new InvalidDataException($"address {address:X} lies in no section");
        }

        if (resources == 0)
        {
            return null;
        }

        var root = Offset(resources);
        var type = Child(file, root, VersionResource);
        var name = type is null ? null : Child(file, root + (type.Value & ~Subdirectory), null);
        var language = name is null ? null : Child(file, root + (name.Value & ~Subdirectory), null);

        if (language is not { } leaf || (leaf & Subdirectory) != 0)
        {
            return null;
        }

        var data = Read(file, root + leaf, 8);
        var block = Read(file, Offset(U32(data, 0)), FixedInfo + 24);

        if (U32(block, FixedInfo) != FixedSignature)
        {
            return null;
        }

        var high = U32(block, FixedInfo + 16);
        var low = U32(block, FixedInfo + 20);

        return high == 0 && low == 0
            ? null
            : string.Join('.', new[] { high >> 16, high & 0xFFFF, low >> 16, low & 0xFFFF }
                .Select(part => part.ToString(CultureInfo.InvariantCulture)));
    }

    private static uint? Child(FileStream file, long directory, uint? id)
    {
        var header = Read(file, directory, 16);
        var count = U16(header, 12) + U16(header, 14);
        var entries = Read(file, directory + 16, count * 8);

        for (var at = 0; at < entries.Length; at += 8)
        {
            if (id is null || U32(entries, at) == id)
            {
                return U32(entries, at + 4);
            }
        }

        return null;
    }

    private static byte[] Read(FileStream file, long offset, int count)
    {
        var buffer = new byte[count];
        file.Position = offset;

        return file.ReadAtLeast(buffer, count, throwOnEndOfStream: false) == count
            ? buffer
            : throw new InvalidDataException($"{file.Name} ends before byte {offset + count}");
    }

    private static ushort U16(byte[] bytes, int at) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));

    private static uint U32(byte[] bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
}
