using System.Security;
using System.Text;

namespace Cabinet.Runtime.Tests.Scenarios;

internal static class Content
{
    private const int SampleRate = 48000;

    public static string Hit(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const int frames = SampleRate / 2;
        using var file = new BinaryWriter(File.Create(path));
        file.Write("RIFF"u8);
        file.Write(36 + frames * 2);
        file.Write("WAVEfmt "u8);
        file.Write(16);
        file.Write((short)1);
        file.Write((short)1);
        file.Write(SampleRate);
        file.Write(SampleRate * 2);
        file.Write((short)2);
        file.Write((short)16);
        file.Write("data"u8);
        file.Write(frames * 2);
        for (var frame = 0; frame < frames; frame++)
        {
            var time = (double)frame / SampleRate;
            file.Write((short)(short.MaxValue * 0.5 * Math.Exp(-8 * time) * Math.Sin(2 * Math.PI * 110 * time)));
        }

        return path;
    }

    public static string Lv2State(string path, string uri, string key, string value) =>
        State(
            path,
            $"<Type>LV2</Type><URI>{Escape(uri)}</URI>",
            "<CustomData><Type>http://lv2plug.in/ns/ext/atom#Chunk</Type>"
            + $"<Key>{Escape(key)}</Key><Value>\n{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}\n    </Value>"
            + "</CustomData>");

    public static string ChunkState(string path, string format, string binary, byte[] chunk) =>
        State(
            path,
            $"<Type>{format}</Type><Binary>{Escape(binary)}</Binary>",
            $"<Chunk>{Convert.ToBase64String(chunk)}</Chunk>");

    public static byte[] Juce(string xml)
    {
        var text = Encoding.UTF8.GetBytes(xml);
        using var chunk = new MemoryStream();
        using var writer = new BinaryWriter(chunk);
        writer.Write("VC2!"u8);
        writer.Write(text.Length);
        writer.Write(text);
        writer.Write((byte)0);
        writer.Flush();
        return chunk.ToArray();
    }

    public static string Escape(string text) => SecurityElement.Escape(text);

    private static string State(string path, string info, string data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            "<?xml version='1.0' encoding='UTF-8'?>\n<!DOCTYPE CARLA-PRESET>\n<CARLA-PRESET VERSION='2.0'>\n"
            + $"<Info>{info}</Info>\n<Data><Active>Yes</Active>{data}</Data>\n</CARLA-PRESET>\n");
        return path;
    }
}
