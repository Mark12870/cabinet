# Runtime tests

Cabinet's normal checks run on a clean Linux runner and cannot load a real Windows
plugin. The runtime tests therefore use a dedicated Toolbox named `cabinet-runtime`.
The setup creates a persistent test root with a synthetic `HOME`, private Flatpak
user installation, XDG runtime directory and temporary directory. Each case runs
Carla's no-GUI frontend without a display.

## Setup

The setup command creates the Toolbox if it does not exist, installs its build
dependencies, installs private copies of the host's local Cabinet build and REAPER,
builds Carla, and installs the fixtures. The Toolbox, packages and test root persist.
The Toolbox includes the .NET 10 SDK. The Carla build uses one job to keep the harness
within its memory limit. Runtime tests do not run as part of setup.
The Cabinet ref is captured from the host's local build and REAPER is installed at a pinned
Flatpak commit.

```sh
scripts/setup-runtime-tests.sh
```

Carla is built from a pinned 2.6 development commit because Fedora's packaged Carla
does not expose the required CLAP support. The build is installed in the Toolbox test
root's `~/.var/app/io.github.mark12870.cabinet/data/carla-tests/prefix` and the source
is kept in its `source` sibling.

The setup installs the pinned free catalogue entries, a pinned Windows Surge XT archive
for Windows CLAP coverage, and IK Product Manager. It does not log
in to or install a product through IK Product Manager. The manager test only checks
that the manager itself installs correctly.

## Test matrix

`Cabinet.Runtime.Tests` requires every fixture and fails if one is missing. It starts Carla's
full frontend with `--no-gui` through an in-Toolbox supervisor, checks the load log, and cleans
up each owned process, Flatpak instance and socket. Carla uses its dummy audio engine, so the
Toolbox needs no JACK server.

| Host side | Plugin | Format | Purpose |
| --- | --- | --- | --- |
| Linux | Decent Sampler | VST2 | Native Linux VST2 |
| Linux | Surge XT | VST3 | Native Linux VST3 |
| Linux | Surge XT | CLAP | Native Linux CLAP |
| Linux | Surge XT | LV2 | Native Linux LV2 |
| Windows | Sitala 1 | VST2 | Windows yabridge VST2 and MSI |
| Windows | Valhalla Supermassive | VST2 | Windows yabridge VST2 and ZIP installer |
| Windows | Valhalla Supermassive | VST3 | Windows yabridge VST3 |
| Windows | SINEplayer | VST3 | Windows yabridge VST3 |
| Windows | Surge XT | CLAP | Windows yabridge CLAP |

Surge XT VST3 covers native Linux VST3 alongside its CLAP and LV2 formats.

## Wine sessions

`Cabinet.Contract.Tests`, run by `scripts/checks.sh`, drives Core's real `ProcessRunner` through
the built shim on the DAW path, with scripts standing in for `flatpak` and Wine. A shim running
inside Cabinet's own sandbox needs the installed `/app/lib/yabridge/cabinet-wine`, so
`TeardownTests` covers that path here: a job Cabinet starts carries its exit status and its
session retires, and Cabinet joins a session a DAW started rather than starting a second one.

Ownership is the other half: `OwnershipTests` holds a DAW-side plugin job in a prefix of its own
and checks that `set desktop on`, `use` and `winetricks` are all refused with the reason, that the
plugin keeps its Wine, and that the same change goes through once the plugin is gone. It also ends
Wine from inside a session Cabinet started, which is what a forced `library stop` does.

## Winelib hosts

yabridge's hosts are built against Debian's Wine 8.0 headers and import libraries but run on the
Wine base, which moves on its own. `WinelibHostTests` starts both `yabridge-host.exe.so` and
`yabridge-host-32.exe.so` under the bundled Wine in a fresh prefix, and asserts each prints its
version banner with no `err:module` import failure. No 32-bit plugin is hosted.

Run the complete matrix after setup with:

```sh
toolbox run --container cabinet-runtime \
  dotnet test tests/Cabinet.Runtime.Tests --nologo \
  -m:1 -p:BuildInParallel=false -p:RestoreDisableParallel=true
```

Enter the Toolbox before the test applies its synthetic HOME and XDG paths.

Windows plugins are tested through Cabinet's yabridge wrappers and
`lib/yabridge/cabinet-wine`, never by loading a DLL directly. Linux plugins are
loaded from Cabinet's native links. Windows LV2 is intentionally absent: yabridge
does not bridge LV2, so there is no supported Windows LV2 test for Cabinet.

## Patch probes

`DragAndDropTests` tells you when `patches/yabridge-foreign-drag-drop.patch` is no longer needed
(see `PATCHES.md`). The Toolbox's `mingw64-gcc` compiles a small Windows probe from
`Probes/revoke-drag-drop.c`. Each case runs it through the shim in a probe prefix on the newest
release of a runner family:

- the bundled Wine;
- mklnln's latest d2d1 build on GitHub (Cabinet's index pins only one);
- the newest Kron4ek and the newest Soda release in `cabinet runners available`.

Each setup run moves a probe prefix onto a newer release as soon as one appears. The probe
registers drag-and-drop on its own window and starts a copy of itself that revokes it. The tests
pass while those releases still crash on that call. A case that fails names a release that has
the fix.

The manager is deliberately provisioned separately from acquisition. This keeps the
test repeatable without storing credentials, account state or machine-specific files
in the repository. The manager is not used to install a plugin.

## Editor geometry

`EditorTests` opens a bridged plugin's editor through Carla's `carla_backend`, with `DISPLAY`
kept, and compares where yabridge puts the Wine window against the wrapper window hosting it.
The two must share an origin: a Wine window that drifts draws off the frame the DAW shows, which
is a blank editor and nothing in a log.

`AnEditorIsWhereWineHasBeenToldItIs` covers the other half of the same contract: yabridge sends
Wine a synthetic `ConfigureNotify` naming the editor's position, and Wine turns screen coordinates
into client ones with it. A window that is not where Wine believes it is delivers every click at
that offset, so the trace's `Translated coords` must match where X puts the window.

The window ids come from yabridge's own `+editor` trace, so the cases need no DAW and no pixels.
Both formats are covered because both drifted; only VST2 showed it, since VST3 is pulled back by
the size-mismatch poll in `vst3.cpp`. Both suites pass as of 2026-09-16 with
`patches/yabridge-editor-window-origin.patch` applied; without it each reports the offset it
measured. Like every other editor case they run on their own headless `weston` (`Display.Start`),
so a run never puts a plugin window on the user's screen.

## Editor rendering and interaction

Geometry says where a window is, not whether the user can use what is in it. `InteractionTests`
answers that: `Probes/editor-interaction.py` opens each plugin's editor, captures the window,
sweeps the pointer over a grid inside it, then clicks and drags at four points, comparing every
capture against the first. Four things are asserted per plugin.

- **It draws.** The capture must hold more than a handful of distinct colours. One flat colour is
  the blank frame a DAW shows when the plugin renders somewhere else, and nothing in a log says
  so.
- **It reacts.** The change the pointer causes must beat the change measured with the pointer held
  still. That second number is the point: an editor that animates on its own would otherwise pass
  without ever receiving an event.
- **The host keeps responding.** Every `engine_idle` is timed, as are opening and closing the
  editor, removing the plugin and `engine_close`. A host that stops servicing its loop is the DAW
  going unresponsive under the user's hands.
- **It survives.** The editor is closed and opened again, and the host output must not report a
  plugin crashing while being torn down.

Format is not an axis. A plugin's interface is the plugin's, not the format's, so each plugin is
exercised once, and Surge XT twice because native and bridged are different binaries down
different paths and the native run is the control: a check that fails natively too is the
harness's fault, not the bridge's.

Measured over the whole set on 2026-09-20, which is where the floors come from:

| Plugin | Path | Colours | Reaction | Noise | Worst idle |
| --- | --- | --- | --- | --- | --- |
| Valhalla Supermassive | bridged VST2 | 2352 | 0.0083 | 0 | 0 ms |
| Sitala | bridged VST2 | 2178 | 0.0026 | 0 | 0 ms |
| Surge XT | native CLAP | 5075 | 0.0048 | 0 | 29 ms |
| Surge XT | bridged CLAP | 5054 | **0.0000** | 0 | 9 ms |
| SINE Player | bridged VST3 | 256 | **0.0000** | 0 | 0 ms |
| Decent Sampler | native VST2 | 1597 | **0.0000** | 0 | **1571 ms** |

A drawn editor holds hundreds to thousands of colours and a blank one holds one, so the colour
floor is 16. A reacting editor moved at least 0.0026 of its pixels and an unreacting one moved
none, so the reaction floor is 0.0005, five times under the weakest real reaction. Reaction is
the fraction of pixels that changed, so it does not shrink as the window grows — an earlier RMSE
metric did, and reported a WebView2 login screen answering a click with a text caret as zero.

### Known defects

Three plugins fail a check today. Their cases carry a `Known` flag and assert that the defect is
still there, the way `DragAndDropTests` does, so the suite stays green and tells you when one is
fixed rather than going red every run.

- **Surge XT through the bridge takes no pointer input** (`Known.NoInput`). It draws identically
  to the native build, 5054 colours against 5075, and the geometry cases pass, but no hover,
  click or drag over twenty points moves a single pixel. Native Surge XT on the same compositor
  answers at 0.0048, and bridged VST2 and VST3 both answer, so the bridge's CLAP editor input
  path is the one thing left.
- **SINE Player takes no pointer input** (`Known.NoInput`). Its WebView2 editor renders the
  Orchestral Tools login screen and nothing answers the pointer. Unverified whether this shares a
  cause with the above.
- **Decent Sampler stalls the host for 1.5 s and takes no pointer input** (`Known.Stalls |
  Known.NoInput`). Its editor opens behind a vendor "a new version is available" dialog, so the
  sweep lands on a dimmed backdrop; the stall is consistent with its update check running on the
  GUI thread. This one follows the pinned build and will change when the fixture is refreshed.

Native Surge XT loses the X server partway through a full sweep — `XIO: fatal IO error` during a
drag, reproducibly, at the same gesture. It does not show up now because the sweep stops as soon
as a plugin has answered, and Surge XT answers early. It is unexplained and is not a Cabinet
defect as far as anything here shows.

### Running it

Each probe gets its own headless `weston` with `--xwayland` and `--refresh-rate=60000`
(`Display.Start`). Synthetic input must not land on the session display, and a capture needs an
unobscured screen. Weston also brings its own window management, so no separate window manager is
needed.

It must be `weston`, not `Xvfb`. A virtual display with no refresh rate has no vblank, DXVK
throws inside `DxgiOutput::WaitForVBlank`, and Wine's unwinder turns that into a stack overflow:
on `Xvfb`, SINE Player died four seconds into `add_plugin`, its main thread gone and its process
alive at no CPU, which is the hang AGENTS.md describes — the host waits on it forever. Under
`weston` the same plugin loads and draws.

Cabinet's sandbox reaches the compositor's X server through `--filesystem=/tmp/.X11-unix`; the
toolbox shares the host's `/tmp`, so the socket is visible from both sides.

Two things the probe must keep doing. Capture with `xwd`, never ImageMagick's `import`: `import`
grabs the X server, so a plugin whose menu holds a grab wedges the probe until its timeout. And
end the compositor with `SIGTERM`, never `SIGKILL`: a killed `weston` cannot unlink its own socket
in `/tmp/.X11-unix`, and waiting for that socket to go cost a minute per plugin, five of the
eight that a full run once took. A run is about two minutes.

Every capture is kept under `$CABINET_RUNTIME_ROOT/tmp/interaction/<plugin>/`, with the
measurement in `result.txt` beside them. Read them before believing a failure.
