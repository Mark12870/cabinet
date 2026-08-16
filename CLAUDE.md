# CLAUDE.md

Wine packaged as a Flatpak so Windows VST plugins run on Fedora Silverblue, one Wine prefix
per plugin, bridged into a DAW by upstream yabridge. App ID `io.github.mark12870.cabinet`.
`x86_64` only — the maintainer runs one machine, so there is deliberately no arm build.

Packaging follows `../beeper-flatpak`: self-hosted GPG-signed OSTree repo on GitHub Pages,
daily version-bump workflow, same publishing gotchas.

## Code style

Keep it minimal and readable — no dead config, no scaffolding that isn't used. Comment only
what is non-obvious, and say *why*, not *what*. The same goes for prose: a doc fix should not
come out longer than what it replaced. Follow the basic Clean Code rules.

Languages are split on purpose: **Rust only for `shim/`**, C# for everything else including
any future GUI. The shim is Rust because it is exec'd on the plugin-load path inside foreign
sandboxes; nothing else has that constraint.

`TODO.md` is the live list of what is unverified, untested or deferred — read it before
picking up work. **It only ever shrinks.** Remove what is finished; do not add items, and do
not keep a "done" section. What was learned on the way belongs in the commit message, or here
under Gotchas if it will bite again.

## Layout

- `shim/` — `cabinet-wine`, the `$WINELOADER` shim. Std-only, no dependencies.
- `src/Cabinet.Core/` — every operation, as a library. A future Avalonia GUI references this.
- `src/Cabinet.Cli/` — argument parsing and rendering only, NativeAOT.
- `scripts/`, `site/`, `.github/workflows/` — packaging and publishing.

## Build and test

```sh
# Neither toolchain exists on a Silverblue host; both come from SDK extensions.
flatpak run --share=network --filesystem="$PWD" --command=sh org.freedesktop.Sdk//25.08 -c '
  . /usr/lib/sdk/dotnet10/enable.sh
  export PATH=/usr/lib/sdk/rust-stable/bin:/usr/lib/sdk/llvm20/bin:$PATH
  dotnet test tests/Cabinet.Core.Tests && (cd shim && cargo test && cargo clippy -- -D warnings)'

flatpak run org.flatpak.Builder --repo=repo --force-clean --disable-rofiles-fuse \
  --default-branch=stable build io.github.mark12870.cabinet.yml
flatpak install --user cabinet-local io.github.mark12870.cabinet   # remote: file://$PWD/repo
```

## The architecture, in one paragraph

yabridge's DAW-side halves cannot live in the sandbox, because the DAW does not — but they
are not copied out either: the DAW reads them from the installed Flatpak's own
`current/active/files`, granted read-only by `enrol`'s override. Only Wine runs inside. There
is **no `setup` command**; `Bootstrap.Ensure` makes the one link Cabinet needs on every
invocation. The crossing happens at `$WINELOADER`, which yabridge's winegcc wrapper execs —
upstream supports this through `YABRIDGE_TEMP_DIR` and `YABRIDGE_NO_WATCHDOG`. Prefixes need
no registration: yabridge walks up from the plugin `.dll` for a `dosdevices` directory.

**Everything Cabinet owns stays in `~/.var/app/io.github.mark12870.cabinet/`, prefixes
included** — the Bottles model, and the standard to hold new code to. Two paths outside it
are unavoidable: `~/.vst3/yabridge/…` (where DAWs scan) and `~/.var/app/<daw>/data/yabridge`
(the chainloader's compiled-in search path). Anything else in `$HOME` is a bug.

## Gotchas

These were all found by something failing, not by reading documentation.

- **`--device=all` does not include `/dev/shm`.** yabridge's audio buffers are `shm_open`,
  so `--device=shm` is required on Cabinet *and* on every bridged DAW. A sandbox without it
  sees an empty `/dev/shm` while the host has entries — that is the check `doctor` makes.
- **Flatpak masks `~/.var/app/<other-app>` even under `--filesystem=home`.** The chainloader
  resolves yabridge through the DAW's `$XDG_DATA_HOME`, which is exactly that masked path, so
  Wine is handed a path that does not exist on its side and fails with
  `failed to open …yabridge-host.exe.so`. **The shim `realpath`s everything it forwards** — do
  not "simplify" that away. An explicit `--filesystem=~/.var/app` is honoured.
- **`~/.local/share/flatpak` is masked too**, so `doctor` reads override files through an
  explicit `--filesystem=~/.local/share/flatpak/overrides:ro`.
- **A DAW cannot reach prefixes in `~/.var/app/<cabinet>` by default.** yabridgectl's bundles
  symlink into the prefix and libyabridge walks up for `dosdevices`, both in the *DAW's*
  process. REAPER reported only "failed to scan". `enrol` grants `--filesystem=<prefixes>:ro`;
  anything that moves the prefixes must move that grant too.
- **`base:` copies the base app's files but not its extension declarations.** Inheriting
  multilib Wine loses `org.freedesktop.Platform.Compat.i386` and it dies on
  `/lib/ld-linux.so.2: could not open`. The manifest re-declares them, *including*
  `org.winehq.Wine.gecko` and `.mono` — another app's extensions are re-declarable, as
  Bottles does against the same base.
- **`org.winehq.Wine` bakes `WINEPREFIX=/var/data/wine` into its metadata.** Always pass an
  explicit prefix rather than assuming an unset one.
- **`yabridgectl set` panics in 5.1.1**, on every invocation: `--path-auto` is declared as
  taking a value and then read as a flag, so clap aborts and `--path` is unreachable.
  `Bootstrap` symlinks Cabinet's own `$XDG_DATA_HOME/yabridge` instead. Re-check on the next
  yabridge bump.
- **A DAW's `WINELOADER` reaches Cabinet through `flatpak run`**, so anything Cabinet starts
  would exec the shim and re-enter its own sandbox. Every Wine invocation pins
  `WINELOADER=/app/bin/wine`.
- **`app-path` in `/.flatpak-info` is content-addressed**, so it must never be baked into a
  DAW's override. `Layout.StableAlias` rewrites it to the `current/active` alias.
- **`--filesystem=home:ro` plus `--filesystem=~/.vst3:create` works**: the specific grant
  wins. Verified, not assumed.
- **`File.ResolveLinkTarget` throws when the path does not exist** — the normal first run.
  `new DirectoryInfo(p).LinkTarget` answers `null` instead.
- **Surge XT's editor does not repaint under Wine — it is a Surge bug, not ours.**
  [surge#7636](https://github.com/surge-synthesizer/surge/issues/7636), open since May 2024,
  reproduces in FL Studio and in Surge's *standalone* build, so neither yabridge nor a DAW is
  involved. It repaints only when something external pokes it. **Do not test editor embedding
  with Surge XT**; Dexed is bridged for that. A long investigation blamed GNOME
  focus-stealing, the embedding handshake, Wine's spinning `mmdevapi_midi_n` thread and
  fractional scaling in turn, and all four were wrong — read a
  `YABRIDGE_DEBUG_LEVEL=2+editor` log before theorising, and check upstream's issue tracker
  for the *plugin* early. Evidence in `~/cabinet-editor-embedding-evidence.log`.
- **`stable-25.08`, not `wow64-25.08`.** yabridge's 32-bit host is a 32-bit *winelib* binary,
  which new WoW64 cannot run — that would silently drop most of the older VST2 catalogue.
- **The shim is not statically linked**, though it should be: the freedesktop SDK ships no
  static libc and rust-stable carries only gnu targets. `--cabinet-self-test` exists so a DAW
  on an older runtime gives a legible error instead of a plugin that will not scan.
- **`nuget-sources.json` must be regenerated whenever the C# packages move**, with
  `flatpak-dotnet-generator.py` from `flatpak/flatpak-builder-tools` (the `flatpak` org, not
  `flathub`). Required even with zero third-party packages: NativeAOT pulls ILCompiler from
  NuGet. Keep the shim dependency-free and the Rust side needs no equivalent.
- **Never use `flatpak-builder --install`**, and never re-add `--generate-static-deltas` — see
  `../beeper-flatpak/CLAUDE.md`, whose publishing gotchas all apply here unchanged.
- Fedora's system `flathub` remote is filtered; add a user-scoped one to install SDKs.

## Signing

Cabinet signs with its own key, **not** beeper-flatpak's:

```
ed25519  5C6EC25CCC962DA9B07F5944995E2BEBE73034E6  Cabinet Flatpak (mark12870)
```

Separate on purpose. One key across both repos would mean one revocation breaks both, and
beeper's uid reads "Beeper Flatpak", which would be visibly wrong on Cabinet's site.

The key has **no passphrase**, so only `GPG_PRIVATE_KEY` is set on the repo and the
workflow's `GPG_PASSPHRASE` path stays unused. It has no expiry either, and that is not an
oversight: flatpak pins the key when a client adds the remote, so replacing it forces every
installed user to remove and re-add the remote. Treat it as permanent, and keep it backed up
accordingly.

## Versioning

A release is named after the yabridge it carries, and `scripts/update-version.py` is what
bumps it — it rewrites the manifest's url/sha256 and prepends a `<release>` to the metainfo.
`render-site.py` reads that release back out of each published commit to build the table, so
the two have to stay in step.

**Bumping is manual, on purpose.** Unlike beeper-flatpak there is no daily workflow chasing
upstream: yabridge releases rarely, and an unattended rebuild would publish a commit every
installed client sees as an update. Run the script, check the diff, commit:

```sh
python3 scripts/update-version.py   # prints updated=true/false
```

A push touching the manifest triggers `build-publish.yml`; otherwise run it from the Actions
tab via `workflow_dispatch`.
