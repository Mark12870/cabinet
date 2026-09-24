#include "CarlaNativePlugin.h"
#include "CarlaHost.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstdlib>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <string>
#include <vector>

namespace {

constexpr uint32_t sample_rate = 48000;
constexpr uint32_t block_size = 256;
constexpr uint32_t silence_before = sample_rate;
constexpr uint32_t signal_frames = sample_rate / 10;
constexpr uint32_t silence_after = sample_rate * 4;
constexpr uint32_t total_frames = silence_before + signal_frames + silence_after;
constexpr uint32_t rendered_frames = ((total_frames + block_size - 1) / block_size) * block_size;

struct HostState {
    NativeTimeInfo time{};
};

uint32_t get_buffer_size(NativeHostHandle)
{
    return block_size;
}

double get_sample_rate(NativeHostHandle)
{
    return sample_rate;
}

bool is_offline(NativeHostHandle)
{
    return true;
}

const NativeTimeInfo* get_time_info(NativeHostHandle handle)
{
    return &static_cast<HostState*>(handle)->time;
}

bool write_midi_event(NativeHostHandle, const NativeMidiEvent*)
{
    return true;
}

void parameter_changed(NativeHostHandle, uint32_t, float)
{
}

void midi_program_changed(NativeHostHandle, uint8_t, uint32_t, uint32_t)
{
}

void custom_data_changed(NativeHostHandle, const char*, const char*)
{
}

void ui_closed(NativeHostHandle)
{
}

const char* file_dialog(NativeHostHandle, bool, const char*, const char*)
{
    return nullptr;
}

intptr_t dispatcher(NativeHostHandle, NativeHostDispatcherOpcode, int32_t, intptr_t, void*, float)
{
    return 0;
}

struct RackGuard {
    const NativePluginDescriptor* descriptor;
    NativePluginHandle handle;
    bool active = false;

    ~RackGuard()
    {
        if (handle == nullptr)
            return;

        if (active && descriptor->deactivate != nullptr)
            descriptor->deactivate(handle);

        descriptor->cleanup(handle);
    }
};

struct HostGuard {
    CarlaHostHandle handle;

    ~HostGuard()
    {
        if (handle != nullptr)
            carla_host_handle_free(handle);
    }
};

struct ParameterSnapshot {
    std::string name;
    ParameterRanges ranges;
};

int error(const char* phase, const char* detail)
{
    std::fprintf(stderr, "audio-render: %s: %s\n", phase, detail);
    std::printf("AUDIO_RENDER=error phase=%s\n", phase);
    return 1;
}

void reached(const char* phase)
{
    std::fprintf(stderr, "audio-render: %s\n", phase);
    std::fflush(stderr);
}

std::string lower_ascii(const char* text)
{
    std::string result;
    if (text == nullptr)
        return result;

    for (const unsigned char character : std::string(text))
        result.push_back(character >= 'A' && character <= 'Z' ? static_cast<char>(character + 'a' - 'A')
                                                               : static_cast<char>(character));
    return result;
}

std::string one_line(const char* text)
{
    std::string result = text == nullptr ? "" : text;
    for (char& character : result)
    {
        if (character == '\t' || character == '\n' || character == '\r')
            character = ' ';
    }
    return result;
}

bool write_u16(std::ofstream& file, uint16_t value)
{
    file.put(static_cast<char>(value & 0xff));
    file.put(static_cast<char>((value >> 8) & 0xff));
    return file.good();
}

bool write_u32(std::ofstream& file, uint32_t value)
{
    for (unsigned int shift = 0; shift < 32; shift += 8)
        file.put(static_cast<char>((value >> shift) & 0xff));
    return file.good();
}

bool write_wav(const std::filesystem::path& path,
               const std::vector<float>& left,
               const std::vector<float>& right)
{
    if (left.size() != right.size() || left.size() > (UINT32_MAX - 44) / 8)
        return false;

    const uint32_t data_size = static_cast<uint32_t>(left.size() * 8);
    std::ofstream file(path, std::ios::binary);
    if (!file)
        return false;

    file.write("RIFF", 4);
    write_u32(file, data_size + 36);
    file.write("WAVEfmt ", 8);
    write_u32(file, 16);
    write_u16(file, 3);
    write_u16(file, 2);
    write_u32(file, sample_rate);
    write_u32(file, sample_rate * 8);
    write_u16(file, 8);
    write_u16(file, 32);
    file.write("data", 4);
    write_u32(file, data_size);

    for (size_t index = 0; index < left.size(); ++index)
    {
        file.write(reinterpret_cast<const char*>(&left[index]), sizeof(float));
        file.write(reinterpret_cast<const char*>(&right[index]), sizeof(float));
    }

    return file.good();
}

bool write_parameters(const std::filesystem::path& path,
                      CarlaHostHandle host,
                      uint32_t plugin,
                      uint32_t& count,
                      bool& mix_changed)
{
    count = carla_get_parameter_count(host, plugin);
    std::vector<ParameterSnapshot> parameters;
    parameters.reserve(count);

    for (uint32_t index = 0; index < count; ++index)
    {
        const CarlaParameterInfo* info = carla_get_parameter_info(host, plugin, index);
        const ParameterRanges* ranges = carla_get_parameter_ranges(host, plugin, index);
        if (info == nullptr || ranges == nullptr || !std::isfinite(ranges->min)
            || !std::isfinite(ranges->max) || !std::isfinite(ranges->def)
            || !std::isfinite(ranges->step) || !std::isfinite(ranges->stepSmall)
            || !std::isfinite(ranges->stepLarge))
            return false;

        parameters.push_back({one_line(info->name), *ranges});

        const ParameterData* data = carla_get_parameter_data(host, plugin, index);
        if (data != nullptr && data->type == CarlaBackend::PARAMETER_INPUT
            && (data->hints & CarlaBackend::PARAMETER_IS_READ_ONLY) == 0
            && lower_ascii(parameters.back().name.c_str()) == "mix"
            && parameters.back().ranges.min <= parameters.back().ranges.max)
        {
            const float wanted = parameters.back().ranges.max;
            carla_set_parameter_value(host, plugin, index, wanted);
            mix_changed = std::abs(carla_get_current_parameter_value(host, plugin, index) - wanted)
                <= std::max(parameters.back().ranges.stepSmall, 0.0001f);
        }
    }

    std::ofstream file(path);
    if (!file)
        return false;

    file << std::setprecision(9);
    for (uint32_t index = 0; index < count; ++index)
    {
        const float current = carla_get_current_parameter_value(host, plugin, index);
        if (!std::isfinite(current))
            return false;

        const ParameterSnapshot& parameter = parameters[index];
        file << index << '\t' << parameter.name << '\t' << current << '\t' << parameter.ranges.min << '\t'
             << parameter.ranges.max << '\t' << parameter.ranges.def << '\t' << parameter.ranges.step << '\t'
             << parameter.ranges.stepSmall << '\t' << parameter.ranges.stepLarge << '\n';
    }

    return file.good();
}

void make_input(std::vector<float>& left, std::vector<float>& right)
{
    constexpr double frequency = 440.0;
    constexpr double amplitude = 0.25;
    constexpr double pi = 3.141592653589793238462643383279502884;

    for (uint32_t frame = silence_before; frame < silence_before + signal_frames; ++frame)
    {
        const double phase = 2.0 * pi * frequency * (frame - silence_before) / sample_rate;
        left[frame] = static_cast<float>(amplitude * std::sin(phase));
        right[frame] = static_cast<float>(amplitude * std::cos(phase));
    }
}

double rms(const std::vector<float>& left, const std::vector<float>& right, uint32_t begin, uint32_t end)
{
    long double sum = 0.0;
    for (uint32_t frame = begin; frame < end; ++frame)
        sum += static_cast<long double>(left[frame]) * left[frame]
             + static_cast<long double>(right[frame]) * right[frame];
    return std::sqrt(static_cast<double>(sum / (2.0L * (end - begin))));
}

int render(const char* plugin_path, const char* format, const char* artefact_dir, const char* binaries_dir)
{
    const CarlaBackend::PluginType plugin_type = std::string(format) == "vst2"
        ? CarlaBackend::PLUGIN_VST2
        : std::string(format) == "vst3" ? CarlaBackend::PLUGIN_VST3 : CarlaBackend::PLUGIN_NONE;
    if (plugin_type == CarlaBackend::PLUGIN_NONE)
        return error("setup", "unsupported plugin format");

    std::error_code filesystem_error;
    const std::filesystem::path plugin(plugin_path);
    const std::filesystem::path binaries(binaries_dir);
    const std::filesystem::path artefacts(artefact_dir);
    std::filesystem::create_directories(artefacts, filesystem_error);
    if (filesystem_error || !std::filesystem::is_directory(artefacts)
        || !std::filesystem::exists(plugin) || !std::filesystem::is_directory(binaries))
        return error("setup", "invalid plugin, artefact, or Carla binaries path");

    HostState state;
    NativeHostDescriptor native_host{
        &state,
        binaries.c_str(),
        "cabinet-audio-render",
        0,
        get_buffer_size,
        get_sample_rate,
        is_offline,
        get_time_info,
        write_midi_event,
        parameter_changed,
        midi_program_changed,
        custom_data_changed,
        ui_closed,
        file_dialog,
        file_dialog,
        dispatcher,
    };

    const NativePluginDescriptor* descriptor = carla_get_native_rack_plugin();
    if (descriptor == nullptr || descriptor->instantiate == nullptr || descriptor->cleanup == nullptr
        || descriptor->activate == nullptr || descriptor->deactivate == nullptr || descriptor->process == nullptr)
        return error("setup", "Carla rack descriptor is incomplete");

    RackGuard rack{descriptor, descriptor->instantiate(&native_host)};
    if (rack.handle == nullptr)
        return error("setup", "Carla rack could not be instantiated");

    HostGuard host{carla_create_native_plugin_host_handle(descriptor, rack.handle)};
    if (host.handle == nullptr)
        return error("setup", "Carla host handle could not be created");

    carla_set_engine_option(host.handle, CarlaBackend::ENGINE_OPTION_PATH_BINARIES, 0, binaries.c_str());
    reached("add_plugin enter");
    const bool added = carla_add_plugin(host.handle, CarlaBackend::BINARY_POSIX64, plugin_type, plugin_path,
                                        "audio-render", nullptr, 0, nullptr, 0);
    reached("add_plugin leave");
    if (!added)
        return error("load", carla_get_last_error(host.handle));

    if (carla_get_current_plugin_count(host.handle) != 1
        || carla_get_plugin_info(host.handle, 0) == nullptr)
        return error("load", "Carla did not retain the plugin");

    uint32_t parameter_count = 0;
    bool mix_changed = false;
    reached("parameters enter");
    if (!write_parameters(artefacts / "parameters.txt", host.handle, 0, parameter_count, mix_changed))
        return error("setup", "could not report finite plugin parameters");
    reached("parameters leave");

    const std::filesystem::path input_path = artefacts / "input.wav";
    const std::filesystem::path output_path = artefacts / "output.wav";
    std::vector<float> input_left(rendered_frames, 0.0f);
    std::vector<float> input_right(rendered_frames, 0.0f);
    std::vector<float> output_left(rendered_frames, 0.0f);
    std::vector<float> output_right(rendered_frames, 0.0f);
    make_input(input_left, input_right);

    input_left.resize(total_frames);
    input_right.resize(total_frames);
    if (!write_wav(input_path, input_left, input_right))
        return error("artefact", "could not write input.wav");
    input_left.resize(rendered_frames, 0.0f);
    input_right.resize(rendered_frames, 0.0f);

    carla_set_active(host.handle, 0, true);
    reached("child active");
    descriptor->activate(rack.handle);
    rack.active = true;
    reached("rack active");

    try
    {
        for (uint32_t offset = 0; offset < rendered_frames; offset += block_size)
        {
            float* input[] = {input_left.data() + offset, input_right.data() + offset};
            float* output[] = {output_left.data() + offset, output_right.data() + offset};
            state.time.playing = true;
            state.time.frame = offset;
            state.time.usecs = static_cast<uint64_t>(static_cast<double>(offset) * 1000000.0 / sample_rate);
            if (offset == 0)
                reached("process enter");
            descriptor->process(rack.handle, input, output, block_size, nullptr, 0);
            if (offset == 0)
                reached("process leave");
        }
    }
    catch (...)
    {
        return error("process", "Carla raised while rendering");
    }

    size_t nonfinite = 0;
    double peak = 0.0;
    for (uint32_t frame = 0; frame < total_frames; ++frame)
    {
        if (!std::isfinite(output_left[frame]) || !std::isfinite(output_right[frame]))
        {
            nonfinite += 2;
            continue;
        }

        peak = std::max(peak, static_cast<double>(std::abs(output_left[frame])));
        peak = std::max(peak, static_cast<double>(std::abs(output_right[frame])));
    }

    if (nonfinite != 0)
        return error("nonfinite", "plugin output was not finite");

    output_left.resize(total_frames);
    output_right.resize(total_frames);
    if (!write_wav(output_path, output_left, output_right))
        return error("artefact", "could not write output.wav");

    carla_set_active(host.handle, 0, false);
    if (!carla_remove_all_plugins(host.handle))
        return error("cleanup", carla_get_last_error(host.handle));
    float settling_left[block_size]{};
    float settling_right[block_size]{};
    float* settling_input[] = {settling_left, settling_right};
    float* settling_output[] = {settling_left, settling_right};
    for (uint32_t block = 0; block < 16; ++block)
    {
        descriptor->process(rack.handle, settling_input, settling_output, block_size, nullptr, 0);
    }
    descriptor->deactivate(rack.handle);
    rack.active = false;

    const uint32_t tail_begin = silence_before + signal_frames + sample_rate / 5;
    const uint32_t tail_end = silence_before + signal_frames + sample_rate * 2;
    std::printf("AUDIO_RENDER=ok peak=%.9g pre_rms=%.9g signal_rms=%.9g tail_rms=%.9g "
                "frames=%u params=%u mix_changed=%u\n",
                peak,
                rms(output_left, output_right, 0, silence_before),
                rms(output_left, output_right, silence_before, silence_before + signal_frames),
                rms(output_left, output_right, tail_begin, tail_end),
                total_frames,
                parameter_count,
                mix_changed ? 1 : 0);
    std::fflush(stdout);
    std::_Exit(0);
}

}

extern "C" int audio_render(const char* plugin, const char* format, const char* artefacts, const char* binaries)
{
    try
    {
        return render(plugin, format, artefacts, binaries);
    }
    catch (...)
    {
        return error("setup", "unexpected exception");
    }
}
