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

There is no setup step. Cabinet copies nothing onto your system — your DAW reads yabridge
straight out of the installed Flatpak — so updating Cabinet updates yabridge with nothing
further to run.

`enrol` links yabridge into the DAW's data directory, which is the one place that DAW's
chainloader looks, and then **prints** a `flatpak override` command for you to run. It is not
applied automatically — see [Permissions](#permissions).

## Use

```sh
cabinet=io.github.mark12870.cabinet

flatpak run $cabinet new serum                              # a prefix of its own
flatpak run $cabinet install serum ~/Downloads/Serum.exe    # run the installer in it
flatpak run $cabinet sync                                   # bridge what it installed
flatpak run $cabinet doctor                                 # check both sides
```

Plugins that ship as a plain folder rather than an installer can be unpacked straight into
`~/.var/app/io.github.mark12870.cabinet/data/prefixes/<name>/drive_c/Program Files/Common Files/VST3`,
then `sync`. `new` creates the conventional locations for VST3, VST2 and CLAP under both
`Program Files` and `Program Files (x86)`, and `sync` registers whichever ones exist — a
32-bit plugin belongs in the `(x86)` half.

`cabinet run <name> winecfg` (or `regedit`, or any command) is the escape hatch when a
plugin needs a DLL override or a registry key.

## How it works

yabridge is not one binary. The half your DAW loads — a chainloader and
`libyabridge-*.so` — has to run *outside* the sandbox, because the DAW is outside it. Only
Wine runs inside.

```
DAW  →  ~/.vst3/yabridge/Plugin.vst3        chainloader
     →  <cabinet>/files/lib/yabridge/…so    yabridge, in the DAW's process
     →  <cabinet>/files/…/cabinet-wine      the shim, at $WINELOADER
     →  flatpak run … cabinet → wine        the sandbox
     →  plugin.dll, in its own prefix
```

`<cabinet>` is the installed Flatpak itself —
`~/.local/share/flatpak/app/io.github.mark12870.cabinet/current/active`. Nothing is copied
out of it: `current/active` is an alias flatpak repoints on update, so the DAW always loads
the yabridge that shipped with the Cabinet you have. That is also why the DAW needs a
read-only grant on that directory — Flatpak masks `~/.local/share/flatpak` even under
`--filesystem=home`.

The shim is the whole boundary. `flatpak run` starts the sandbox with a clean environment
and a different mount namespace, so the shim forwards an explicit list of variables and
resolves every path with `realpath` first — Flatpak masks `~/.var/app/<other-app>` too, and
that is exactly where a Flatpak DAW's `XDG_DATA_HOME` points.

Prefixes are found by yabridge itself: it walks up from the plugin's `.dll` looking for a
`dosdevices` directory. Nothing has to be registered anywhere, and several prefixes work at
once.

## Permissions

Cabinet keeps everything it owns inside `~/.var/app/io.github.mark12870.cabinet/`, prefixes
included, and holds `--filesystem=home:ro` — it can read an installer you point it at, but
the only places it can write are `~/.vst3`, `~/.vst`, `~/.clap` and `~/.var/app`. The
trade-off is worth knowing: because prefixes are in its data directory,
`flatpak uninstall --delete-data` **will** take your plugin library with it.

Exactly two things end up outside that directory, and neither is avoidable:
`~/.vst3/yabridge/…`, because that is where DAWs scan for plugins; and a symlink at
`~/.var/app/<daw>/data/yabridge`, because the chainloader's search path is compiled in and
`$XDG_DATA_HOME/yabridge` is the only entry a sandboxed DAW can reach.

Bridging into a **Flatpak DAW** needs four things granted to that DAW, and one of them is
significant:

| Permission | Why |
| --- | --- |
| `--device=shm` | yabridge's audio buffers are `shm_open`. `--device=all` does *not* include `/dev/shm`. |
| `--filesystem=xdg-run/yabridge:create` | the socket directory, at the same path on both sides |
| `--filesystem=<cabinet>/files:ro` | read the chainloader out of Cabinet's install directory, which `home` does not cover |
| `--talk-name=org.freedesktop.Flatpak` | lets the shim reach the host to start Wine — **and lets that DAW run any command on your host** |

The last one is a real weakening of that DAW's sandbox. `enrol` prints it rather than
applying it, so the decision stays yours. Undo it with
`flatpak override --user --reset <daw-id>`.

## Rolling back

Releases stay in the published repo, so a bad update can be undone:

```sh
flatpak remote-info --log cabinet io.github.mark12870.cabinet
flatpak update --commit=<COMMIT> io.github.mark12870.cabinet
```

Everything is bundled in the commit, so any version still listed is still installable.
`flatpak mask io.github.mark12870.cabinet` holds a rollback until you unmask it.

## Known limitations

- **32-bit plugins** need the `org.freedesktop.Platform.Compat.i386` runtime, which the
  manifest declares; it is pulled in on install. Wine's Mono and Gecko are re-declared the
  same way, so installers needing .NET or an embedded browser work.
- **Uninstalling with `--delete-data` deletes your prefixes**, since they live in Cabinet's
  own data directory. Plain `flatpak uninstall` leaves them alone.
- **`RLIMIT_MEMLOCK`** is 8 MB on many systems, below what yabridge wants for locking its
  audio buffers into RAM. `doctor` warns about it and prints the fix: `DefaultLimitMEMLOCK=1G`
  under `[Manager]` in `/etc/systemd/user.conf.d/60-memlock.conf`. Not `limits.conf` —
  `pam_limits` does not reach anything the systemd user manager starts, which is every
  Flatpak DAW you launch from the desktop.
- Upstream yabridge does not support Flatpak DAWs. Cabinet makes it work, but that
  configuration is Cabinet's problem, not upstream's — please do not report it there.

## Building

```sh
flatpak run org.flatpak.Builder --repo=repo --force-clean \
  --default-branch=stable build io.github.mark12870.cabinet.yml
flatpak remote-add --user --no-gpg-verify cabinet-local "file://$PWD/repo"
flatpak install --user cabinet-local io.github.mark12870.cabinet
```

## License

GPL-3.0-or-later, matching yabridge. See [LICENSE](LICENSE).
