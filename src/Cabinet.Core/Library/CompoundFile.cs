using System.Buffers.Binary;
using System.Text;

namespace Cabinet.Core;

public sealed class CompoundFile
{
    private static readonly byte[] Magic = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private const uint LastSector = 0xFFFFFFFA;
    private const int HeaderFatSectors = 109;
    private const int EntrySize = 128;
    private const byte StreamEntry = 2;
    private const byte RootEntry = 5;

    private readonly byte[] image;
    private readonly int sectorSize;
    private readonly int miniSectorSize;
    private readonly uint miniCutoff;
    private readonly List<uint> fat = [];
    private readonly List<uint> miniFat = [];
    private readonly byte[] miniStream = [];
    private readonly Dictionary<string, (uint Start, long Size)> streams = new(StringComparer.Ordinal);

    public CompoundFile(byte[] image)
    {
        if (image.Length < 512 || !image.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidDataException("not a Windows Installer package: no compound file header");
        }

        this.image = image;
        sectorSize = 1 << U16(30);
        miniSectorSize = 1 << U16(32);
        miniCutoff = U32(56);

        var fatSectors = new List<uint>();

        for (var index = 0; index < HeaderFatSectors; index++)
        {
            fatSectors.Add(U32(76 + index * 4));
        }

        var extension = U32(68);

        for (var remaining = U32(72); remaining > 0 && extension < LastSector; remaining--)
        {
            var entries = Sector(extension);
            var count = sectorSize / 4 - 1;

            for (var index = 0; index < count; index++)
            {
                fatSectors.Add(BinaryPrimitives.ReadUInt32LittleEndian(entries[(index * 4)..]));
            }

            extension = BinaryPrimitives.ReadUInt32LittleEndian(entries[(count * 4)..]);
        }

        foreach (var sector in fatSectors.Take((int)U32(44)))
        {
            Words(Sector(sector), fat);
        }

        foreach (var sector in Chain(U32(60), fat))
        {
            Words(Sector(sector), miniFat);
        }

        var directory = Concatenated(Chain(U32(48), fat));

        for (var at = 0; at + EntrySize <= directory.Length; at += EntrySize)
        {
            var entry = directory.AsSpan(at, EntrySize);
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(entry[64..]);
            var start = BinaryPrimitives.ReadUInt32LittleEndian(entry[116..]);
            var size = sectorSize == 512
                ? BinaryPrimitives.ReadUInt32LittleEndian(entry[120..])
                : (long)BinaryPrimitives.ReadUInt64LittleEndian(entry[120..]);

            if (entry[66] == RootEntry)
            {
                miniStream = Concatenated(Chain(start, fat));
            }
            else if (entry[66] == StreamEntry && nameLength >= 2)
            {
                streams[Encoding.Unicode.GetString(entry[..(nameLength - 2)])] = (start, size);
            }
        }
    }

    public IEnumerable<string> Names => streams.Keys;

    public byte[] Read(string name)
    {
        var (start, size) = streams[name];

        if (size >= miniCutoff)
        {
            return Concatenated(Chain(start, fat))[..(int)size];
        }

        var stream = new byte[size];
        var filled = 0;

        foreach (var sector in Chain(start, miniFat).TakeWhile(_ => filled < size))
        {
            var length = Math.Min(miniSectorSize, stream.Length - filled);
            miniStream.AsSpan((int)sector * miniSectorSize, length).CopyTo(stream.AsSpan(filled));
            filled += length;
        }

        return stream;
    }

    private ushort U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(at));

    private uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at));

    private ReadOnlySpan<byte> Sector(uint sector) =>
        image.AsSpan((int)((sector + 1) * sectorSize), sectorSize);

    private byte[] Concatenated(IEnumerable<uint> sectors)
    {
        using var joined = new MemoryStream();

        foreach (var sector in sectors)
        {
            joined.Write(Sector(sector));
        }

        return joined.ToArray();
    }

    private static IEnumerable<uint> Chain(uint start, List<uint> table)
    {
        var seen = new HashSet<uint>();

        for (var sector = start; sector < LastSector; sector = table[(int)sector])
        {
            if (!seen.Add(sector) || sector >= table.Count)
            {
                throw new InvalidDataException("a Windows Installer package has a broken sector chain");
            }

            yield return sector;
        }
    }

    private static void Words(ReadOnlySpan<byte> sector, List<uint> into)
    {
        for (var at = 0; at + 4 <= sector.Length; at += 4)
        {
            into.Add(BinaryPrimitives.ReadUInt32LittleEndian(sector[at..]));
        }
    }
}
