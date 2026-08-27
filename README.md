# Cabinet

Windows VST plugins on Linux, packaged as a Flatpak, with **one Wine prefix per plugin** — its own C: drive, registry
and dependencies, instead of a single prefix every installer fights over. Built for immutable systems like Fedora
Silverblue, where Wine and yabridge cannot be installed the ordinary way. Packaging, mostly: the bridging
is [yabridge](https://github.com/robbert-vdh/yabridge) and the compatibility layer is [Wine](https://www.winehq.org).
Cabinet bundles them, gives each plugin its own prefix, and wires the result to your DAW.

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
flatpak run $cabinet library show podolski                  # what one is, and what it costs
flatpak run $cabinet library install surge-xt               # a Linux build, so no Wine at all
flatpak run $cabinet library install fabfilter-total-bundle # downloaded for you, prefix and all
flatpak run $cabinet library install vital ~/Downloads/VitalInstaller.zip  # yours to download
flatpak run $cabinet library remove valhalla-supermassive   # its own uninstaller, then unbridged
flatpak run $cabinet library launch spitfire-audio          # a manager; what it installs, bridged

flatpak run $cabinet new serum                              # a prefix of its own
flatpak run $cabinet install serum ~/Downloads/Serum.exe    # run the installer in it
flatpak run $cabinet dxvk serum                             # Direct3D, which some editors want
flatpak run $cabinet sync                                   # bridge what it installed
flatpak run $cabinet doctor                                 # check both sides

flatpak run $cabinet run serum winecfg                      # winecfg, regedit, anything
flatpak run $cabinet delete serum                           # prefix and plugins, gone
flatpak run $cabinet runners install 9.21                   # fetch a Wine build
flatpak run $cabinet use serum wine-9.21-staging-tkg        # move a prefix onto one

flatpak run $cabinet show serum                             # everything this prefix is set to
flatpak run $cabinet set serum sync fsync                   # system, esync, fsync or ntsync
flatpak run $cabinet set serum dxvk off                     # put Wine's own Direct3D back
flatpak run $cabinet set serum env WINEDEBUG=-all           # WINEDEBUG= removes it
```

`library` is the short way in: it knows which Wine a plugin's editor needs, whether that editor wants DXVK, and it
bridges the result. A plugin you had to buy, or one behind a logged-in account, asks for the file you fetched yourself
and points you at the page to fetch it from — unless the vendor serves a trial anyone can fetch, as FabFilter does. One
with a working Linux build is listed only as that and needs no prefix: its files live in Cabinet's own directory, linked
into `~/.vst3`, `~/.clap`, `~/.lv2` and `~/.vst`, so `library remove` takes them out cleanly, and `enrol` is what lets a
Flatpak DAW follow those links. A few read a directory of their own by name, as the u-he ones do `~/.u-he/<Product>`;
Cabinet fills that and says before deleting it.

`library remove` works on a Windows plugin too: if nothing else Cabinet installed is left in its prefix it offers to
delete the prefix outright, Wine tree and registry with it, and otherwise runs the plugin's own uninstaller and leaves
the prefix for the plugins sharing it. Where nothing looks like it, it says so. A manager is the exception:
`library launch` opens it and bridges what it installs as it lands, `library log` says what it printed, and removing one
takes its prefix.

The four commands after it are the same thing by hand, and still the whole workflow for a plugin the library has never
heard of. `set` is per prefix and reaches the bridged plugin too: a sync mode or a variable set here is handed to the
Wine your DAW starts, not just to `winecfg`.
`sync system` is the default and means *leave it to whatever launched the DAW*. `run` covers what `set` does not;
`delete` asks first, and unbridges what it held.

## Permissions

`enrol` prints the `flatpak override` rather than applying it, because one of the permissions it asks for is
`--talk-name=org.freedesktop.Flatpak`. That lets the shim start Wine on the host — **and it lets that DAW run any
command on your host.** It is a real weakening of that DAW's sandbox, so the decision stays yours; undo it with
`flatpak override --user --reset <daw-id>`. Your prefixes live in `~/.var/app/io.github.mark12870.cabinet/`, so
`flatpak uninstall --delete-data` **will** delete your plugin library — a plain
`flatpak uninstall` leaves it alone.

## Building

```sh
flatpak run org.flatpak.Builder --repo=repo --force-clean --default-branch=stable build io.github.mark12870.cabinet.yml
flatpak remote-add --user --if-not-exists --no-gpg-verify cabinet-local "file://$PWD/repo"
flatpak install --user --or-update cabinet-local io.github.mark12870.cabinet
```

## License

GPL-3.0-or-later, matching yabridge. See [LICENSE](LICENSE). The app icon combines the *dresser*
and *piano-keys* icons from [Phosphor Icons](https://phosphoricons.com), recoloured — MIT, see
[data/LICENSE.phosphor](data/LICENSE.phosphor).
