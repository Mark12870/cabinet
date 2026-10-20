using System.Buffers.Binary;
using System.Text;

namespace Cabinet.Core.Tests;

public static class Executable
{
    private const int Header = 0x40;
    private const int Optional = Header + 24;
    private const int Sections = Optional + 0xF0;
    private const int Resources = 0x200;
    private const uint Address = 0x1000;
    private const ushort ThirtyTwoBit = 0x10B;
    private const ushort SixtyFourBit = 0x20B;
    private const uint VersionType = 16;
    private const uint IconType = 3;

    public static byte[] Bytes(ushort major, ushort minor, ushort patch, ushort build) =>
        Image(SixtyFourBit, 112, VersionType, major, minor, patch, build);

    public static byte[] ThirtyTwoBitBytes(ushort major, ushort minor, ushort patch, ushort build) =>
        Image(ThirtyTwoBit, 96, VersionType, major, minor, patch, build);

    public static byte[] WithoutResources()
    {
        var image = Bytes(1, 0, 0, 0);
        Write32(image, Optional + 112 + 16, 0);
        return image;
    }

    public static byte[] WithoutVersionResource() => Image(SixtyFourBit, 112, IconType, 1, 0, 0, 0);

    private static byte[] Image(
        ushort magic, int directories, uint type, ushort major, ushort minor, ushort patch, ushort build)
    {
        var image = new byte[0x400];

        Encoding.ASCII.GetBytes("MZ").CopyTo(image, 0);
        Write32(image, 0x3C, Header);
        Encoding.ASCII.GetBytes("PE").CopyTo(image, Header);
        Write16(image, Header + 4, 0x8664);
        Write16(image, Header + 6, 1);
        Write16(image, Header + 20, 0xF0);
        Write16(image, Optional, magic);
        Write32(image, Optional + directories + 16, Address);
        Write32(image, Optional + directories + 20, 0x200);

        Encoding.ASCII.GetBytes(".rsrc").CopyTo(image, Sections);
        Write32(image, Sections + 8, 0x200);
        Write32(image, Sections + 12, Address);
        Write32(image, Sections + 16, 0x200);
        Write32(image, Sections + 20, Resources);

        ResourceDirectory(image, 0x00, type, 0x80000018);
        ResourceDirectory(image, 0x18, 1, 0x80000030);
        ResourceDirectory(image, 0x30, 0x409, 0x48);
        Write32(image, Resources + 0x48, Address + 0x60);
        Write32(image, Resources + 0x4C, 0x5C);

        var block = Resources + 0x60;
        Write16(image, block, 0x5C);
        Write16(image, block + 2, 0x34);
        Encoding.Unicode.GetBytes("VS_VERSION_INFO").CopyTo(image, block + 6);
        Write32(image, block + 40, 0xFEEF04BD);
        Write32(image, block + 44, 0x10000);
        Write32(image, block + 56, (uint)(major << 16 | minor));
        Write32(image, block + 60, (uint)(patch << 16 | build));

        return image;
    }

    private static void ResourceDirectory(byte[] image, int at, uint id, uint target)
    {
        Write16(image, Resources + at + 14, 1);
        Write32(image, Resources + at + 16, id);
        Write32(image, Resources + at + 20, target);
    }

    private static void Write16(byte[] image, int at, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(at), value);

    private static void Write32(byte[] image, int at, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(at), value);
}
