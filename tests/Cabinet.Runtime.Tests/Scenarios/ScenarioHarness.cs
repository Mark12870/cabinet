using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Cabinet.Core;

namespace Cabinet.Runtime.Tests.Scenarios;

internal sealed class ScenarioHarness(string id, PluginKind kind) : IDisposable
{
    private static readonly TimeSpan InstallPatience = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ProbePatience = TimeSpan.FromMinutes(10);
    private static readonly Dictionary<string, (string Extension, string Carla)> Formats = new()
    {
        ["VST3"] = (".vst3", "vst3"),
        ["VST2"] = (".vst", "vst2"),
    };
    private readonly string root = Path.Combine(RuntimeTestEnvironment.TemporaryDirectory, "scenarios", id);
    private readonly string socket = RuntimeTestEnvironment.SocketDirectory;
    private Task<string>? audioProbe;

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
        Directory.CreateDirectory(socket);
    }

    public async Task Install(string download, Display display)
    {
        var result = await Run(
            "flatpak",
            [
                "run",
                "--nofilesystem=home",
                $"--filesystem={Home}:create",
                $"--filesystem={InstalledApp}:ro",
                $"--env=HOME={Home}",
                $"--env=XDG_RUNTIME_DIR={RuntimeTestEnvironment.RuntimeDirectory}",
                $"--env=FLATPAK_USER_DIR={RuntimeTestEnvironment.FlatpakUserDirectory}",
                Host.App,
                "library",
                "install",
                id,
            ],
            display,
            InstallPatience);

        File.WriteAllText(Path.Combine(Artefacts, "install.log"), result.Said);
        Assert.True(result.ExitCode == 0, result.Said);
        Assert.Contains($"Downloading {download}", result.Said, StringComparison.Ordinal);
    }

    public Bridge Plugin(string format, string file) => new(
        Path.Combine(ScanDir(Formats[format].Extension), file),
        Formats[format].Carla);

    public async Task<AudioMeasurement> Render(Bridge bridge, string mix, Display display)
    {
        var (plugin, format) = bridge;
        Require(plugin, "bridged plugin");
        var library = await (audioProbe ??= CompileAudioProbe());
        var binaries = Path.Combine(EditorProbe.CarlaPrefix(), "lib", "carla");
        var audio = Path.Combine(Artefacts, "audio", format);
        Directory.CreateDirectory(audio);
        var wrapper = EditorProbe.Wrapper(Path.Combine(root, "bin"), display, Home, socket);

        var launcher = Path.Combine(AppContext.BaseDirectory, "Probes", "audio-render.py");
        var result = await Run(
            "python3",
            [launcher, library, plugin, format, mix, audio, binaries],
            display,
            ProbePatience,
            info =>
            {
                var yabridge = Path.Combine(Host.Location(), "files", "lib", "yabridge");
                info.Environment["PATH"] = $"{wrapper}:{yabridge}:/usr/bin:/bin";
                info.Environment["WINELOADER"] = Path.Combine(yabridge, "cabinet-wine");
                info.Environment["YABRIDGE_TEMP_DIR"] = socket;
                info.Environment["YABRIDGE_NO_WATCHDOG"] = "1";
            });

        File.WriteAllText(Path.Combine(audio, "render.log"), result.Said);
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

    public void VerifyEditor(Bridge bridge)
    {
        var (plugin, format) = bridge;
        var shots = Path.Combine(Artefacts, "editor", format);
        Directory.CreateDirectory(shots);
        var result = EditorProbe.RunIn(
            Home, socket, Path.Combine(shots, "yabridge.log"), "editor-interaction.py", plugin, format, shots);
        File.WriteAllText(Path.Combine(shots, "probe.log"), result.Said);
        Assert.True(result.ExitCode == 0, result.Said);

        var found = Regex.Match(
            result.Said,
            @"EDITOR=(\S+) SIZE=(\S+) COLOURS=(\d+) REACTION=([0-9.]+) NOISE=([0-9.]+) "
            + @"IDLE_MS=(\d+) OPEN_MS=(\d+) CLOSE_MS=(\d+) REMOVE_MS=(\d+) SHUTDOWN_MS=(\d+) "
            + @"BLIND=(\d+) CLOSED=(yes|no) REOPEN=(yes|no) AIM=(\S+) MISS=(\d+)");
        Assert.True(found.Success, result.Said);

        var colours = int.Parse(found.Groups[3].Value, CultureInfo.InvariantCulture);
        var reaction = Number(found, 4);
        var noise = Number(found, 5);
        Assert.True(colours >= 16, $"the editor held only {colours} colours: {result.Said}");
        Assert.True(reaction >= 0.0005 && reaction > noise, $"the editor did not react: {result.Said}");
        Assert.Equal("0", found.Groups[11].Value);
        Assert.Equal("yes", found.Groups[13].Value);
        Assert.True(found.Groups[14].Value != "none", $"no control moved under the pointer: {result.Said}");
        Assert.True(found.Groups[15].Value == "0", $"the pointer landed away from what it aimed at: {result.Said}");
        Assert.DoesNotContain("crashed while being torn down", result.Said, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        Host.KillAll(Host.App, socket);
        Host.Discard(socket);
    }

    private string Data => Path.Combine(Home, ".var", "app", Host.App, "data");

    private string ScanDir(string extension)
    {
        var layout = new Layout(Home, RuntimeTestEnvironment.RuntimeDirectory);
        return kind == PluginKind.Windows ? layout.WindowsScanDir(extension) : layout.NativeScanDir(extension);
    }

    private async Task<string> CompileAudioProbe()
    {
        var output = Path.Combine(root, "audio-render.so");
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
        Action<ProcessStartInfo>? configure = null)
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

        configure?.Invoke(info);
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"could not start {file}");
        var output = process.StandardOutput.ReadToEndAsync();
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

    private static string InstalledApp =>
        Path.Combine(RuntimeTestEnvironment.FlatpakUserDirectory, "app", Host.App);

    private static void Require(string path, string description)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new FileNotFoundException($"{description} is missing", path);
        }
    }

    private static double Number(Match found, int group) =>
        double.Parse(found.Groups[group].Value, CultureInfo.InvariantCulture);
}

internal sealed record ScenarioProcessResult(int ExitCode, string Output, string Error)
{
    public string Said => $"{Output}\nstderr:\n{Error}\nexit: {ExitCode}";
}

internal sealed record Bridge(string Plugin, string Format);

internal sealed record AudioMeasurement(
    double Peak,
    double Before,
    double Tail,
    int Parameters,
    bool MixChanged);
