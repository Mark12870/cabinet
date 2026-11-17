using System.Buffers.Binary;
using System.Text;

namespace Cabinet.Runtime.Tests.Scenarios;

internal static class Take
{
    private const int Rate = 48000;
    private const int Seconds = 8;
    private const int Harmonics = 15;
    private const double NoiseLevel = 0.02;

    public static void WriteVoice(string path)
    {
        var random = new Random(7);
        var samples = new short[Rate * Seconds];
        for (var n = 0; n < samples.Length; n++)
        {
            var time = (double)n / Rate;
            var voiced = time >= 1 && time < Seconds - 1;
            var envelope = voiced ? 0.5 - 0.5 * Math.Cos(2 * Math.PI * 3 * time) : 0;
            var sweep = 2 * Math.PI * (140 + 20 * Math.Sin(2 * Math.PI * 0.5 * time)) * time;
            var tone = Enumerable.Range(1, Harmonics).Sum(k => Math.Sin(k * sweep) / k);
            var noise = Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
            samples[n] = (short)(Math.Clamp(0.25 * envelope * tone + NoiseLevel * noise, -1, 1) * short.MaxValue);
        }

        using var file = new BinaryWriter(File.Create(path));
        file.Write("RIFF"u8);
        file.Write(36 + samples.Length * 2);
        file.Write("WAVEfmt "u8);
        file.Write(16);
        file.Write((short)1);
        file.Write((short)1);
        file.Write(Rate);
        file.Write(Rate * 2);
        file.Write((short)2);
        file.Write((short)16);
        file.Write("data"u8);
        file.Write(samples.Length * 2);
        foreach (var sample in samples)
        {
            file.Write(sample);
        }
    }

    public static TakeMeasurement Compare(string input, string output)
    {
        var before = FirstChannel(input);
        var after = FirstChannel(output);
        var length = Math.Min(before.Length, after.Length);
        double[] Edges(double[] samples) => [.. samples[..Rate], .. samples[(length - Rate)..length]];
        double[] Middle(double[] samples) => samples[Rate..(length - Rate)];
        var difference = Enumerable.Range(0, length).Select(n => after[n] - before[n]).ToArray();

        return new TakeMeasurement(
            Decibels(Edges(before)) - Decibels(Edges(after)),
            Decibels(Middle(after)) - Decibels(Middle(before)),
            Decibels(difference));
    }

    private static double Decibels(double[] samples) =>
        20 * Math.Log10(Math.Max(Math.Sqrt(samples.Sum(sample => sample * sample) / samples.Length), 1e-9));

    private static double[] FirstChannel(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var (channels, bits, floating) = (0, 0, false);
        for (var at = 12; at + 8 <= bytes.Length;)
        {
            var id = Encoding.ASCII.GetString(bytes, at, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 4));
            var body = bytes.AsSpan(at + 8, Math.Min(size, bytes.Length - at - 8));
            if (id == "fmt ")
            {
                var tag = BinaryPrimitives.ReadUInt16LittleEndian(body);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(body[14..]);
                floating = tag == 3 || (tag == 0xFFFE && BinaryPrimitives.ReadUInt16LittleEndian(body[24..]) == 3);
            }
            else if (id == "data")
            {
                return Samples(body, channels, bits, floating);
            }

            at += 8 + size + (size & 1);
        }

        throw new InvalidDataException($"{path} holds no audio");
    }

    private static double[] Samples(ReadOnlySpan<byte> data, int channels, int bits, bool floating)
    {
        var width = bits / 8;
        var frames = new double[data.Length / (width * channels)];
        for (var frame = 0; frame < frames.Length; frame++)
        {
            var sample = data.Slice(frame * width * channels, width);
            frames[frame] = (floating, width) switch
            {
                (true, 4) => BinaryPrimitives.ReadSingleLittleEndian(sample),
                (true, 8) => BinaryPrimitives.ReadDoubleLittleEndian(sample),
                (false, 2) => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768.0,
                (false, 3) => ((sample[2] << 24) | (sample[1] << 16) | (sample[0] << 8)) / 2147483648.0,
                (false, 4) => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648.0,
                _ => throw new InvalidDataException($"cannot read {bits}-bit audio"),
            };
        }

        return frames;
    }
}

internal sealed record TakeMeasurement(double NoiseReduction, double VoiceChange, double Difference);
