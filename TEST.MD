# Runtime tests

Cabinet's normal checks run on a clean Linux runner and cannot load a real Windows
plugin. The runtime tests therefore use a persistent Toolbox named `carla`. The
Toolbox keeps Carla and its compiler dependencies out of the immutable host, while
sharing the Flatpak installation and plugin files with the test. Each case starts
its editor on a private Xvfb display with a lightweight X11 window manager, never on the desktop.

## One-time setup

The setup command creates the Toolbox if it does not exist, builds Carla, and installs
the fixtures. Later runs reuse the Toolbox and only rebuild Carla when its pinned
source commit changes.

```sh
scripts/setup-carla-tests.sh
```

Carla is built from a pinned 2.6 development commit because Fedora's packaged Carla
does not expose CLAP through `carla-single`. The build is installed in the Toolbox
user's `~/.var/app/io.github.mark12870.cabinet/data/carla-tests/prefix` and the source
is kept in its `source` sibling.

The setup installs the pinned free catalogue entries, Xvfb and its lightweight window manager, a pinned Windows
Surge XT archive for Windows CLAP coverage, and IK Product Manager. It does not log
in to or install a product through IK Product Manager. The manager test only checks
that the manager itself installs correctly.

## Test matrix

`Cabinet.Runtime.Tests` requires every fixture and fails if one is missing. It starts Carla
through an in-Toolbox supervisor and `xvfb-run`, checks the load log, and cleans up each owned
process, Flatpak instance, display and socket.

| Host side | Plugin | Format | Purpose |
| --- | --- | --- | --- |
| Linux | Decent Sampler | VST2 | Native Linux VST2 |
| Linux | Decent Sampler | VST3 | Native Linux VST3 |
| Linux | Surge XT | CLAP | Native Linux CLAP |
| Linux | Surge XT | LV2 | Native Linux LV2 |
| Windows | Sitala 1 | VST2 | Windows yabridge VST2 and MSI |
| Windows | Valhalla Supermassive | VST2 | Windows yabridge VST2 and ZIP installer |
| Windows | Valhalla Supermassive | VST3 | Windows yabridge VST3 |
| Windows | Surge XT | CLAP | Windows yabridge CLAP |

Surge XT VST3 is excluded because Carla's VST3 loader asserts on its zero-size
editor; Surge XT remains covered through native CLAP/LV2 and Windows CLAP.

Run the complete matrix explicitly:

```sh
CABINET_RUN_CARLA_TESTS=1 dotnet test tests/Cabinet.Runtime.Tests --nologo
```

The matrix uses Xvfb because Cabinet's Wine and yabridge boundary is deliberately X11-based.
Windows plugins are tested through Cabinet's yabridge wrappers and
`lib/yabridge/cabinet-wine`, never by loading a DLL directly. Linux plugins are
loaded from Cabinet's native links. Windows LV2 is intentionally absent: yabridge
does not bridge LV2, so there is no supported Windows LV2 test for Cabinet.

The manager is deliberately provisioned separately from acquisition. This keeps the
test repeatable without storing credentials, account state or machine-specific files
in the repository. The manager is not used to install a plugin.
