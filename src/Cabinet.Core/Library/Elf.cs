using System.Buffers.Binary;
using System.Text;

namespace Cabinet.Core;

public static class Elf
{
    private static readonly byte[] Magic = [0x7F, (byte)'E', (byte)'L', (byte)'F'];

    private const int SixtyFourBit = 2;
    private const int LittleEndian = 1;
    private const uint DynamicSection = 6;
    private const ulong End = 0;
    private const ulong Needed = 1;
    private const int SectionHeader = 0x28;
    private const int SectionSize = 0x3A;
    private const int SectionCount = 0x3C;
    private const int SectionOffset = 0x18;
    private const int SectionLength = 0x20;
    private const int SectionLink = 0x28;
    private const int SectionType = 0x04;
    private const int EntrySize = 16;

    public static bool Relink(string path, string from, string to)
    {
        if (to.Length > from.Length)
        {
            throw new ArgumentException(
                $"cannot relink {from} to the longer {to} — a string table cannot grow in place",
                nameof(to));
        }

        if (Image(path) is not { } image)
        {
            return false;
        }

        var rewritten = false;

        foreach (var at in Sonames(image)
                     .Where(found => found.Name == from)
                     .Select(found => found.At))
        {
            Array.Clear(image, at, from.Length);
            Encoding.ASCII.GetBytes(to, 0, to.Length, image, at);
            rewritten = true;
        }

        if (rewritten)
        {
            File.WriteAllBytes(path, image);
        }

        return rewritten;
    }

    private static byte[]? Image(string path)
    {
        using (var head = File.OpenRead(path))
        {
            var opening = new byte[6];

            if (head.ReadAtLeast(opening, opening.Length, false) < opening.Length
                || !opening.AsSpan(0, Magic.Length).SequenceEqual(Magic)
                || opening[4] != SixtyFourBit
                || opening[5] != LittleEndian)
            {
                return null;
            }
        }

        return File.ReadAllBytes(path);
    }

    private static IEnumerable<(string Name, int At)> Sonames(byte[] image)
    {
        var headers = (int)Read64(image, SectionHeader);
        var size = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(SectionSize));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(SectionCount));

        if (size < 0x40 || headers <= 0 || headers + count * size > image.Length)
        {
            yield break;
        }

        for (var index = 0; index < count; index++)
        {
            var header = headers + index * size;

            if (Read32(image, header + SectionType) != DynamicSection)
            {
                continue;
            }

            var link = (int)Read32(image, header + SectionLink);

            if (link >= count)
            {
                continue;
            }

            var strings = (int)Read64(image, headers + link * size + SectionOffset);
            var offset = (int)Read64(image, header + SectionOffset);
            var length = (int)Read64(image, header + SectionLength);

            if (offset < 0 || length < 0 || offset + length > image.Length || strings < 0)
            {
                continue;
            }

            for (var at = offset; at + EntrySize <= offset + length; at += EntrySize)
            {
                var tag = Read64(image, at);

                if (tag == End)
                {
                    break;
                }

                if (tag != Needed)
                {
                    continue;
                }

                var start = strings + (int)Read64(image, at + 8);

                if (start > 0 && start < image.Length)
                {
                    yield return (Text(image, start), start);
                }
            }
        }
    }

    private static string Text(byte[] image, int at)
    {
        var end = Array.IndexOf(image, (byte)0, at);
        return Encoding.ASCII.GetString(image, at, (end < 0 ? image.Length : end) - at);
    }

    private static ulong Read64(byte[] image, int at) =>
        BinaryPrimitives.ReadUInt64LittleEndian(image.AsSpan(at));

    private static uint Read32(byte[] image, int at) =>
        BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at));
}
