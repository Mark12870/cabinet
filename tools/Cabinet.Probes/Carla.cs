using System.Runtime.InteropServices;

namespace Cabinet.Probes;

internal sealed unsafe class Carla
{
    public const int BinaryNative = 2;
    public const int PluginLv2 = 4;
    public const int PluginVst2 = 5;
    public const int PluginVst3 = 6;
    public const int PluginClap = 14;
    public const int ParameterInput = 1;
    public const uint ParameterIsEnabled = 0x10;
    public const uint ParameterIsReadOnly = 0x40;
    public const uint OptionUseChunks = 0x8;

    private const int OptionProcessMode = 1;
    private const int OptionTransportMode = 2;
    private const int OptionPathBinaries = 20;
    private const int ProcessModeContinuousRack = 2;
    private const int TransportModeInternal = 1;

    private readonly IntPtr[] libraries;

    public Carla(IntPtr handle, params IntPtr[] libraries)
    {
        Handle = handle;
        this.libraries = libraries;
    }

    public IntPtr Handle { get; }

    public static Carla Standalone(string prefix)
    {
        var library = NativeLibrary.Load(Library(prefix, "libcarla_standalone2.so"));
        var init = (delegate* unmanaged<IntPtr>)NativeLibrary.GetExport(library, "carla_standalone_host_init");
        return new Carla(init(), library);
    }

    public static string Library(string prefix, string name) => Path.Combine(prefix, "lib", "carla", name);

    public IntPtr Export(string name)
    {
        foreach (var library in libraries)
        {
            if (NativeLibrary.TryGetExport(library, name, out var found))
            {
                return found;
            }
        }

        throw new EntryPointNotFoundException(name);
    }

    public void Rack(string prefix)
    {
        SetEngineOption(OptionProcessMode, ProcessModeContinuousRack, "");
        SetEngineOption(OptionTransportMode, TransportModeInternal, "");
        Binaries(Path.Combine(prefix, "lib", "carla"));
    }

    public void Binaries(string directory) => SetEngineOption(OptionPathBinaries, 0, directory);

    public void SetEngineOption(int option, int value, string text)
    {
        using var utf8 = new Utf8(text);
        ((delegate* unmanaged<IntPtr, int, int, IntPtr, void>)Export("carla_set_engine_option"))(
            Handle, option, value, utf8.Pointer);
    }

    public bool EngineInit(string driver, string client)
    {
        using var name = new Utf8(driver);
        using var title = new Utf8(client);
        return ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte>)Export("carla_engine_init"))(
            Handle, name.Pointer, title.Pointer) != 0;
    }

    public bool EngineClose() => ((delegate* unmanaged<IntPtr, byte>)Export("carla_engine_close"))(Handle) != 0;

    public void EngineIdle() => ((delegate* unmanaged<IntPtr, void>)Export("carla_engine_idle"))(Handle);

    public string LastError() =>
        Marshal.PtrToStringUTF8(((delegate* unmanaged<IntPtr, IntPtr>)Export("carla_get_last_error"))(Handle)) ?? "";

    public bool AddPlugin(int binary, int type, string? filename, string? name, string? label)
    {
        using var file = new Utf8(filename);
        using var called = new Utf8(name);
        using var labelled = new Utf8(label);
        return ((delegate* unmanaged<IntPtr, int, int, IntPtr, IntPtr, IntPtr, long, IntPtr, uint, byte>)
            Export("carla_add_plugin"))(
            Handle, binary, type, file.Pointer, called.Pointer, labelled.Pointer, 0, IntPtr.Zero, 0) != 0;
    }

    public bool RemovePlugin(uint plugin) =>
        ((delegate* unmanaged<IntPtr, uint, byte>)Export("carla_remove_plugin"))(Handle, plugin) != 0;

    public bool RemoveAllPlugins() =>
        ((delegate* unmanaged<IntPtr, byte>)Export("carla_remove_all_plugins"))(Handle) != 0;

    public uint CurrentPluginCount() =>
        ((delegate* unmanaged<IntPtr, uint>)Export("carla_get_current_plugin_count"))(Handle);

    public IntPtr PluginInfo(uint plugin) =>
        ((delegate* unmanaged<IntPtr, uint, IntPtr>)Export("carla_get_plugin_info"))(Handle, plugin);

    public uint OptionsAvailable(uint plugin) => ((uint*)PluginInfo(plugin))[3];

    public void ShowCustomUi(uint plugin, bool show) =>
        ((delegate* unmanaged<IntPtr, uint, byte, void>)Export("carla_show_custom_ui"))(Handle, plugin, show ? (byte)1 : (byte)0);

    public void SetActive(uint plugin, bool active) =>
        ((delegate* unmanaged<IntPtr, uint, byte, void>)Export("carla_set_active"))(Handle, plugin, active ? (byte)1 : (byte)0);

    public void SetOption(uint plugin, uint option, bool enabled) =>
        ((delegate* unmanaged<IntPtr, uint, uint, byte, void>)Export("carla_set_option"))(
            Handle, plugin, option, enabled ? (byte)1 : (byte)0);

    public bool SavePluginState(uint plugin, string path)
    {
        using var file = new Utf8(path);
        return ((delegate* unmanaged<IntPtr, uint, IntPtr, byte>)Export("carla_save_plugin_state"))(
            Handle, plugin, file.Pointer) != 0;
    }

    public bool LoadPluginState(uint plugin, string path)
    {
        using var file = new Utf8(path);
        return ((delegate* unmanaged<IntPtr, uint, IntPtr, byte>)Export("carla_load_plugin_state"))(
            Handle, plugin, file.Pointer) != 0;
    }

    public uint ParameterCount(uint plugin) =>
        ((delegate* unmanaged<IntPtr, uint, uint>)Export("carla_get_parameter_count"))(Handle, plugin);

    public ParameterData* ParameterData(uint plugin, uint index) =>
        ((delegate* unmanaged<IntPtr, uint, uint, ParameterData*>)Export("carla_get_parameter_data"))(Handle, plugin, index);

    public ParameterRanges* ParameterRanges(uint plugin, uint index) =>
        ((delegate* unmanaged<IntPtr, uint, uint, ParameterRanges*>)Export("carla_get_parameter_ranges"))(
            Handle, plugin, index);

    public IntPtr* ParameterInfo(uint plugin, uint index) =>
        ((delegate* unmanaged<IntPtr, uint, uint, IntPtr*>)Export("carla_get_parameter_info"))(Handle, plugin, index);

    public float CurrentParameterValue(uint plugin, uint index) =>
        ((delegate* unmanaged<IntPtr, uint, uint, float>)Export("carla_get_current_parameter_value"))(
            Handle, plugin, index);

    public void SetParameterValue(uint plugin, uint index, float value) =>
        ((delegate* unmanaged<IntPtr, uint, uint, float, void>)Export("carla_set_parameter_value"))(
            Handle, plugin, index, value);

    public void SendMidiNote(uint plugin, byte channel, byte note, byte velocity) =>
        ((delegate* unmanaged<IntPtr, uint, byte, byte, byte, void>)Export("carla_send_midi_note"))(
            Handle, plugin, channel, note, velocity);

    public float OutputPeak(uint plugin, bool left) =>
        ((delegate* unmanaged<IntPtr, uint, byte, float>)Export("carla_get_output_peak_value"))(
            Handle, plugin, left ? (byte)1 : (byte)0);

    public static int Type(string format) => format switch
    {
        "vst2" => PluginVst2,
        "vst3" => PluginVst3,
        "clap" => PluginClap,
        "lv2" => PluginLv2,
        _ => throw new ArgumentException($"no Carla plugin type for {format}"),
    };

    private readonly struct Utf8(string? text) : IDisposable
    {
        public IntPtr Pointer { get; } = text is null ? IntPtr.Zero : Marshal.StringToCoTaskMemUTF8(text);

        public void Dispose() => Marshal.FreeCoTaskMem(Pointer);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct ParameterData
{
    public int Type;
    public uint Hints;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ParameterRanges
{
    public float Default;
    public float Min;
    public float Max;
    public float Step;
    public float StepSmall;
    public float StepLarge;
}
