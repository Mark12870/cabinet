# CLAUDE.md

Wine packaged as a Flatpak so Windows VST plugins run on Fedora Silverblue, one Wine prefix
per plugin, bridged into a DAW by upstream yabridge. App ID `io.github.mark12870.cabinet`.
`x86_64` only — the maintainer runs one machine, so there is deliberately no arm build.

Packaging follows `../beeper-flatpak`: self-hosted GPG-signed OSTree repo on GitHub Pages,
daily version-bump workflow, same publishing gotchas.

## Code style

Keep it minimal and readable — no dead config, no scaffolding that isn't used. Comment only
what is non-obvious, and say *why*, not *what*. The same goes for prose: a doc fix should not
come out longer than what it replaced.

Languages are split on purpose: **Rust only for `shim/`**, C# for everything else including
any future GUI. The shim is Rust because it is exec'd on the plugin-load path inside foreign
sandboxes; nothing else has that constraint.

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

yabridge's DAW-side halves cannot live in the sandbox, because the DAW does not. `cabinet
setup` exports them to `~/.local/share/yabridge`; only Wine stays inside. The crossing
happens at `$WINELOADER`, which yabridge's winegcc wrapper execs — upstream supports this
explicitly through `YABRIDGE_TEMP_DIR` and `YABRIDGE_NO_WATCHDOG`. Prefixes need no
registration: yabridge walks up from the plugin `.dll` for a `dosdevices` directory.

## Gotchas

These were all found by something failing, not by reading documentation.

- **`--device=all` does not include `/dev/shm`.** yabridge's audio buffers are `shm_open`,
  so `--device=shm` is required on Cabinet *and* on every bridged DAW. A sandbox without it
  sees an empty `/dev/shm` while the host has entries — that is the check `doctor` makes.
- **Flatpak masks `~/.var/app/<other-app>` even under `--filesystem=home`.** The chainloader
  resolves yabridge through the DAW's `$XDG_DATA_HOME`, which for a Flatpak DAW is exactly
  that masked path, so Wine gets handed a path that does not exist on its side and fails with
  `failed to open …yabridge-host.exe.so`. **The shim `realpath`s everything it forwards.**
  Do not "simplify" that away. An explicit `--filesystem=~/.var/app` is honoured, which is
  how `enrol` writes the link at all.
- **`~/.local/share/flatpak` is masked too**, so `doctor` reads override files through an
  explicit `--filesystem=~/.local/share/flatpak/overrides:ro`.
- **`base:` copies the base app's files but not its extension declarations.** Inheriting
  multilib Wine this way loses `org.freedesktop.Platform.Compat.i386` and it dies on
  `/lib/ld-linux.so.2: could not open`. The manifest re-declares them. Wine's Mono and Gecko
  are lost the same way and are *not* re-declared — they are extensions of another app.
- **`org.winehq.Wine` bakes `WINEPREFIX=/var/data/wine` into its metadata.** Always pass an
  explicit prefix rather than assuming an unset one.
- **`stable-25.08`, not `wow64-25.08`.** yabridge's 32-bit host is a 32-bit *winelib* binary,
  which new WoW64 cannot run — that would silently drop most of the older VST2 catalogue.
- **The shim is not statically linked**, though it should be: the freedesktop SDK ships no
  static libc and the rust-stable extension carries only gnu targets, which flatpak-builder
  cannot extend offline. `--cabinet-self-test` exists so a DAW on an older runtime produces a
  legible error instead of a plugin that silently will not scan.
- **`nuget-sources.json` must be regenerated whenever the C# packages move**, with
  `flatpak-dotnet-generator.py` from `flatpak/flatpak-builder-tools` (the `flatpak` org, not
  `flathub`). It is required even with zero third-party packages, because NativeAOT pulls
  ILCompiler from NuGet. The Rust side deliberately has no equivalent: keep the shim
  dependency-free and there is nothing to vendor.
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
