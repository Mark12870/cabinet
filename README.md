# Cabinet

Windows VST plugins on Linux as a Flatpak, aiming to work out of the box: install a plugin and it shows up in your DAW,
with no Wine or yabridge to set up. Plugins get **Wine prefixes of their own**, one per vendor or product family,
instead of a single prefix every installer fights over. Built for immutable systems like Fedora Silverblue. The bridging
is [yabridge](https://github.com/robbert-vdh/yabridge), with a few Cabinet patches, and the compatibility layer is
[Wine](https://www.winehq.org); Cabinet bundles both and wires the result to your DAW.

![Cabinet application preview](site/screenshots/cabinet.png)

## Yabridge patches

[PATCHES.md](PATCHES.md) explains each patch in more detail.

- **teardown-guard** (Splice crash on removal) — a plugin crashing while removed no longer takes the DAW down.
- **foreign-drag-drop** (WebView2 editors) — stops a Wine drag-and-drop bug crashing them when they open or close.
- **editor-window-origin** (VST2 editors) — keeps the editor in its window, so it shows and clicks land right.
- **chainloader-cabinet-first** (DAWs outside Flatpak) — loads Cabinet's own yabridge first, not another yabridge on
  the system.
- **state-per-interface** (Kontakt 8 fix) — projects saved on Windows reopen with their instrument instead of empty.

## Install

```sh
flatpak remote-add --if-not-exists cabinet \
  https://mark12870.github.io/cabinet/io.github.mark12870.cabinet.flatpakrepo
flatpak install cabinet io.github.mark12870.cabinet
```

**A DAW installed outside Flatpak needs nothing more.** Windows plugins land in `~/.vst3/cabinet/windows`, native ones
in `cabinet/native` (LV2 straight in `~/.lv2`), and the same under `~/.clap` and `~/.vst`, which such a DAW scans.

**A Flatpak DAW has to be enrolled first, once.** Its sandbox hides Cabinet's yabridge, your prefixes and the Wine that
runs them, so without this it sees no Windows plugins at all. Look up its id with `flatpak list --app`, then:

```sh
flatpak run io.github.mark12870.cabinet enrol fm.reaper.Reaper
```

or, in the window, *Doctor → Enrol a DAW*. It changes nothing: it **prints** a `flatpak override` for you to run (see
[Permissions](#permissions)) and a self-test for the DAW's runtime. Restart the DAW afterwards; if an update needs more
permissions, `sync`, the window and `doctor` say so, and `enrol` prints the new command.

Everything is in the window. For the command line, `flatpak run io.github.mark12870.cabinet --help` lists every command.

The Library installs without showing vendors' licences, so installing accepts their terms on your behalf; the window and
`library install` say whose before anything runs.

## Permissions

`enrol` prints the `flatpak override` rather than applying it, because the DAW then loads native plugins from
`~/.vst*`, `~/.clap` and `~/.lv2`, which the Windows code Cabinet runs can write to, unlike any other Flatpak app's
data. `--talk-name=io.github.mark12870.cabinet.Bridge` lets the shim start Cabinet's Wine, in Cabinet's sandbox, from
inside the DAW's. DAWs enrolled by earlier releases hold `--talk-name=org.freedesktop.Flatpak` instead, which still
works **but lets that DAW run any command on your host**; `doctor` names them.
`flatpak override --user --reset <daw-id>` undoes either. `flatpak uninstall --delete-data` **will** delete your
prefixes; after any uninstall, remove `~/.vst*/cabinet`, `~/.clap/cabinet` and the native plugins Cabinet put in
`~/.lv2` yourself.

## Building

```sh
flatpak run org.flatpak.Builder --repo=repo --force-clean --default-branch=stable build io.github.mark12870.cabinet.yml
flatpak remote-add --user --if-not-exists --no-gpg-verify cabinet-local "file://$PWD/repo"
flatpak install --user --or-update cabinet-local io.github.mark12870.cabinet
```

## License

GPL-3.0-or-later, matching yabridge. See [LICENSE](LICENSE). The app icon combines the *dresser* and *piano-keys* icons
from [Phosphor Icons](https://phosphoricons.com), recoloured — MIT, see [data/LICENSE.phosphor](data/LICENSE.phosphor).
