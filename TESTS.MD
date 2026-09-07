# Runtime tests

Cabinet's normal checks run on a clean Linux runner and cannot load a real Windows
plugin. The runtime tests therefore use a dedicated Toolbox named `cabinet-runtime`.
The setup creates a persistent test root with a synthetic `HOME`, private Flatpak
user installation, XDG runtime directory and temporary directory.
Each case starts its editor on a private Xvfb display with a lightweight X11 window
manager, never on the desktop.

## Setup

The setup command creates the Toolbox if it does not exist, installs its build
dependencies, installs private copies of the host's local Cabinet build and REAPER,
builds Carla, and installs the fixtures. The Toolbox, packages and test root persist.
The Carla build uses one job to keep the harness within its memory limit. Runtime
tests do not run as part of setup.
The Cabinet ref is captured from the host's local build and REAPER is installed at a pinned
Flatpak commit.

```sh
scripts/setup-carla-tests.sh
```

Carla is built from a pinned 2.6 development commit because Fedora's packaged Carla
does not expose CLAP through `carla-single`. The build is installed in the Toolbox
test root's `~/.var/app/io.github.mark12870.cabinet/data/carla-tests/prefix` and the
source is kept in its `source` sibling.

The setup installs the pinned free catalogue entries, Xvfb and its lightweight window manager, a pinned Windows
Surge XT archive for Windows CLAP coverage, and IK Product Manager. It does not log
in to or install a product through IK Product Manager. The manager test only checks
that the manager itself installs correctly.

## Test matrix

`Cabinet.Runtime.Tests` requires every fixture and fails if one is missing. It starts Carla
through an in-Toolbox supervisor and `xvfb-run`, checks the load log, and cleans up each owned
process, Flatpak instance, display and socket. Carla runs on its dummy audio engine, so the
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
| Windows | Surge XT | CLAP | Windows yabridge CLAP |

Surge XT VST3 covers native Linux VST3 alongside its CLAP and LV2 formats.

Run the complete matrix after setup with:

```sh
toolbox run --container cabinet-runtime \
  dotnet test tests/Cabinet.Runtime.Tests --nologo \
  -m:1 -p:BuildInParallel=false -p:RestoreDisableParallel=true
```

Enter the Toolbox before the test applies its synthetic HOME and XDG paths.

The matrix uses Xvfb because Cabinet's Wine and yabridge boundary is deliberately X11-based.
Windows plugins are tested through Cabinet's yabridge wrappers and
`lib/yabridge/cabinet-wine`, never by loading a DLL directly. Linux plugins are
loaded from Cabinet's native links. Windows LV2 is intentionally absent: yabridge
does not bridge LV2, so there is no supported Windows LV2 test for Cabinet.

The manager is deliberately provisioned separately from acquisition. This keeps the
test repeatable without storing credentials, account state or machine-specific files
in the repository. The manager is not used to install a plugin.
