using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Cabinet.Probes;

internal static unsafe class AudioRender
{
    private const uint SampleRate = 48000;
    private const uint BlockSize = 256;
    private const uint SilenceBefore = SampleRate;
    private const uint SignalFrames = SampleRate / 10;
    private const uint SilenceAfter = SampleRate * 4;
    private const uint TotalFrames = SilenceBefore + SignalFrames + SilenceAfter;
    private const uint RenderedFrames = (TotalFrames + BlockSize - 1) / BlockSize * BlockSize;

    private static TimeInfo* time;

    [StructLayout(LayoutKind.Explicit, Size = 80)]
    private struct TimeInfo
    {
        [FieldOffset(0)] public byte Playing;
        [FieldOffset(8)] public ulong Frame;
        [FieldOffset(16)] public ulong Usecs;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MidiEvent
    {
        public uint Time;
        public byte Port;
        public byte Size;
        public fixed byte Data[4];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HostDescriptor
    {
        public IntPtr Handle;
        public IntPtr ResourceDir;
        public IntPtr UiName;
        public nuint UiParentId;
        public delegate* unmanaged<IntPtr, uint> GetBufferSize;
        public delegate* unmanaged<IntPtr, double> GetSampleRate;
        public delegate* unmanaged<IntPtr, byte> IsOffline;
        public delegate* unmanaged<IntPtr, TimeInfo*> GetTimeInfo;
        public delegate* unmanaged<IntPtr, MidiEvent*, byte> WriteMidiEvent;
        public delegate* unmanaged<IntPtr, uint, float, void> UiParameterChanged;
        public delegate* unmanaged<IntPtr, byte, uint, uint, void> UiMidiProgramChanged;
        public delegate* unmanaged<IntPtr, IntPtr, IntPtr, void> UiCustomDataChanged;
        public delegate* unmanaged<IntPtr, void> UiClosed;
        public delegate* unmanaged<IntPtr, byte, IntPtr, IntPtr, IntPtr> UiOpenFile;
        public delegate* unmanaged<IntPtr, byte, IntPtr, IntPtr, IntPtr> UiSaveFile;
        public delegate* unmanaged<IntPtr, int, int, nint, IntPtr, float, nint> Dispatcher;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PluginDescriptor
    {
        public fixed uint Counts[9];
        public IntPtr Name;
        public IntPtr Label;
        public IntPtr Maker;
        public IntPtr Copyright;
        public delegate* unmanaged<HostDescriptor*, IntPtr> Instantiate;
        public delegate* unmanaged<IntPtr, void> Cleanup;
        public fixed long Unused[9];
        public delegate* unmanaged<IntPtr, void> UiIdle;
        public fixed long UiSetters[3];
        public delegate* unmanaged<IntPtr, void> Activate;
        public delegate* unmanaged<IntPtr, void> Deactivate;
        public delegate* unmanaged<IntPtr, float**, float**, uint, MidiEvent*, uint, void> Process;
    }

    public static int Run(string[] arguments)
    {
        var (pluginPath, format, mix, artefacts, binaries) =
            (arguments[0], arguments[1], arguments[2], arguments[3], arguments[4]);
        var audio = arguments[5] == "1";
        var note = int.TryParse(arguments[6], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
        var (preset, click) = (arguments[7], arguments[8]);

        var type = format switch
        {
            "vst2" => Carla.PluginVst2,
            "vst3" => Carla.PluginVst3,
            "lv2" => Carla.PluginLv2,
            _ => 0,
        };

        if (type == 0)
        {
            return Error("setup", "unsupported plugin format");
        }

        if (note > 127 || (!audio && note < 0))
        {
            return Error("setup", "nothing to render, or a note outside MIDI's range");
        }

        try
        {
            Directory.CreateDirectory(artefacts);
        }
        catch (IOException)
        {
        }

        if (!Directory.Exists(artefacts) || (type != Carla.PluginLv2 && !Path.Exists(pluginPath))
                                          || !Directory.Exists(binaries))
        {
            return Error("setup", "invalid plugin, artefact, or Carla binaries path");
        }

        var standalone = NativeLibrary.Load(Path.Combine(binaries, "libcarla_standalone2.so"));
        var native = NativeLibrary.Load(Path.Combine(binaries, "libcarla_native-plugin.so"));
        var descriptor = ((delegate* unmanaged<PluginDescriptor*>)NativeLibrary.GetExport(native, "carla_get_native_rack_plugin"))();

        if (descriptor == null || descriptor->Instantiate == null || descriptor->Cleanup == null
            || descriptor->Activate == null || descriptor->Deactivate == null || descriptor->Process == null
            || descriptor->UiIdle == null)
        {
            return Error("setup", "Carla rack descriptor is incomplete");
        }

        time = (TimeInfo*)NativeMemory.AllocZeroed((nuint)sizeof(TimeInfo));
        var nativeHost = (HostDescriptor*)NativeMemory.AllocZeroed((nuint)sizeof(HostDescriptor));
        nativeHost->Handle = (IntPtr)time;
        nativeHost->ResourceDir = Marshal.StringToCoTaskMemUTF8(binaries);
        nativeHost->UiName = Marshal.StringToCoTaskMemUTF8("cabinet-audio-render");
        nativeHost->GetBufferSize = &GetBufferSize;
        nativeHost->GetSampleRate = &GetSampleRate;
        nativeHost->IsOffline = &IsOffline;
        nativeHost->GetTimeInfo = &GetTimeInfo;
        nativeHost->WriteMidiEvent = &WriteMidiEvent;
        nativeHost->UiParameterChanged = &ParameterChanged;
        nativeHost->UiMidiProgramChanged = &MidiProgramChanged;
        nativeHost->UiCustomDataChanged = &CustomDataChanged;
        nativeHost->UiClosed = &UiClosed;
        nativeHost->UiOpenFile = &FileDialog;
        nativeHost->UiSaveFile = &FileDialog;
        nativeHost->Dispatcher = &Dispatcher;

        var rack = descriptor->Instantiate(nativeHost);

        if (rack == IntPtr.Zero)
        {
            return Error("setup", "Carla rack could not be instantiated");
        }

        var created = ((delegate* unmanaged<PluginDescriptor*, IntPtr, IntPtr>)
            NativeLibrary.GetExport(native, "carla_create_native_plugin_host_handle"))(descriptor, rack);

        if (created == IntPtr.Zero)
        {
            return Error("setup", "Carla host handle could not be created");
        }

        var host = new Carla(created, standalone, native);
        host.Binaries(binaries);
        Reached("add_plugin enter");
        var byUri = type == Carla.PluginLv2;
        var added = host.AddPlugin(Carla.BinaryNative, type, byUri ? "" : pluginPath, "audio-render", byUri ? pluginPath : null);
        Reached("add_plugin leave");

        if (!added)
        {
            return Error("load", host.LastError());
        }

        if (host.CurrentPluginCount() != 1 || host.PluginInfo(0) == IntPtr.Zero)
        {
            return Error("load", "Carla did not retain the plugin");
        }

        Reached("parameters enter");

        if (!WriteParameters(Path.Combine(artefacts, "parameters.txt"), host, mix, out var parameterCount, out var mixChanged))
        {
            return Error("setup", "could not report finite plugin parameters");
        }

        Reached("parameters leave");

        var inputLeft = new float[RenderedFrames];
        var inputRight = new float[RenderedFrames];
        var outputLeft = new float[RenderedFrames];
        var outputRight = new float[RenderedFrames];

        if (audio)
        {
            MakeInput(inputLeft, inputRight);
        }

        if (!WriteWav(Path.Combine(artefacts, "input.wav"), inputLeft, inputRight))
        {
            return Error("artefact", "could not write input.wav");
        }

        host.SetActive(0, true);
        Reached("child active");
        descriptor->Activate(rack);
        Reached("rack active");

        if (preset.Length > 0)
        {
            if ((host.OptionsAvailable(0) & Carla.OptionUseChunks) != 0)
            {
                host.SetOption(0, Carla.OptionUseChunks, true);
            }

            if (!host.LoadPluginState(0, preset))
            {
                return Error("preset", host.LastError());
            }

            Reached("preset loaded");
        }

        if (click.Length > 0)
        {
            host.ShowCustomUi(0, true);

            for (var turn = 0; turn < 300; turn++)
            {
                descriptor->UiIdle(rack);
                Thread.Sleep(10);
            }

            var clicked = Gesture(Path.Combine(artefacts, "click.log"), click, () => descriptor->UiIdle(rack));
            host.ShowCustomUi(0, false);

            if (!clicked)
            {
                return Error("click", "xdotool could not click the editor");
            }

            Reached("clicked");
        }

        var quiet = new float[BlockSize];
        var ignoredLeft = new float[BlockSize];
        var ignoredRight = new float[BlockSize];

        if (preset.Length > 0 || click.Length > 0)
        {
            for (var block = 0; block < 5 * SampleRate / BlockSize; block++)
            {
                Process(descriptor, rack, quiet, quiet, ignoredLeft, ignoredRight, 0, null, 0);
                Thread.Sleep(TimeSpan.FromMicroseconds(1000000 * BlockSize / SampleRate));
            }

            Reached("settled");
        }

        var events = stackalloc MidiEvent[2];

        for (uint offset = 0; offset < RenderedFrames; offset += BlockSize)
        {
            time->Playing = 1;
            time->Frame = offset;
            time->Usecs = (ulong)(offset * 1000000.0 / SampleRate);

            if (offset == 0)
            {
                Reached("process enter");
            }

            var count = NoteEvents(note, offset, events);
            Process(descriptor, rack, inputLeft, inputRight, outputLeft, outputRight, offset, events, count);

            if (offset == 0)
            {
                Reached("process leave");
            }
        }

        var peak = 0.0;

        for (var frame = 0; frame < TotalFrames; frame++)
        {
            if (!float.IsFinite(outputLeft[frame]) || !float.IsFinite(outputRight[frame]))
            {
                return Error("nonfinite", "plugin output was not finite");
            }

            peak = Math.Max(peak, Math.Abs(outputLeft[frame]));
            peak = Math.Max(peak, Math.Abs(outputRight[frame]));
        }

        if (!WriteWav(Path.Combine(artefacts, "output.wav"), outputLeft, outputRight))
        {
            return Error("artefact", "could not write output.wav");
        }

        host.SetActive(0, false);

        if (!host.RemoveAllPlugins())
        {
            return Error("cleanup", host.LastError());
        }

        for (var block = 0; block < 16; block++)
        {
            Process(descriptor, rack, ignoredLeft, ignoredRight, ignoredLeft, ignoredRight, 0, null, 0);
        }

        descriptor->Deactivate(rack);

        var tailBegin = SilenceBefore + SignalFrames + SampleRate / 5;
        var tailEnd = SilenceBefore + SignalFrames + SampleRate * 2;
        Console.WriteLine(
            $"AUDIO_RENDER=ok peak={General(peak)} pre_rms={General(Rms(outputLeft, outputRight, 0, SilenceBefore))} "
            + $"signal_rms={General(Rms(outputLeft, outputRight, SilenceBefore, SilenceBefore + SignalFrames))} "
            + $"tail_rms={General(Rms(outputLeft, outputRight, tailBegin, tailEnd))} "
            + $"frames={TotalFrames} params={parameterCount} mix_changed={(mixChanged ? 1 : 0)}");
        Libc.Leave(0);
        return 0;
    }

    private static void Process(
        PluginDescriptor* descriptor, IntPtr rack, float[] inLeft, float[] inRight, float[] outLeft, float[] outRight,
        uint offset, MidiEvent* events, uint count)
    {
        var start = (int)offset;
        var length = (int)BlockSize;

        fixed (float* il = inLeft.AsSpan(start, length))
        fixed (float* ir = inRight.AsSpan(start, length))
        fixed (float* ol = outLeft.AsSpan(start, length))
        fixed (float* orr = outRight.AsSpan(start, length))
        {
            var input = stackalloc float*[] { il, ir };
            var output = stackalloc float*[] { ol, orr };
            descriptor->Process(rack, input, output, BlockSize, events, count);
        }
    }

    private static bool Gesture(string log, string click, Action idle)
    {
        var redirect = $" >> '{log}' 2>&1";
        var commands = new StringBuilder(
            $"window=$(xdotool search --sync --onlyvisible --name audio-render | head -1){redirect}"
            + $" && xdotool windowactivate $window{redirect} && xdotool windowraise $window{redirect}"
            + $" && xdotool mousemove 0 0{redirect} && sleep 1");
        var positions = click.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        for (var at = 0; at + 1 < positions.Length; at += 2)
        {
            commands.Append(
                $" && sleep 1 && xdotool mousemove --window $window {positions[at]} {positions[at + 1]}{redirect}"
                + $" && xdotool getmouselocation{redirect} && xdotool click 1{redirect}");
        }

        commands.Append(" && sleep 1");
        using var gesture = System.Diagnostics.Process.Start("sh", ["-c", commands.ToString()]);

        for (var turn = 0; turn < 3000 && !gesture.HasExited; turn++)
        {
            idle();
            Thread.Sleep(10);
        }

        gesture.WaitForExit();
        return gesture.ExitCode == 0;
    }

    private static bool WriteParameters(string path, Carla host, string mix, out uint count, out bool mixChanged)
    {
        count = host.ParameterCount(0);
        mixChanged = false;
        var parameters = new List<(string Name, ParameterRanges Ranges)>();

        for (uint index = 0; index < count; index++)
        {
            var info = host.ParameterInfo(0, index);
            var ranges = host.ParameterRanges(0, index);

            if (info == null || ranges == null || !float.IsFinite(ranges->Min) || !float.IsFinite(ranges->Max)
                || !float.IsFinite(ranges->Default) || !float.IsFinite(ranges->Step)
                || !float.IsFinite(ranges->StepSmall) || !float.IsFinite(ranges->StepLarge))
            {
                return false;
            }

            parameters.Add((OneLine(Marshal.PtrToStringUTF8(info[0])), *ranges));
            var data = host.ParameterData(0, index);
            var (name, range) = parameters[^1];

            if (data != null && data->Type == Carla.ParameterInput && (data->Hints & Carla.ParameterIsReadOnly) == 0
                && mix.Length > 0 && name == mix && range.Min <= range.Max)
            {
                var wanted = range.Max;
                host.SetParameterValue(0, index, wanted);
                mixChanged = Math.Abs(host.CurrentParameterValue(0, index) - wanted) <= Math.Max(range.StepSmall, 0.0001f);
            }
        }

        var lines = new StringBuilder();

        for (uint index = 0; index < count; index++)
        {
            var current = host.CurrentParameterValue(0, index);

            if (!float.IsFinite(current))
            {
                return false;
            }

            var (name, ranges) = parameters[(int)index];
            lines.Append(CultureInfo.InvariantCulture, $"{index}\t{name}\t").AppendJoin('\t', new[]
            {
                current, ranges.Min, ranges.Max, ranges.Default, ranges.Step, ranges.StepSmall, ranges.StepLarge,
            }.Select(value => General(value))).Append('\n');
        }

        File.WriteAllText(path, lines.ToString());
        return true;
    }

    private static string OneLine(string? text) =>
        (text ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    private static void MakeInput(float[] left, float[] right)
    {
        const double frequency = 440.0;
        const double amplitude = 0.25;

        for (var frame = SilenceBefore; frame < SilenceBefore + SignalFrames; frame++)
        {
            var phase = 2.0 * Math.PI * frequency * (frame - SilenceBefore) / SampleRate;
            left[frame] = (float)(amplitude * Math.Sin(phase));
            right[frame] = (float)(amplitude * Math.Cos(phase));
        }
    }

    private static bool WriteWav(string path, float[] left, float[] right)
    {
        const uint frames = TotalFrames;
        const uint dataSize = frames * 8;

        using var file = new BinaryWriter(File.Create(path));
        file.Write("RIFF"u8);
        file.Write(dataSize + 36);
        file.Write("WAVEfmt "u8);
        file.Write(16u);
        file.Write((ushort)3);
        file.Write((ushort)2);
        file.Write(SampleRate);
        file.Write(SampleRate * 8);
        file.Write((ushort)8);
        file.Write((ushort)32);
        file.Write("data"u8);
        file.Write(dataSize);

        for (var index = 0; index < frames; index++)
        {
            file.Write(left[index]);
            file.Write(right[index]);
        }

        return true;
    }

    private static double Rms(float[] left, float[] right, uint begin, uint end)
    {
        var sum = 0.0;
        var compensation = 0.0;

        for (var frame = begin; frame < end; frame++)
        {
            var term = (double)left[frame] * left[frame] + (double)right[frame] * right[frame] - compensation;
            var next = sum + term;
            compensation = next - sum - term;
            sum = next;
        }

        return Math.Sqrt(sum / (2.0 * (end - begin)));
    }

    private static uint NoteEvents(int note, uint offset, MidiEvent* events)
    {
        if (note < 0)
        {
            return 0;
        }

        uint count = 0;
        const uint on = SilenceBefore;
        const uint off = SilenceBefore + SignalFrames;

        if (on >= offset && on < offset + BlockSize)
        {
            events[count++] = Event(on - offset, 0x90, (byte)note, 100);
        }

        if (off >= offset && off < offset + BlockSize)
        {
            events[count++] = Event(off - offset, 0x80, (byte)note, 0);
        }

        return count;
    }

    private static MidiEvent Event(uint at, byte status, byte note, byte velocity)
    {
        var made = new MidiEvent { Time = at, Port = 0, Size = 3 };
        made.Data[0] = status;
        made.Data[1] = note;
        made.Data[2] = velocity;
        return made;
    }

    private static string General(double value) =>
        value.ToString("G9", CultureInfo.InvariantCulture).Replace("E", "e", StringComparison.Ordinal);

    private static int Error(string phase, string detail)
    {
        Console.Error.WriteLine($"audio-render: {phase}: {detail}");
        Console.WriteLine($"AUDIO_RENDER=error phase={phase}");
        Libc.Leave(1);
        return 1;
    }

    private static void Reached(string phase) => Console.Error.WriteLine($"audio-render: {phase}");

    [UnmanagedCallersOnly]
    private static uint GetBufferSize(IntPtr handle) => BlockSize;

    [UnmanagedCallersOnly]
    private static double GetSampleRate(IntPtr handle) => SampleRate;

    [UnmanagedCallersOnly]
    private static byte IsOffline(IntPtr handle) => 1;

    [UnmanagedCallersOnly]
    private static TimeInfo* GetTimeInfo(IntPtr handle) => time;

    [UnmanagedCallersOnly]
    private static byte WriteMidiEvent(IntPtr handle, MidiEvent* midi) => 1;

    [UnmanagedCallersOnly]
    private static void ParameterChanged(IntPtr handle, uint index, float value)
    {
    }

    [UnmanagedCallersOnly]
    private static void MidiProgramChanged(IntPtr handle, byte channel, uint bank, uint program)
    {
    }

    [UnmanagedCallersOnly]
    private static void CustomDataChanged(IntPtr handle, IntPtr key, IntPtr value)
    {
    }

    [UnmanagedCallersOnly]
    private static void UiClosed(IntPtr handle)
    {
    }

    [UnmanagedCallersOnly]
    private static IntPtr FileDialog(IntPtr handle, byte isDir, IntPtr title, IntPtr filter) => IntPtr.Zero;

    [UnmanagedCallersOnly]
    private static nint Dispatcher(IntPtr handle, int opcode, int index, nint value, IntPtr ptr, float opt) => 0;
}
