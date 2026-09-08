using System.Diagnostics;

namespace Cabinet.Runtime.Tests;

public sealed class CarlaTests : IDisposable
{
    private readonly RuntimeTestLock runtimeLock = RuntimeTestLock.Acquire();
    private static readonly string Home = RuntimeTestEnvironment.Home;

    public static IEnumerable<object[]> PluginCases()
    {
        object[][] cases =
        [
            [new PluginCase("linux-vst2", "nv2", "vst2", HomePath(".vst", "DecentSampler.so"), true,
                "CARLA_PLUGIN_LOADED=ok")],
            [new PluginCase("linux-vst3", "nv3", "vst3", HomePath(".vst3", "Surge XT.vst3"), true,
                "CARLA_PLUGIN_LOADED=ok")],
            [new PluginCase("linux-clap", "ncp", "clap", HomePath(".clap", "Surge XT.clap"), true,
                "CARLA_PLUGIN_LOADED=ok")],
            [new PluginCase("linux-lv2", "nl2", "lv2", "https://surge-synthesizer.github.io/lv2/surge-xt", true,
                "CARLA_PLUGIN_LOADED=ok", HomePath(".lv2", "Surge XT.lv2"))],
            [new PluginCase("windows-vst2-sitala", "wsi", "vst2", HomePath(".vst", "yabridge", "Sitala.so"), false,
                "Finished initializing")],
            [new PluginCase("windows-vst2-valhalla", "wv2", "vst2",
                HomePath(".vst", "yabridge", "ValhallaSupermassive_x64.so"), false, "Finished initializing")],
            [new PluginCase("windows-vst3-valhalla", "wv3", "vst3",
                HomePath(".vst3", "yabridge", "ValhallaSupermassive.vst3"), false, "Finished initializing")],
            [new PluginCase("windows-vst3-sine", "wsine", "vst3",
                HomePath(".vst3", "yabridge", "SINE Player.vst3"), false, "Finished initializing")],
            [new PluginCase("windows-clap-surge", "wsc", "clap", HomePath(".clap", "yabridge", "Surge XT.clap"), true,
                "Finished initializing")],
        ];

        return cases;
    }

    [Theory]
    [MemberData(nameof(PluginCases))]
    public async Task LoadsThroughCarlaNoGui(PluginCase plugin)
    {
        var configuration = RuntimeConfiguration.Create(plugin);
        var result = await CarlaProcess.Run(configuration, plugin);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("CARLA_CLEANUP=ok", result.Output);

        foreach (var error in CarlaProcess.ErrorPatterns)
        {
            Assert.DoesNotContain(error, result.Output, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(plugin.Success, result.Output);
    }

    private static string HomePath(params string[] parts) => Path.Combine([Home, .. parts]);

    public void Dispose() => runtimeLock.Dispose();
}

public sealed record PluginCase(
        string Name,
        string RunId,
        string Format,
    string Plugin,
    bool Testing,
    string Success,
    string? Fixture = null)
{
    public string FixturePath => Fixture ?? Plugin;
}

internal sealed record RuntimeConfiguration(string Carla, string CabinetFiles, string Backend, string Toolbox)
{
    public static RuntimeConfiguration Create(PluginCase plugin)
    {
        var carla = Path.Combine(RuntimeTestEnvironment.Home,
            ".var", "app", "io.github.mark12870.cabinet", "data", "carla-tests", "prefix", "bin",
            "carla");

        RequireFile(carla, "Carla");
        RequireFile(plugin.FixturePath, $"{plugin.Name} fixture");

        var location = FlatpakLocation();
        var cabinetFiles = Path.Combine(location, "files");
        RequireFile(Path.Combine(cabinetFiles, "lib", "yabridge", "cabinet-wine"), "Cabinet's wine shim");

        return new RuntimeConfiguration(
            carla, cabinetFiles, RuntimeTestEnvironment.Backend, RuntimeTestEnvironment.Toolbox);
    }

    private static string FlatpakLocation() => Host.Location();

    private static void RequireFile(string path, string name)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new FileNotFoundException($"{name} is missing", path);
        }
    }
}

internal sealed record ProcessResult(int ExitCode, string Output, string Error);

internal sealed record CarlaResult(int ExitCode, string Output);

internal static class CarlaProcess
{
    public static readonly string[] ErrorPatterns =
    [
        "Plugin failed",
        "Could not load plugin",
        "Wine host process has exited",
        "Connection reset",
        "Carla assertion failure",
        "Engine failed to initialize",
        "Failed to load selected project",
        "Carla no-gui mode",
        "Failed to load plugin",
        "X Error of failed request",
        "terminate called",
        "std::system_error",
        "stack overflow",
        "division by zero",
    ];

    private const string Supervisor = """
        #!/usr/bin/env bash
        set -u

        run_id=$1
        carla=$2
        format=$3
        plugin=$4
        testing=$5
        timeout_duration=$6
        cabinet_files=$7

        state_root=${CABINET_RUNTIME_ROOT:?}/q
        socket_root=${CABINET_RUNTIME_SOCKET_DIRECTORY:?}
        socket_runtime=${socket_root%/yabridge/c}
        state=$state_root/$run_id
        log=$state/carla.log
        report_log=$state/report.log
        project=$state/project.carxp
        instances=$state/flatpak-instances
        process_group=$state/process-group
        carla_pid=
        group_id=
        result=0
        ready=0
        cleanup_failed=0
        deadline_pid=

        umask 077

        process_alive() {
            kill -0 "$1" 2>/dev/null || return 1
            case "$(ps -o stat= -p "$1" 2>/dev/null)" in
                Z*) return 1 ;;
            esac
        }

        if [ -f "$state/supervisor.pid" ]; then
            read -r stale_pid < "$state/supervisor.pid" || stale_pid=
            if [ -n "$stale_pid" ] && [ "$stale_pid" != "$$" ] && process_alive "$stale_pid"; then
                stale_command=$(tr '\0' ' ' < "/proc/$stale_pid/cmdline" 2>/dev/null || true)
                case "$stale_command" in
                    *"$run_id"*)
                        kill -TERM "$stale_pid" 2>/dev/null || true
                        sleep 1
                        process_alive "$stale_pid" && kill -KILL "$stale_pid" 2>/dev/null || true
                        ;;
                esac
            fi
        fi

        rm -rf "$state"
        mkdir -p "$state"
        printf '%s\n' "$$" > "$state/supervisor.pid"
        mkdir -p "$socket_root"
        exec 9>"$instances"

        cleanup() {
            trap - EXIT TERM INT HUP
            set +e

            if [ -f "$process_group" ]; then
                read -r group_id < "$process_group"
            fi

            if [ -n "$group_id" ] && [ "$group_id" -gt 1 ]; then
                kill -TERM -- "-$group_id" 2>/dev/null
                sleep 1
                kill -KILL -- "-$group_id" 2>/dev/null
            fi

            if [ -n "$carla_pid" ] && process_alive "$carla_pid"; then
                kill -TERM "$carla_pid" 2>/dev/null
                sleep 1
                kill -KILL "$carla_pid" 2>/dev/null
                wait "$carla_pid" 2>/dev/null
            fi

            if [ -n "${deadline_pid:-}" ] && process_alive "$deadline_pid"; then
                kill "$deadline_pid" 2>/dev/null
                wait "$deadline_pid" 2>/dev/null
            fi

            exec 9>&-
            while IFS= read -r instance || [ -n "$instance" ]; do
                [ -n "$instance" ] || continue
                if ! timeout --kill-after=2s 5s flatpak kill "$instance" >/dev/null 2>&1; then
                    timeout --kill-after=2s 5s flatpak ps --columns=instance 2>/dev/null |
                        grep -Fxq "$instance" && cleanup_failed=1
                fi
            done < "$instances"

            printf 'CARLA_CLEANUP=%s\n' "$([ "$cleanup_failed" -eq 0 ] && printf ok || printf failed)"
            if [ -f "$report_log" ]; then
                cat "$report_log"
            elif [ -f "$log" ]; then
                cat "$log"
            fi
            for endpoint in "$socket_root"/cabinet-* "$socket_root"/yabridge-*; do
                [ -e "$endpoint" ] || continue
                rm -rf -- "$endpoint"
            done
            rm -rf "$state"
            [ "$cleanup_failed" -eq 0 ] || result=1
            exit "$result"
        }

        trap cleanup EXIT
        trap 'result=143; exit 143' TERM INT HUP

        export YABRIDGE_TEMP_DIR=$socket_root
        export YABRIDGE_NO_WATCHDOG=1
        export CARLA_BRIDGE_DUMMY=1
        export CABINET_RUNTIME_RUN_ID=$run_id
        export FONTCONFIG_FILE=/etc/fonts/fonts.conf
        export PYTHONUNBUFFERED=1
        printf '%s\n' "$CABINET_RUNTIME_ROOT" > "$state/runtime-root"
        printf '%s\n' "$FLATPAK_USER_DIR" > "$state/flatpak-user-dir"
        printf '%s\n' "$HOME" > "$state/home"
        printf '%s\n' "$XDG_RUNTIME_DIR" > "$state/runtime-dir"
        mkdir -p "$YABRIDGE_TEMP_DIR"

        xml_escape() {
            local value=$1
            value=${value//&/\&amp;}
            value=${value//</\&lt;}
            value=${value//>/\&gt;}
            printf '%s' "$value"
        }

        xml_run_id=$(xml_escape "$run_id")
        xml_plugin=$(xml_escape "$plugin")

        mkdir -p "$HOME/.config/falkTX"
        cat > "$HOME/.config/falkTX/Carla2.conf" <<'SETTINGS'
        [Engine]
        AudioDriver=Dummy
        ProcessMode=2
        TransportMode=1
        ManageUIs=false
        PreferPluginBridges=false
        PreferUiBridges=false
        UIBridgesTimeout=4000
        SETTINGS

        case "$format" in
            vst2) project_type=VST2 ;;
            vst3) project_type=VST3 ;;
            clap) project_type=CLAP ;;
            lv2) project_type=LV2 ;;
            *) printf 'unsupported Carla format: %s\n' "$format" >&2; exit 2 ;;
        esac

        if [ "$format" = lv2 ]; then
            plugin_info="           <Type>LV2</Type>
           <Name>$xml_run_id</Name>
           <URI>$xml_plugin</URI>"
        else
            plugin_info="           <Type>$project_type</Type>
           <Name>$xml_run_id</Name>
           <Binary>$xml_plugin</Binary>"
        fi

        cat > "$project" <<PROJECT
        <?xml version='1.0' encoding='UTF-8'?>
        <!DOCTYPE CARLA-PROJECT>
        <CARLA-PROJECT VERSION='2.0'>
         <EngineSettings>
          <ForceStereo>false</ForceStereo>
          <PreferPluginBridges>false</PreferPluginBridges>
          <PreferUiBridges>false</PreferUiBridges>
          <UIsAlwaysOnTop>false</UIsAlwaysOnTop>
          <MaxParameters>200</MaxParameters>
          <UIBridgesTimeout>4000</UIBridgesTimeout>
         </EngineSettings>
         <Plugin>
          <Info>
        $plugin_info
          </Info>
          <Data>
           <Active>Yes</Active>
           <ControlChannel>1</ControlChannel>
           <Options>0x0</Options>
          </Data>
         </Plugin>
        </CARLA-PROJECT>
        PROJECT

        cat > "$state/flatpak" <<'WRAPPER'
        #!/usr/bin/env bash
        if [ "${1:-}" != run ]; then
            exec "$CABINET_RUNTIME_FLATPAK" "$@"
        fi

        shift
        filtered=()
        for argument in "$@"; do
            case "$argument" in
                --env=DISPLAY=*|--env=XAUTHORITY=*|--env=WAYLAND_DISPLAY=*) ;;
                *) filtered+=("$argument") ;;
            esac
        done
        runtime_root=${CABINET_RUNTIME_ROOT:?}
        state=$runtime_root/q/$CABINET_RUNTIME_RUN_ID
        instances=$state/flatpak-instances
        exec 9>>"$instances"
        flatpak_user_dir=$runtime_root/home/.local/share/flatpak
        home=$runtime_root/home
        runtime_dir=$socket_runtime
        exec env \
            HOME="$home" \
            XDG_RUNTIME_DIR="$runtime_dir" \
            FLATPAK_USER_DIR="$flatpak_user_dir" \
            FLATPAK_SYSTEM_DIRS=/var/lib/flatpak \
            "$CABINET_RUNTIME_FLATPAK" run \
            --die-with-parent \
            --instance-id-fd=9 \
            --nofilesystem=home \
            --filesystem="$runtime_root":create \
            --filesystem="$CABINET_RUNTIME_SOCKET_DIRECTORY":create \
            --env=HOME="$home" \
            --env=XDG_RUNTIME_DIR="$runtime_dir" \
            --env=CABINET_RUNTIME_RUN_ID="$CABINET_RUNTIME_RUN_ID" \
            --env=FLATPAK_USER_DIR="$flatpak_user_dir" \
            --env=DISPLAY= \
            --env=XAUTHORITY= \
            --env=WAYLAND_DISPLAY= \
            "${filtered[@]}"
        WRAPPER
        chmod +x "$state/flatpak"
        cat > "$state/wine-loader" <<WRAPPER
        #!/usr/bin/env bash
        export PATH="$state:\$PATH"
        exec "$cabinet_files/lib/yabridge/cabinet-wine" "\$@"
        WRAPPER
        chmod +x "$state/wine-loader"
        export CABINET_RUNTIME_FLATPAK=$(command -v flatpak)
        export PATH=$state:$cabinet_files/lib/yabridge:$PATH
        export WINELOADER=$state/wine-loader

        if [ "$testing" = 1 ]; then
            export CARLA_BRIDGE_TESTING=1
        fi

        setsid --wait bash -c 'group_id=$(ps -o pgid= -p "$$"); group_id=${group_id//[[:space:]]/}; printf "%s\\n" "$group_id" > "$1"; shift; exec stdbuf -oL -eL "$@"' carla "$process_group" "$carla" --no-gui "$project" >"$log" 2>&1 &
        carla_pid=$!
        deadline=$state/deadline
        ( sleep "$timeout_duration"; : > "$deadline" ) &
        deadline_pid=$!

        while process_alive "$carla_pid"; do
            if grep -Eq 'Plugin failed|Could not load plugin|Wine host process has exited|Connection reset|Carla assertion failure|Engine failed to initialize|Failed to load selected project|Carla no-gui mode|Failed to load plugin|X Error of failed request|terminate called|std::system_error|stack overflow|division by zero' "$log" 2>/dev/null; then
                result=1
                break
            fi
            if grep -Fxq 'Carla ready!' "$log" 2>/dev/null; then
                ready=1
                result=0
                cp "$log" "$report_log"
                break
            fi
            if [ -e "$deadline" ]; then
                result=1
                cp "$log" "$report_log"
                break
            fi
            sleep 0.25
        done

        kill "$deadline_pid" 2>/dev/null
        wait "$deadline_pid" 2>/dev/null

        if process_alive "$carla_pid"; then
            kill -TERM -- "-$carla_pid" 2>/dev/null || kill -TERM "$carla_pid" 2>/dev/null
            sleep 1
            kill -KILL -- "-$carla_pid" 2>/dev/null || kill -KILL "$carla_pid" 2>/dev/null
            wait "$carla_pid" 2>/dev/null
        else
            wait "$carla_pid"
            status=$?
            if grep -Fxq 'Carla ready!' "$log" 2>/dev/null; then
                ready=1
            fi
            if [ "$result" -eq 0 ] && { [ "$ready" -eq 0 ] || [ "$status" -ne 0 ]; }; then
                result=1
            fi
        fi

        if [ "$testing" = 1 ] && [ "$ready" -eq 1 ] && [ "$result" -eq 0 ]; then
            printf 'CARLA_PLUGIN_LOADED=ok\n'
        fi
        exit "$result"
        """;

    public static async Task<CarlaResult> Run(RuntimeConfiguration configuration, PluginCase plugin)
    {
        var runId = plugin.RunId;
        var info = new ProcessStartInfo(configuration.Backend == "toolbox" ? "toolbox" : "bash")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        info.Environment.Remove("DISPLAY");
        info.Environment.Remove("WAYLAND_DISPLAY");
        info.Environment.Remove("XAUTHORITY");

        if (configuration.Backend == "toolbox")
        {
            info.ArgumentList.Add("run");
            info.ArgumentList.Add("--container");
            info.ArgumentList.Add(configuration.Toolbox);
            info.ArgumentList.Add("env");
            RuntimeTestEnvironment.AddEnvironmentArguments(info.ArgumentList);
            info.ArgumentList.Add("bash");
        }
        else
        {
            RuntimeTestEnvironment.Apply(info);
        }

        info.ArgumentList.Add("-s");
        info.ArgumentList.Add("--");
        info.ArgumentList.Add(runId);
        info.ArgumentList.Add(configuration.Carla);
        info.ArgumentList.Add(plugin.Format);
        info.ArgumentList.Add(plugin.Plugin);
        info.ArgumentList.Add(plugin.Testing ? "1" : "0");
        info.ArgumentList.Add("30s");
        info.ArgumentList.Add(configuration.CabinetFiles);

        using var process = Process.Start(info)
                            ?? throw new InvalidOperationException("could not start supervisor");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await process.StandardInput.WriteAsync(Supervisor.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            TerminateSupervisor(runId, configuration);

            if (!process.HasExited)
            {
                using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await process.WaitForExitAsync(cleanupTimeout.Token);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }

            throw new TimeoutException($"runtime supervisor did not finish for {plugin.Name}");
        }

        var combined = (await output) + Environment.NewLine + (await error);
        return new CarlaResult(process.ExitCode, combined);
    }

    private static void TerminateSupervisor(string runId, RuntimeConfiguration configuration)
    {
        var state = Path.Combine(RuntimeTestEnvironment.Root, "q", runId);
        var pidPath = Path.Combine(state, "supervisor.pid");

        try
        {
            if (!File.Exists(pidPath) || !int.TryParse(File.ReadAllText(pidPath), out var pid))
            {
                return;
            }

            if (configuration.Backend == "toolbox")
            {
                using var signalProcess = new Process
                {
                    StartInfo = new ProcessStartInfo("toolbox")
                    {
                        UseShellExecute = false,
                    },
                };
                signalProcess.StartInfo.ArgumentList.Add("run");
                signalProcess.StartInfo.ArgumentList.Add("--container");
                signalProcess.StartInfo.ArgumentList.Add(configuration.Toolbox);
                signalProcess.StartInfo.ArgumentList.Add("kill");
                signalProcess.StartInfo.ArgumentList.Add("-TERM");
                signalProcess.StartInfo.ArgumentList.Add(pid.ToString());
                signalProcess.Start();
                signalProcess.WaitForExit();
                return;
            }

            using var supervisor = Process.GetProcessById(pid);
            var commandLine = File.ReadAllText($"/proc/{pid}/cmdline");
            if (!commandLine.Contains(runId, StringComparison.Ordinal))
            {
                return;
            }

            var signalInfo = new ProcessStartInfo("kill")
            {
                UseShellExecute = false,
            };
            signalInfo.ArgumentList.Add("-TERM");
            signalInfo.ArgumentList.Add(pid.ToString());
            using var signal = Process.Start(signalInfo);
            signal?.WaitForExit();

            if (!supervisor.WaitForExit(5000))
            {
                supervisor.Kill(entireProcessTree: true);
            }
        }
        catch (ArgumentException)
        {
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }
}
