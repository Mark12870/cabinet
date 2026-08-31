using System.Buffers.Binary;
using System.Text;

namespace Cabinet.Core.Tests;

internal static class SharedObject
{
    public const int Strings = 0x40;
    public const int First = Strings + 1;
    public const int Second = First + 20;
    public const string FirstSoname = "libcurl-gnutls.so.4";
    public const string SecondSoname = "libm.so.6";

    public static byte[] Bytes()
    {
        var image = new byte[0x150];

        image[0] = 0x7F;
        Encoding.ASCII.GetBytes("ELF").CopyTo(image, 1);
        image[4] = 2;
        image[5] = 1;
        image[6] = 1;
        Write16(image, 0x10, 3);
        Write16(image, 0x12, 0x3E);
        Write32(image, 0x14, 1);
        Write64(image, 0x28, 0x90);
        Write16(image, 0x34, 0x40);
        Write16(image, 0x3A, 0x40);
        Write16(image, 0x3C, 3);

        Encoding.ASCII.GetBytes(FirstSoname).CopyTo(image, First);
        Encoding.ASCII.GetBytes(SecondSoname).CopyTo(image, Second);

        Write64(image, 0x60, 1);
        Write64(image, 0x68, First - Strings);
        Write64(image, 0x70, 1);
        Write64(image, 0x78, Second - Strings);

        Write32(image, 0xD4, 3);
        Write64(image, 0xE8, Strings);
        Write64(image, 0xF0, 31);

        Write32(image, 0x114, 6);
        Write64(image, 0x128, 0x60);
        Write64(image, 0x130, 48);
        Write32(image, 0x138, 1);

        return image;
    }

    public static string Soname(byte[] image, int at) =>
        Encoding.ASCII.GetString(image, at, Array.IndexOf(image, (byte)0, at) - at);

    private static void Write16(byte[] image, int at, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(at), value);

    private static void Write32(byte[] image, int at, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(at), value);

    private static void Write64(byte[] image, int at, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(image.AsSpan(at), value);
}
