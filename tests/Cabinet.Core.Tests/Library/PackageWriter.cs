using System.Buffers.Binary;
using System.Text;

namespace Cabinet.Core.Tests;

internal sealed class PackageWriter
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz._";
    private const int SectorSize = 512;
    private const int MiniSectorSize = 64;
    private const int MiniCutoff = 4096;
    private const uint Free = 0xFFFFFFFF;
    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint FatSector = 0xFFFFFFFD;
    private const int StringType = 0x0D00;
    private const int ShortType = 0x0502;

    private readonly List<string> strings = [""];
    private readonly List<(string Table, int Number, string Name, int Type)> columns = [];
    private readonly List<(string Name, byte[] Data)> streams = [];
    private int references = 2;

    public PackageWriter Table(string name, string[] header, params object?[][] rows)
    {
        var types = header.Select(column => column.EndsWith(":i2", StringComparison.Ordinal) ? ShortType : StringType).ToArray();
        var names = header.Select(column => column.Split(':')[0]).ToArray();

        for (var index = 0; index < names.Length; index++)
        {
            columns.Add((name, index + 1, names[index], types[index]));
        }

        using var data = new MemoryStream();

        for (var column = 0; column < names.Length; column++)
        {
            foreach (var row in rows)
            {
                var cell = row[column] switch
                {
                    null => 0,
                    int number => number + 0x8000,
                    string text => Id(text),
                    _ => throw new ArgumentException("a cell is text, a number or null"),
                };
                data.Write(BitConverter.GetBytes(cell).AsSpan(0, types[column] == StringType ? references : 2));
            }
        }

        streams.Add(("!" + name, data.ToArray()));
        return this;
    }

    public PackageWriter WidePool()
    {
        references = 3;
        return this;
    }

    public PackageWriter Stream(string name, byte[] data)
    {
        streams.Add((name, data));
        return this;
    }

    public void Save(string path)
    {
        using var described = new MemoryStream();

        foreach (var (part, width) in new (Func<(string Table, int Number, string Name, int Type), int>, int)[]
                 {
                     (column => Id(column.Table), references),
                     (column => column.Number + 0x8000, 2),
                     (column => Id(column.Name), references),
                     (column => column.Type | 0x8000, 2),
                 })
        {
            foreach (var column in columns)
            {
                described.Write(BitConverter.GetBytes(part(column)).AsSpan(0, width));
            }
        }

        var pool = new List<byte>(BitConverter.GetBytes(references == 3 ? 0x80000000u | 1252u : 1252u));
        var text = new List<byte>();

        foreach (var value in strings.Skip(1))
        {
            var bytes = Encoding.Latin1.GetBytes(value);
            pool.AddRange(BitConverter.GetBytes((ushort)bytes.Length));
            pool.AddRange(BitConverter.GetBytes((ushort)1));
            text.AddRange(bytes);
        }

        File.WriteAllBytes(path, Compound(
        [
            ("!_Columns", described.ToArray()),
            ("!_StringPool", [.. pool]),
            ("!_StringData", [.. text]),
            .. streams,
        ]));
    }

    private int Id(string text)
    {
        var at = strings.IndexOf(text);

        if (at > 0)
        {
            return at;
        }

        strings.Add(text);
        return strings.Count - 1;
    }

    private static string Encoded(string name)
    {
        var encoded = new StringBuilder();
        var at = 0;

        if (name.StartsWith('!'))
        {
            encoded.Append('䡀');
            at = 1;
        }

        while (at < name.Length)
        {
            var first = Alphabet.IndexOf(name[at]);

            if (at + 1 < name.Length && Alphabet.IndexOf(name[at + 1]) is >= 0 and var second)
            {
                encoded.Append((char)(0x3800 + first + (second << 6)));
                at += 2;
            }
            else
            {
                encoded.Append((char)(0x4800 + first));
                at++;
            }
        }

        return encoded.ToString();
    }

    private static byte[] Compound(IReadOnlyList<(string Name, byte[] Data)> content)
    {
        var mini = new List<byte>();
        var miniFat = new List<uint>();
        var big = new List<(byte[] Data, int Sectors)>();
        var starts = new uint[content.Count];

        for (var index = 0; index < content.Count; index++)
        {
            var data = content[index].Data;

            if (data.Length < MiniCutoff)
            {
                var sectors = Math.Max(1, (data.Length + MiniSectorSize - 1) / MiniSectorSize);
                starts[index] = (uint)miniFat.Count;

                for (var sector = 0; sector < sectors; sector++)
                {
                    miniFat.Add(sector == sectors - 1 ? EndOfChain : (uint)miniFat.Count + 1);
                }

                mini.AddRange(data);
                mini.AddRange(new byte[sectors * MiniSectorSize - data.Length]);
            }
            else
            {
                big.Add((data, (data.Length + SectorSize - 1) / SectorSize));
            }
        }

        var directorySectors = ((content.Count + 1) * 128 + SectorSize - 1) / SectorSize;
        var miniFatSectors = (miniFat.Count * 4 + SectorSize - 1) / SectorSize;
        var miniStreamSectors = (mini.Count + SectorSize - 1) / SectorSize;
        var dataSectors = directorySectors + miniFatSectors + miniStreamSectors + big.Sum(stream => stream.Sectors);
        var fatSectors = 1;

        while (fatSectors * (SectorSize / 4) < dataSectors + fatSectors)
        {
            fatSectors++;
        }

        var fat = new List<uint>();
        var image = new MemoryStream();

        uint Chain(int sectors)
        {
            var first = (uint)fat.Count;

            for (var sector = 0; sector < sectors; sector++)
            {
                fat.Add(sector == sectors - 1 ? EndOfChain : (uint)fat.Count + 1);
            }

            return sectors == 0 ? EndOfChain : first;
        }

        var directoryStart = Chain(directorySectors);
        var miniFatStart = Chain(miniFatSectors);
        var miniStreamStart = Chain(miniStreamSectors);
        var bigIndex = 0;

        for (var index = 0; index < content.Count; index++)
        {
            if (content[index].Data.Length >= MiniCutoff)
            {
                starts[index] = Chain(big[bigIndex++].Sectors);
            }
        }

        var fatStart = fat.Count;

        for (var sector = 0; sector < fatSectors; sector++)
        {
            fat.Add(FatSector);
        }

        var header = new byte[SectorSize];
        new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(24), 0x3E);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(26), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(28), 0xFFFE);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(30), 9);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(32), 6);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(44), (uint)fatSectors);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(48), directoryStart);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(56), MiniCutoff);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(60), miniFatStart);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(64), (uint)miniFatSectors);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(68), EndOfChain);

        for (var slot = 0; slot < 109; slot++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                header.AsSpan(76 + slot * 4), slot < fatSectors ? (uint)(fatStart + slot) : Free);
        }

        image.Write(header);

        var directory = new byte[directorySectors * SectorSize];
        Entry(directory, 0, "Root Entry", 5, miniStreamStart, mini.Count);

        for (var index = 0; index < content.Count; index++)
        {
            Entry(directory, index + 1, Encoded(content[index].Name), 2, starts[index], content[index].Data.Length);
        }

        image.Write(directory);
        image.Write(Padded([.. miniFat.SelectMany(BitConverter.GetBytes)], miniFatSectors, 0xFF));
        image.Write(Padded([.. mini], miniStreamSectors, 0));

        foreach (var (data, sectors) in big)
        {
            image.Write(Padded(data, sectors, 0));
        }

        while (fat.Count < fatSectors * (SectorSize / 4))
        {
            fat.Add(Free);
        }

        image.Write([.. fat.SelectMany(BitConverter.GetBytes)]);
        return image.ToArray();
    }

    private static void Entry(byte[] directory, int index, string name, byte type, uint start, long size)
    {
        var entry = directory.AsSpan(index * 128, 128);
        var encoded = Encoding.Unicode.GetBytes(name);
        encoded.CopyTo(entry);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[64..], (ushort)(encoded.Length + 2));
        entry[66] = type;
        BinaryPrimitives.WriteUInt32LittleEndian(entry[68..], Free);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[72..], Free);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[76..], Free);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[116..], start);
        BinaryPrimitives.WriteUInt64LittleEndian(entry[120..], (ulong)size);
    }

    private static byte[] Padded(byte[] data, int sectors, byte fill)
    {
        var padded = Enumerable.Repeat(fill, sectors * SectorSize).ToArray();
        data.CopyTo(padded, 0);
        return padded;
    }
}
