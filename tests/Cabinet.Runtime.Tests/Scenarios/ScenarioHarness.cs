using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Cabinet.Core;
using Cabinet.Core.Tests;

namespace Cabinet.Runtime.Tests.Scenarios;

internal sealed class ScenarioHarness(LibraryEntry entry) : IDisposable
{
    private static readonly TimeSpan InstallPatience = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ProbePatience = TimeSpan.FromMinutes(10);
    private static readonly Dictionary<string, (string Extension, string Carla)> Formats = new()
    {
        ["VST3"] = (".vst3", "vst3"),
        ["VST2"] = (".vst", "vst2"),
        ["LV2"] = (".lv2", "lv2"),
    };
    private readonly string root = Path.Combine(RuntimeTestEnvironment.TemporaryDirectory, "scenarios", entry.Id);
    private static readonly Lock Compiling = new();
    private static readonly HashSet<int> Slots = [];
    private int slot = -1;
    private string socket = "";
    private static Task<string>? audioProbe;

    public string Artefacts => Path.Combine(root, "artefacts");
    public string Home => Path.Combine(root, "home");

    public void Prepare()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(Artefacts);
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Path.Combine(Home, ".local", "share"));

        lock (Slots)
        {
            slot = Enumerable.Range(0, int.MaxValue).First(Slots.Add);
        }

        socket = Path.Combine(
            Path.GetDirectoryName(RuntimeTestEnvironment.SocketDirectory)!,
            slot.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(socket);
    }

    public async Task Install(string download, Display display)
    {
        var said = await Install(display);
        Assert.Contains($"Downloading {download}", said, StringComparison.Ordinal);
    }

    public Task InstallFrom(string installer, Display display) => Install(display, installer);

    private async Task<string> Install(Display display, params string[] installer)
    {
        var phases = new InstallPhases(entry.DemoUrl ?? entry.Url);
        var result = await Run(
            "flatpak", Cabinet("install", installer), display, InstallPatience, onLine: phases.Heard);
        foreach (var (phase, taken) in phases.Finish())
        {
            Time(phase, taken);
        }

        File.WriteAllText(Path.Combine(Artefacts, "install.log"), result.Said);
        Assert.True(result.ExitCode == 0, result.Said);
        return result.Said;
    }

    public void Time(string step, TimeSpan taken) =>
        File.AppendAllText(
            Path.Combine(Artefacts, "timing.txt"),
            string.Create(CultureInfo.InvariantCulture, $"{step} {taken.TotalSeconds:F1}\n"));

    public Manager Open(Display display)
    {
        var shots = Path.Combine(Artefacts, "manager");
        Directory.CreateDirectory(shots);
        var info = Prepared("flatpak", Cabinet("launch"), display);
        var launch = Process.Start(info) ?? throw new InvalidOperationException("could not launch the manager");
        return new Manager(
            launch,
            display,
            shots,
            () => Run("flatpak", Cabinet("stop"), display, ProbePatience));
    }

    public string Prefix => Path.Combine(Data, "prefixes", entry.Prefix);

    public async Task Set(Display display, params string[] arguments)
    {
        var result = await Run(
            "flatpak", [.. Sandboxed(), "set", entry.Prefix, .. arguments], display, ProbePatience);
        Assert.True(result.ExitCode == 0, result.Said);
    }

    public async Task SendText(Display display, string title, int x, int y, string text)
    {
        var executable = Path.Combine(Home, ".send-input.exe");
        if (!File.Exists(executable))
        {
            var compiled = await Run(
                "x86_64-w64-mingw32-gcc",
                [
                    "-O2",
                    "-municode",
                    "-mwindows",
                    "-static",
                    Repo.Path("tests/Cabinet.Runtime.Tests/Probes/send-input.c"),
                    "-lshell32",
                    "-o",
                    executable,
                ],
                display: null,
                ProbePatience);
            Assert.True(compiled.ExitCode == 0, compiled.Said);
        }

        var input = Path.Combine(Home, $".input-{Guid.NewGuid():N}");
        await File.WriteAllBytesAsync(input, Encoding.Unicode.GetBytes(text));
        try
        {
            var result = await Run(
                "flatpak",
                [
                    .. Sandboxed(),
                    "run",
                    entry.Prefix,
                    Windows(executable),
                    Windows(input),
                    title,
                    x.ToString(CultureInfo.InvariantCulture),
                    y.ToString(CultureInfo.InvariantCulture),
                ],
                display,
                ProbePatience);
            Assert.True(result.ExitCode == 0, result.Said);
        }
        finally
        {
            File.Delete(input);
        }
    }

    public void Restore(string registry)
    {
        var user = Path.Combine(Prefix, "user.reg");
        var restored = Sections(Encoding.UTF8.GetString(Convert.FromBase64String(registry)));
        var replaced = restored.Select(Key).ToHashSet(StringComparer.Ordinal);
        var kept = Sections(File.ReadAllText(user)).Where(section => !replaced.Contains(Key(section)));
        File.WriteAllText(user, string.Join("\n\n", [.. kept, .. restored]) + "\n");
    }

    private static List<string> Sections(string registry) =>
        [.. Regex.Split(registry, @"\n(?=\[)").Select(section => section.TrimEnd()).Where(section => section.Length > 0)];

    private static string Key(string section) => section.Split("] ", 2)[0];

    private static string Windows(string path) => @"Z:" + path.Replace('/', '\\');

    private List<string> Cabinet(string verb, params string[] arguments) =>
        [.. Sandboxed(), "library", verb, entry.Id, .. arguments];

    private List<string> Sandboxed() =>
    [
        "run",
        "--nofilesystem=home",
        $"--filesystem={Home}:create",
        $"--filesystem={InstalledApp}:ro",
        $"--env=HOME={Home}",
        $"--env=XDG_RUNTIME_DIR={RuntimeTestEnvironment.RuntimeDirectory}",
        $"--env=FLATPAK_USER_DIR={RuntimeTestEnvironment.FlatpakUserDirectory}",
        Host.App,
    ];

    public bool Holds(string format, string file)
    {
        var path = Path.Combine(ScanDir(Formats[format].Extension), file);
        return File.Exists(path) || Directory.Exists(path);
    }

    public Bridge Plugin(string format, string file) => format == "LV2"
        ? new(file, Formats[format].Carla)
        : new(Installed(Path.Combine(ScanDir(Formats[format].Extension), file)), Formats[format].Carla);

    public Task<AudioMeasurement> Render(Bridge bridge, string mix, Display display) =>
        Measure(bridge, mix, audio: true, note: -1, state: "", click: "", display);

    public Task<AudioMeasurement> Render(Bridge bridge, string mix, int note, Display display) =>
        Measure(bridge, mix, audio: true, note, state: "", click: "", display);

    public Task<AudioMeasurement> Play(
        Bridge bridge, int note, Display display, string state = "", string click = "") =>
        Measure(bridge, mix: "", audio: false, note, state, click, display);

    private async Task<AudioMeasurement> Measure(
        Bridge bridge, string mix, bool audio, int note, string state, string click, Display display)
    {
        var (plugin, format) = bridge;
        Task<string> compiled;
        lock (Compiling)
        {
            compiled = audioProbe ??= CompileAudioProbe();
        }

        var library = await compiled;
        var binaries = Path.Combine(EditorProbe.CarlaPrefix(), "lib", "carla");
        var output = Path.Combine(Artefacts, "audio", bridge.Label);
        Directory.CreateDirectory(output);
        var wrapper = EditorProbe.Wrapper(Path.Combine(root, "bin"), display, Home, socket);

        var launcher = Path.Combine(AppContext.BaseDirectory, "Probes", "audio-render.py");
        var result = await Run(
            "python3",
            [
                launcher,
                library,
                plugin,
                format,
                mix,
                output,
                binaries,
                audio ? "1" : "0",
                note.ToString(CultureInfo.InvariantCulture),
                state,
                click,
            ],
            display,
            ProbePatience,
            info =>
            {
                var yabridge = Path.Combine(Host.Location(), "files", "lib", "yabridge");
                info.Environment["PATH"] = $"{wrapper}:{yabridge}:/usr/bin:/bin";
                info.Environment["WINELOADER"] = Path.Combine(yabridge, "cabinet-wine");
                info.Environment["YABRIDGE_TEMP_DIR"] = socket;
                info.Environment["YABRIDGE_NO_WATCHDOG"] = "1";
                RuntimeTestEnvironment.OwnHome(info, Home);
            });

        File.WriteAllText(Path.Combine(output, "render.log"), result.Said);
        Assert.True(result.ExitCode == 0, result.Said);

        var found = Regex.Match(
            result.Output,
            @"AUDIO_RENDER=ok peak=([0-9.eE+-]+) pre_rms=([0-9.eE+-]+) signal_rms=[0-9.eE+-]+ "
            + @"tail_rms=([0-9.eE+-]+) frames=\d+ params=(\d+) mix_changed=([01])");
        Assert.True(found.Success, result.Said);

        return new AudioMeasurement(
            Number(found, 1),
            Number(found, 2),
            Number(found, 3),
            int.Parse(found.Groups[4].Value, CultureInfo.InvariantCulture),
            found.Groups[5].Value == "1");
    }

    public EditorOrigin VerifyEditor(
        Bridge bridge,
        bool controlsAreParameters = true,
        string click = "",
        string control = "",
        string press = "",
        string type = "",
        int still = 0)
    {
        var (plugin, format) = bridge;
        var shots = Path.Combine(Artefacts, "editor", bridge.Label);
        Directory.CreateDirectory(shots);
        var result = EditorProbe.RunIn(
            Home,
            socket,
            Path.Combine(shots, "yabridge.log"),
            "editor-interaction.py",
            new Dictionary<string, string>
            {
                ["CABINET_PROBE_TYPE"] = type,
                ["CABINET_PROBE_STILL"] = still.ToString(CultureInfo.InvariantCulture),
            },
            plugin,
            format,
            shots,
            click,
            controlsAreParameters ? "aim" : "no-aim",
            control,
            press);
        File.WriteAllText(Path.Combine(shots, "probe.log"), result.Said);
        Assert.True(result.ExitCode == 0, result.Said);

        var found = Regex.Match(
            result.Said,
            @"EDITOR=(\S+) SIZE=(\S+) COLOURS=(\d+) REACTION=([0-9.]+) NOISE=([0-9.]+) "
            + @"IDLE_MS=(\d+) OPEN_MS=(\d+) CLOSE_MS=(\d+) REMOVE_MS=(\d+) SHUTDOWN_MS=(\d+) "
            + @"BLIND=(\d+) CLOSED=(yes|no) REOPEN=(yes|no) AIM=(\S+) WINE=(\S+) TOLD=(\S+)");
        Assert.True(found.Success, result.Said);

        var colours = int.Parse(found.Groups[3].Value, CultureInfo.InvariantCulture);
        var reaction = Number(found, 4);
        var noise = Number(found, 5);
        Assert.True(colours >= 16, $"the editor held only {colours} colours: {result.Said}");
        Assert.True(reaction >= 0.0005 && reaction > noise, $"the editor did not react: {result.Said}");
        Assert.Equal("0", found.Groups[11].Value);
        Assert.Equal("yes", found.Groups[13].Value);
        Assert.True(
            !controlsAreParameters || found.Groups[14].Value != "none",
            $"no control moved under the pointer: {result.Said}");
        Assert.DoesNotContain("crashed while being torn down", result.Said, StringComparison.Ordinal);
        return new EditorOrigin(found.Groups[15].Value, found.Groups[16].Value);
    }

    public void Dispose()
    {
        if (slot < 0)
        {
            return;
        }

        Host.KillAll(Host.App, socket);
        Host.Discard(socket);

        lock (Slots)
        {
            Slots.Remove(slot);
        }

        slot = -1;
    }

    private string Data => Path.Combine(Home, ".var", "app", Host.App, "data");

    private string ScanDir(string extension)
    {
        var layout = new Layout(Home, RuntimeTestEnvironment.RuntimeDirectory);
        return entry.Kind == PluginKind.Windows ? layout.WindowsScanDir(extension) : layout.NativeScanDir(extension);
    }

    private async Task<string> CompileAudioProbe()
    {
        var output = Path.Combine(RuntimeTestEnvironment.TemporaryDirectory, "scenarios", "audio-render.so");
        var source = Path.Combine(AppContext.BaseDirectory, "Probes", "audio-render.cpp");
        var carla = EditorProbe.CarlaPrefix();
        var carlaSource = Path.Combine(
            RuntimeTestEnvironment.Home, ".var", "app", Host.App, "data", "carla-tests", "source");
        var library = Path.Combine(carla, "lib", "carla");

        Require(source, "audio probe source");
        Require(Path.Combine(carlaSource, "source", "includes", "CarlaNativePlugin.h"), "Carla headers");
        Require(Path.Combine(library, "libcarla_native-plugin.so"), "Carla native plugin library");

        var result = await Run(
            "c++",
            [
                "-std=c++17",
                "-O2",
                "-shared",
                "-fPIC",
                $"-I{Path.Combine(carlaSource, "source", "includes")}",
                $"-I{Path.Combine(carlaSource, "source", "backend")}",
                source,
                $"-L{library}",
                $"-Wl,-rpath,{library}",
                "-l:libcarla_standalone2.so",
                "-lcarla_native-plugin",
                "-o",
                output,
            ],
            display: null,
            ProbePatience);
        File.WriteAllText(Path.Combine(Artefacts, "compile.log"), result.Said);
        Assert.True(result.ExitCode == 0, result.Said);
        return output;
    }

    private async Task<ScenarioProcessResult> Run(
        string file,
        IReadOnlyList<string> arguments,
        Display? display,
        TimeSpan patience,
        Action<ProcessStartInfo>? configure = null,
        Action<string>? onLine = null)
    {
        var info = Prepared(file, arguments, display);
        configure?.Invoke(info);

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"could not start {file}");
        return await Finished(process, file, patience, onLine);
    }

    private ProcessStartInfo Prepared(string file, IReadOnlyList<string> arguments, Display? display)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        RuntimeTestEnvironment.Apply(info);
        info.Environment["HOME"] = Home;
        info.Environment["XDG_DATA_HOME"] = Path.Combine(Home, ".local", "share");
        info.Environment["XDG_CONFIG_HOME"] = Path.Combine(Home, ".config");
        info.Environment["XDG_CACHE_HOME"] = Path.Combine(Home, ".cache");
        if (display is not null)
        {
            info.Environment["DISPLAY"] = display.Name;
            info.Environment.Remove("WAYLAND_DISPLAY");
        }

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    private static async Task<ScenarioProcessResult> Finished(
        Process process, string file, TimeSpan patience, Action<string>? onLine)
    {
        var output = Read(process.StandardOutput, onLine);
        var error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(patience);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException($"{file} did not finish within {patience}");
        }

        return new ScenarioProcessResult(process.ExitCode, await output, await error);
    }

    private static async Task<string> Read(StreamReader reader, Action<string>? onLine)
    {
        if (onLine is null)
        {
            return await reader.ReadToEndAsync();
        }

        var all = new StringBuilder();
        while (await reader.ReadLineAsync() is { } line)
        {
            onLine(line);
            all.AppendLine(line);
        }

        return all.ToString();
    }

    private static string InstalledApp =>
        Path.Combine(RuntimeTestEnvironment.FlatpakUserDirectory, "app", Host.App);

    private static string Installed(string path)
    {
        Require(path, "installed plugin");
        return path;
    }

    private static void Require(string path, string description)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new FileNotFoundException($"{description} is missing: {path}", path);
        }
    }

    public Performance PlayThroughEditor(Bridge bridge, int note, string steps, string title)
    {
        var (plugin, format) = bridge;
        var shots = Path.Combine(Artefacts, "played", bridge.Label);
        Directory.CreateDirectory(shots);
        var result = EditorProbe.RunIn(
            Home,
            socket,
            Path.Combine(shots, "yabridge.log"),
            "editor-play.py",
            plugin,
            format,
            shots,
            note.ToString(CultureInfo.InvariantCulture),
            steps,
            title);
        File.WriteAllText(Path.Combine(shots, "probe.log"), result.Said);
        Assert.True(result.ExitCode == 0, result.Said);

        var found = Regex.Match(
            result.Said, @"PLAYED BEFORE=([0-9.]+) HELD=([0-9.]+) AFTER=([0-9.]+)");
        Assert.True(found.Success, result.Said);
        return new Performance(Number(found, 1), Number(found, 2), Number(found, 3));
    }

    private static double Number(Match found, int group) =>
        double.Parse(found.Groups[group].Value, CultureInfo.InvariantCulture);
}

internal sealed record ScenarioProcessResult(int ExitCode, string Output, string Error)
{
    public string Said => $"{Output}\nstderr:\n{Error}\nexit: {ExitCode}";
}

internal sealed record Bridge(string Plugin, string Format)
{
    public string Label { get; init; } = Format;
}

internal sealed record EditorOrigin(string Wine, string Told);

internal sealed record Performance(double Before, double Held, double After);

internal sealed record AudioMeasurement(
    double Peak,
    double Before,
    double Tail,
    int Parameters,
    bool MixChanged);
