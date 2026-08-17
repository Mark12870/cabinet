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

flatpak run $cabinet new serum                              # a prefix of its own
flatpak run $cabinet install serum ~/Downloads/Serum.exe    # run the installer in it
flatpak run $cabinet sync                                   # bridge what it installed
flatpak run $cabinet doctor                                 # check both sides

flatpak run $cabinet list                                   # prefixes, runners and paths
flatpak run $cabinet run serum winecfg                      # winecfg, regedit, anything
flatpak run $cabinet delete serum                           # prefix and plugins, gone

flatpak run $cabinet runners                                # Wine builds you can use
flatpak run $cabinet new serum wine-9.21                    # a prefix on one of them
flatpak run $cabinet use serum wine-9.21                    # move an existing prefix
```

The first four are the whole workflow. `run` is the escape hatch when a plugin needs a DLL
override or a registry key; `delete` asks before it does anything, and wants a `sync` after
it to unbridge what it held.

## Wine per plugin

Each prefix picks its own Wine, so a plugin that needs an old one does not hold the rest
back. Unpack any Wine build into
`~/.var/app/io.github.mark12870.cabinet/data/runners/<name>/` — anything with `bin/wine` in
it — and `cabinet runners` will list it. Prefixes that name none use the Wine in the Flatpak.

You will want this: **plugin editors are unusable on the bundled Wine 11.** Clicks land
offset by however far the window sits from the top-left of the screen, which is
[yabridge#382](https://github.com/robbert-vdh/yabridge/issues/382) and needs **Wine 9.21 or
older**. Give such a plugin a prefix on an older runner and its editor behaves.

A plugin shipping as a plain folder skips the installer: unpack it into the VST3 directory
`new` printed — or the `Program Files (x86)` half of the prefix, if it is 32-bit — and `sync`.

If a plugin does not show up, run `doctor` first — it reports which side is wrong.

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
