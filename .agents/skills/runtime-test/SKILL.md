---
name: runtime-test
description: Test or diagnose Cabinet catalogue entries at runtime through an isolated Toolbox and Carla. Use before installing, launching, reconfiguring or removing a catalogue entry for a test, running `Cabinet.Runtime.Tests`, or diagnosing a plugin or manager that fails at runtime.
---

# Test catalogue entries

1. Build the current Flatpak as `build-and-test` describes. Pass its exact OSTree commit as
   `CABINET_RUNTIME_CABINET_REF` when preparing the runtime, so the test runs this build and not an older one.

2. Prepare the isolated Toolbox, private Cabinet and REAPER Flatpaks, pinned full Carla
   build, and catalogue fixtures:

   ```sh
   COMMIT=$(ostree --repo=repo rev-parse app/io.github.mark12870.cabinet/x86_64/stable)
   CABINET_RUNTIME_CABINET_REF="io.github.mark12870.cabinet/x86_64/stable/$COMMIT" \
     scripts/setup-runtime-tests.sh
   ```

3. Run the general runtime suite, the plugin matrix alone:

   ```sh
   toolbox run --container cabinet-runtime \
     dotnet build tests/Cabinet.Runtime.Tests --nologo \
     -m:1 -p:BuildInParallel=false -p:RestoreDisableParallel=true
   toolbox run --container cabinet-runtime \
     dotnet test --project tests/Cabinet.Runtime.Tests --no-build \
     --filter-not-namespace Cabinet.Runtime.Tests.Scenarios \
     --filter-not-namespace Cabinet.Runtime.Tests.Patches
   ```

   For one plugin entry, run its scenario alone as the `plugin-scenario` skill describes.

4. For a manager entry, create a fresh prefix in the isolated runtime for each
   configuration attempt. Install the entry into that prefix with `DISPLAY` set, since a vendor installer needs a
   display, and launch it with `cabinet library launch <id>`, never `cabinet run`.

   `rt` runs the isolated Cabinet; every later step and skill uses it:

   ```sh
   ROOT="${CABINET_RUNTIME_ROOT:-${XDG_CACHE_HOME:-$HOME/.cache}/cabinet-rt}"
   rt() {
     toolbox run --container cabinet-runtime env \
       HOME="$ROOT/home" XDG_RUNTIME_DIR="$ROOT/runtime" \
       XDG_DATA_HOME="$ROOT/home/.local/share" \
       FLATPAK_USER_DIR="$ROOT/home/.local/share/flatpak" \
       flatpak run --nofilesystem=home --filesystem="$ROOT":create \
       io.github.mark12870.cabinet "$@"
   }
   rt library install <id> --prefix <fresh-prefix>
   rt library launch <id>
   ```

5. Before changing configuration, compare the complete nearest working entry and
   any relevant upstream or community installation procedure. Change one variable
   at a time.

6. Treat a manager launch as successful only when its expected window, renderer or
   helper processes, and launch log all agree that it is usable. Process survival
   alone is insufficient.

7. Wrap every command run in the isolated runtime in a short `timeout`: that one step's
   measured duration plus a small margin (a removal 1 minute, an install 3), never one value
   shared by a whole script. When one fires, or a stopped test leaves a run behind, end what
   remains with `scripts/runtime-cleanup.sh`.

8. Remove only temporary prefixes in the isolated runtime after testing. Host prefixes stay as they are.

## Reference

- A DAW in a test copies `ScenarioHarness.RenderAsTake`'s launch: it forwards `CABINET_RUNTIME_ROOT` and
  `FLATPAK_USER_DIR`, without which the shim cannot reach the prefix and the plugin silently fails to load,
  and its private bus starts only Flatpak's session helper.
- `docs/TESTS.md` holds the plugin matrix and the unsupported Windows LV2 combination.
- A scenario's editor check drives the pointer on its own headless `weston`, so it needs no session display. When one
  fails, read the captures and `result.txt` in `$CABINET_RUNTIME_ROOT/tmp/scenarios/<id>/artefacts/editor/` before
  believing the number in the message, and copy them aside before any rerun.
- An editor can be exercised without a DAW: drive Carla's `carla_backend` from Python in the isolated runtime
  (`engine_init("Dummy")`, `add_plugin`, `show_custom_ui`, a timed `engine_idle` loop, `engine_close`) with `DISPLAY`
  kept, under `timeout`. A stall in `engine_close` is the DAW freeze.
- An editor that exists is not an editor that works. Judge one by pixels and input: capture the window and require
  more than one colour in it, then move, click and drag the pointer inside it and require the frame to change by more
  than it changes with the pointer held still. A blank frame and a dead frame are both invisible in every log.
- Give anything that sends synthetic input its own display, never the session one, and make it a headless `weston`
  with `--xwayland --refresh-rate=60000`. A display with no refresh rate has no vblank, and a plugin that presents
  through DXGI dies in Wine's unwinder on one. Capture with `xwd`: ImageMagick's `import` grabs the X server, so a
  plugin holding a menu wedges it. End the compositor with `SIGTERM`; killed, it cannot unlink its own socket. Take
  the client window, the one with `WM_STATE`, not the frame the compositor wraps it in, or every coordinate is off by
  the title bar.
- Wine runs every process elevated, and WebView2 reads an elevated host's browser flags from
  `HKLM\Software\Policies\Microsoft\Edge\WebView2\AdditionalBrowserArguments`, valued by host executable
  (`yabridge-host.exe`); `CatalogueTests` enforces that location. Read `msedgewebview2.exe`'s
  `/proc/<pid>/cmdline` before crediting a flag with any effect.
- Redirecting `XDG_DATA_HOME` to isolate it also hides the user Flatpak installation from the shim's `flatpak run`
  (`app/io.github.mark12870.cabinet/x86_64/master not installed`); set `FLATPAK_USER_DIR` beside it, as step 4 does.
- Native REAPER quit from a ReaScript (`Main_OnCommand(40004)`) with a bridged plugin loaded stalled for 9 s to over
  100 s while `YABRIDGE_DEBUG_LEVEL=1` showed every bridge call answered; run it under `timeout` and read the
  ReaScript's own output.
