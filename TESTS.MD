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
(see `PATCHES.MD`). The Toolbox's `mingw64-gcc` compiles a small Windows probe from
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
measured.
