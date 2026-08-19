# Cabinet

Windows VST plugins on Linux, packaged as a Flatpak, with **one Wine prefix per plugin** —
its own C: drive, registry and dependencies, instead of a single prefix every installer
fights over.

Built for immutable systems like Fedora Silverblue, where Wine and yabridge cannot be
installed the ordinary way. `x86_64` only.

> Packaging, mostly. The bridging is [yabridge](https://github.com/robbert-vdh/yabridge)
> and the compatibility layer is [Wine](https://www.winehq.org); Cabinet bundles them,
> gives each plugin its own prefix, and wires the result to your DAW.

## Install

```sh
flatpak remote-add --if-not-exists cabinet \
  https://mark12870.github.io/cabinet/io.github.mark12870.cabinet.flatpakrepo
flatpak install cabinet io.github.mark12870.cabinet
flatpak run io.github.mark12870.cabinet enrol fm.reaper.Reaper
```

There is no setup step. `enrol` prepares your DAW and **prints** a `flatpak override`
command for you to run — see [Permissions](#permissions).

## Use

```sh
cabinet=io.github.mark12870.cabinet

flatpak run $cabinet                                        # the window, also in your launcher

flatpak run $cabinet library                                # plugins it can install for you
flatpak run $cabinet library install surge-xt               # a Linux build, so no Wine at all
flatpak run $cabinet library install serum serum ~/Downloads/Serum.exe   # prefix, Wine, DXVK

flatpak run $cabinet new serum                              # a prefix of its own
flatpak run $cabinet install serum ~/Downloads/Serum.exe    # run the installer in it
flatpak run $cabinet dxvk serum                             # JUCE editors need this
flatpak run $cabinet sync                                   # bridge what it installed
flatpak run $cabinet doctor                                 # check both sides
flatpak run $cabinet about                                  # version, and where it came from

flatpak run $cabinet list                                   # prefixes, runners and paths
flatpak run $cabinet run serum winecfg                      # winecfg, regedit, anything
flatpak run $cabinet delete serum                           # prefix and plugins, gone

flatpak run $cabinet runners                                # Wine builds you have
flatpak run $cabinet runners install 9.21                   # fetch one
flatpak run $cabinet new serum wine-9.21-staging-tkg        # a prefix on it
flatpak run $cabinet use serum wine-9.21-staging-tkg        # move an existing prefix

flatpak run $cabinet show serum                             # everything this prefix is set to
flatpak run $cabinet set serum sync fsync                   # system, esync, fsync or ntsync
flatpak run $cabinet set serum dxvk off                     # put Wine's own Direct3D back
flatpak run $cabinet set serum env WINEDEBUG=-all           # WINEDEBUG= removes it
```

`library` is the short way in: it knows which Wine a plugin's editor needs, whether its
editor wants DXVK, and it bridges the result — the four steps below, done for you. Plugins
you had to buy cannot be downloaded, so those ask for the installer you already have. A plugin
with a working Linux build is listed only as that, because a native one needs no prefix, no
pinned Wine and no bridge. Linux
plugins in the library need no prefix at all; their files go in Cabinet's own directory and
are linked into `~/.vst3`, `~/.clap`, `~/.lv2` and `~/.vst`, so `library remove` takes them out
cleanly —
`enrol` is what lets a Flatpak DAW follow those links.

The four commands after it are the same thing by hand, and still the whole workflow for a
plugin the library has never heard of. `set` is per prefix, and reaches the bridged plugin
too: a sync mode or a variable set here is handed to the Wine your DAW starts, not just to
`winecfg`. `sync system` is the default and means *leave it to whatever launched the DAW*.
`run` is the escape hatch for anything `set` does not cover; `delete` asks before it does
anything, and wants a `sync` after it to unbridge what it held.

## Permissions

`enrol` prints the `flatpak override` rather than applying it, because one of the
permissions it asks for is `--talk-name=org.freedesktop.Flatpak`. That lets the shim start
Wine on the host — **and it lets that DAW run any command on your host.** It is a real
weakening of that DAW's sandbox, so the decision stays yours. Undo it with
`flatpak override --user --reset <daw-id>`.

Your prefixes live in `~/.var/app/io.github.mark12870.cabinet/`, so
`flatpak uninstall --delete-data` **will** delete your plugin library. A plain
`flatpak uninstall` leaves it alone.

## Building

```sh
flatpak run org.flatpak.Builder --repo=repo --force-clean \
  --default-branch=stable build io.github.mark12870.cabinet.yml
flatpak remote-add --user --no-gpg-verify cabinet-local "file://$PWD/repo"
flatpak install --user cabinet-local io.github.mark12870.cabinet
```

## License

GPL-3.0-or-later, matching yabridge. See [LICENSE](LICENSE).

The app icon is the *dresser* icon from [Phosphor Icons](https://phosphoricons.com),
recoloured. Phosphor is MIT — see [data/LICENSE.phosphor](data/LICENSE.phosphor).
