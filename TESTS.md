# Runtime tests

Cabinet's normal checks run on a clean Linux runner and cannot load a real Windows
plugin. The runtime tests therefore use a dedicated Toolbox named `cabinet-runtime`.
The setup creates a persistent test root with a synthetic `HOME`, private Flatpak
user installation, XDG runtime directory and temporary directory. Each case runs
Carla's no-GUI frontend on a headless `weston` of its own.

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

The setup installs the pinned free catalogue entries, FabFilter Total Bundle for Windows CLAP
coverage, and IK Product Manager. It does not log in to or install a product through IK Product
Manager. The manager test only checks that the manager itself installs correctly.

FabFilter is the one fixture that is neither free nor pinned: it is the only Windows catalogue
entry offering CLAP, its entry is a `rolling` source whose URL the vendor changes with every
release, and its plugins run on a 30-day trial that opens an evaluation dialog over the editor.
Delete the `fabfilter` prefix from the test root and run the setup again to start that trial
over.

## In CI

`.github/workflows/ci.yml`'s `runtime` job runs this suite on every push to `main`, and on demand,
against the Flatpak the same run built. `publish` waits for it, so a release needs a green runtime
run. The drag-and-drop probes are the exception: they say when a yabridge patch can go rather than
whether this commit works, and their four prefixes and three runner families are five gigabytes
every push would otherwise pull, so they run when the fixtures are made — a rebuild, or the turn
of the month — and `clean` keeps them out of the image.

The fixtures cannot be made per run — about 13 GB on a runner that installs all of them, out of
seven vendor installers, five Wine runners and a serial Carla build — and `actions/cache` holds
10 GB for a whole repository. So they are cached in two halves, split by what may be handed out again:

- **A public image, `ghcr.io/<owner>/cabinet-runtime`.** Free software only: the Fedora tools, the
  Carla build, the runners a catalogue entry pins, the Flatpak runtimes, and Surge XT, which is
  GPL-3.0. A public package costs nothing to store or to pull, and pulls from Actions are free of
  transfer charges either way. Every push pulls it, so what rides in it is what every push pays
  for — about two minutes, measured, once it carries only English locales, the runners a
  catalogue entry pins, and a plain Fedora base rather than a toolbox one.
- **This repository's own Actions cache**, which only its workflows can read. Every other fixture
  is marked `Freeware` or `Commercial` in the catalogue, and REAPER's Flathub manifest fetches it
  as extra data rather than shipping it: Cabinet may install those on a machine, but nothing here
  may republish a vendor's binaries. The five plugin prefixes and Decent Sampler's native files
  ride there, about 4 GB, and `runtime-ci.sh clean` takes them and REAPER out of the container
  before the image is committed. `WorkflowTests` derives that list from the catalogue, so a new
  fixture that no licence allows to be published fails the build rather than reaching the image.

`scripts/runtime-ci.sh` drives every step inside the container: the `direct` backend rather than a
Toolbox, an unprivileged user because Wine refuses to run as root, and a headless `weston` for the
vendor installers, which need a display. A push restores the cache, runs the setup — where all
that is left is the new Cabinet commit and a sync — and commits the free half back, so the next
push starts from it. Both halves are kept whenever the setup succeeded rather than when the tests
passed: Decent Sampler fails the first time its editor opens on a root that has no config of its
own yet, and gating on green would let one such plugin keep the fixtures from ever being kept, so
every push would reinstall them and fail again.

What the image must carry can be checked without a run, in the time a `dnf install` takes:

```sh
podman run --rm --security-opt label=disable -v "$PWD":/src:ro \
  registry.fedoraproject.org/fedora:44 bash /src/scripts/runtime-ci.sh prepare
```

`prepare` asks Carla's own questions — `which pyuic5`, the Qt5 pkg-config pair, `moc`, `rcc` and
`uic` — because Carla installs a no-gui build without a word when one of them fails, and the setup
then stops twenty minutes later for want of `bin/carla`. A minimal base image and a toolbox one
differ in exactly that sort of package.

Nothing has to be baked by hand. A run with nothing to pull installs the fixtures itself and
pushes the result, which is how the tag is seeded and how it returns if it is deleted. The vendors'
half is reinstalled at the turn of each month, because the month is in the cache key and there is
no fallback key, and that is what starts FabFilter's 30-day trial over. `Run workflow` with
`rebuild_fixtures` does both from scratch on demand: a squashed image, since the layers a push adds
are only squashed by starting from the base again, and a new trial. Those runs take hours; an
ordinary push takes minutes.

Set the package's visibility to public once it exists — a package starts private, and a private one
of this size bills against the account's quota. A layer has to stay under 10 GB and upload inside
ten minutes, which is the ceiling to watch if the free half ever grows.

Every run uploads a `runtime` artifact — the interaction captures with their `result.txt`, the
Carla supervisor logs, Cabinet's own logs and the `.trx`. Read them before believing a failure. A
hosted runner has no GPU, so DXVK draws through lavapipe there and the numbers below are this
machine's, not its.

## Test matrix

`Cabinet.Runtime.Tests` requires every fixture and fails if one is missing. It starts Carla's
full frontend with `--no-gui` through an in-Toolbox supervisor, checks the load log, and cleans
up each owned process, Flatpak instance and socket. Carla uses its dummy audio engine, so the
Toolbox needs no JACK server.

Each case takes a display of its own, as the editor cases do, and `--no-gui` keeps every editor
shut. It used to run with none, which held on this machine and not on a runner: with no display
Wine loads no display driver, so winevulkan offers no surface extension, `vkCreateInstance` fails
with `-7`, and DXVK's failure goes through Wine's unwinder — `unknown CFA opcode`, repeatedly —
into a stack overflow that takes the host with it. SINE Player died that way on every CI run. A
DAW has a display; the matrix now says so, at two seconds across the nine cases.

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
| Windows | FabFilter Micro | CLAP | Windows yabridge CLAP |

Surge XT VST3 covers native Linux VST3 alongside its CLAP and LV2 formats. FabFilter Micro is
the CLAP case because it is the only Windows catalogue entry that ships one, and `Micro` because
its name carries no version for a vendor update to change.

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

## Plugin scenarios

A plugin scenario is an end-to-end test of one catalogue entry, in
`tests/Cabinet.Runtime.Tests/Scenarios/<Entry>Scenario.cs`. It names its entry once, as
`const string Id`, and takes everything else from the parsed `.yml`: `InstalledEntry` installs the
entry's pinned download into an empty synthetic Cabinet home once per class, and the scenario runs one
case per format in `Formats:`. A version or URL bump in the entry is therefore what the next run tests,
and a format the entry gains fails until the scenario says which bridged file it expects.
`CatalogueTests` checks that every scenario names a shipped entry and that no two name the same one.
`scripts/scenario-coverage.sh` warns about every shipped entry that has no scenario yet, as an
annotation on the entry's `.yml` and a list in the CI `checks` job summary; a scenario naming the
entry clears it.
`ScenarioHarness` owns isolation, process supervision, Carla and artefact collection; the scenario
owns its expectations. Run one with:

```sh
toolbox run --container cabinet-runtime \
  dotnet test tests/Cabinet.Runtime.Tests --nologo -m:1 \
  --filter 'FullyQualifiedName~ValhallaSupermassiveScenario'
```

For each format, `ValhallaSupermassiveScenario` verifies the bridged editor and processes explicit
stereo buffers through Carla's native rack with Mix fully wet, so only the plugin's own output can
fill the tail. Input, output, parameters, captures and logs remain under
`$CABINET_RUNTIME_ROOT/tmp/scenarios/<id>/artefacts/{editor,audio}/<format>/`. Tools and the
prepared Flatpak are reused, but Cabinet downloads and installs the runner, DXVK and plugin into the
new home before it creates the prefix and bridge. Carla only observes the resulting bridge as a DAW.
The render probe is a library that `audio-render.py` loads through `ctypes`, the way
the editor probes load Carla: linked into an executable, Carla's bundled asio takes the place of
yabridge's own and the VST3 bridge crashes while it loads.

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
capture against the first. Five things are asserted per plugin.

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
- **Every frame was measured.** A capture that comes back empty while its window is still mapped
  is a probe that went blind, not a frame that did not change, and it fails the run.

A plugin's interface is the plugin's rather than the format's, so each plugin is exercised once.
Valhalla Supermassive is the exception, in VST2 and VST3, because it is one plugin whose two
bridged formats are the only same-binary comparison the catalogue offers; Surge XT runs natively
as the control, since a check that fails natively too is the harness's fault and not the bridge's.

Measured over the whole set on 2026-09-20, which is where the floors come from:

| Plugin | Path | Colours | Reaction | Noise | Worst idle |
| --- | --- | --- | --- | --- | --- |
| Decent Sampler | native VST2 | 4324 | 0.0028 | 0 | **1793 ms** |
| FabFilter Micro | bridged CLAP | 10290 | 0.3514 | 0 | 0 ms |
| Valhalla Supermassive | bridged VST2 | 2352 | 0.0083 | 0 | 0 ms |
| Valhalla Supermassive | bridged VST3 | 2352 | 0.0083 | 0 | 0 ms |
| Surge XT | native CLAP | 5075 | 0.0043 | 0 | 29 ms |
| Sitala | bridged VST2 | 2178 | 0.0026 | 0 | 0 ms |
| SINE Player | bridged VST3 | 256 | 0.0007 | 0 | 0 ms |

Every path Cabinet bridges answers the pointer: VST2, VST3 and CLAP, against native VST2 and
native CLAP.

A drawn editor holds hundreds to thousands of colours and a blank one holds one, so the colour
floor is 16. The reaction floor is 0.0005. Reaction is the fraction of pixels that changed, so it
does not shrink as the window grows — an earlier RMSE metric did, and reported a WebView2 login
screen answering a click with a text caret as zero. SINE Player is that login screen and answers
at 0.0007, the narrowest margin in the set, so the floor cannot rise without losing it.

Each frame is compared with the one before it rather than with the first one, and the control is
the same measurement with the pointer parked outside the window, taken before the sweep and again
after it. Measured against the first frame instead, a change an editor makes once — settling after
it opens, a dialog closing — differs from the baseline in every later frame, and the sweep reports
the first of them as a reaction. Three of these plugins passed that way until the control was
measured the same way as the reaction.

### How long a host may be held

Every case carries its own idle ceiling. Seven of them get `Steady`, 250 ms, which is the bound
the whole check exists for. Decent Sampler gets `Stalling`, 2500 ms, because it freezes the host
once while its editor opens: four runs measured 1735, 22, 1863 and 1793 ms. 1.25.0 opened that
editor behind a vendor "a new version is available" dialog, which was the whole of its 0.9986
reaction and the assumed cause; 1.32.0 is current, the dialog is gone, the editor answers at
0.0028 over 4324 colours, and the stall stayed. So it was never the dialog.

A ceiling rather than a flag, because the flag this replaced asserted the *defect was still
there* and so went red on the run that measured 22 ms — the suite failing because the plugin
behaved. It also took Decent Sampler out of the bound altogether, so a stall grown to 30 s would
have passed. A per-case ceiling is green either way and still catches that.

The ceiling has to fit the slowest machine the suite runs on, not this one. The same stall
measures 3138 ms on a hosted runner — two cores and software rendering — where it measures under
1.9 s here, so `Stalling` is 5000 ms. That is still an order of magnitude under the freeze the
bound exists to catch, and a number measured on a desktop is not a bound a runner can meet.

Decent Sampler's behaviour depends on `~/.config/DecentSampler`. With no config at all it opens a
modal welcome screen over its interface, and a sweep across that screen measures 1418 colours and
a reaction of exactly 0.000000, which fails `AnEditorRespondsToThePointer`; CI's first cold root
measured exactly that. The setup now writes the `welcomeScreenAlreadyShown` flag the plugin writes
itself on first open, so the fixtures own that state instead of the suite being green only on a
root that has opened the plugin before.

### What the probe must never do again

Three plugins were recorded here as taking no pointer input at all. None of them did. `settle()`
sent `Escape` to the editor before each click, and an editor that has just been given focus by
`xdotool windowactivate` closes on `Escape`; every capture after that read a window that was
gone. `xwd` writes an empty file for a dead drawable rather than failing, so each of those frames
scored as no change, and the sweep reported a plugin no user could operate. Removing that one key
press took Decent Sampler from 0.0000 to 0.9986.

Two guards now stand where that went wrong. A capture that comes back empty while its window is
still mapped counts as `BLIND`, and `EveryFrameOfAnEditorIsMeasured` requires `BLIND=0`, so a
probe that goes blind fails instead of reporting zeros it never measured. A capture that fails
because the window is gone sets `CLOSED`, which counts as the editor answering: a plugin that
closes its own editor under the pointer received the pointer.

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
