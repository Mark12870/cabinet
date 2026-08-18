# CLAUDE.md

Wine packaged as a Flatpak so Windows VST plugins run on Fedora Silverblue, one Wine prefix
per plugin, bridged into a DAW by upstream yabridge. App ID `io.github.mark12870.cabinet`.
`x86_64` only — the maintainer runs one machine, so there is deliberately no arm build.

Packaging follows `../beeper-flatpak`: self-hosted GPG-signed OSTree repo on GitHub Pages,
daily version-bump workflow, same publishing gotchas.

## Code style

Keep it minimal and readable — no dead config, no scaffolding that isn't used. The same goes
for prose: a doc fix should not come out longer than what it replaced. Follow the basic Clean
Code rules.

**The code carries no comments.** Not XML doc tags, not a header on every member, and above
all not a note arguing for why the code is written the way it is — that is a message to a
reviewer, not to whoever maintains this next. Say it in the name or the structure instead.
Anything that genuinely will not fit there is a *gotcha*, and gotchas live under Gotchas
below, where they are findable; what was learned on the way belongs in the commit message.

Languages are split on purpose: **Rust only for `shim/`**, C# for everything else including
the GUI. The shim is Rust because it is exec'd on the plugin-load path inside foreign
sandboxes; nothing else has that constraint.

**Every feature reaches both front ends.** An operation belongs to `Cabinet.Core`; the CLI
and the GUI are two renderings of it, and one that lands in only one of them is unfinished.
Neither front end may hold logic of its own — that is why enrolment's symlink lives in
`Enrolment.Link` rather than in the CLI's `Enrol`, where it started. Adding a capability
means a verb in `Cabinet.Cli` *and* an affordance in `Cabinet.Gui`, in the same change.

`TODO.md` is the live list of what is unverified, untested or deferred — read it before
picking up work. **It only ever shrinks.** Remove what is finished; do not add items, and do
not keep a "done" section. What was learned on the way belongs in the commit message, or here
under Gotchas if it will bite again.

## Layout

- `shim/` — `cabinet-wine`, the `$WINELOADER` shim. Std-only, no dependencies.
- `src/Cabinet.Core/` — every operation, as a library. Both front ends reference this.
- `src/Cabinet.Cli/` — argument parsing and rendering only, NativeAOT.
- `src/Cabinet.Gui/` — GTK4 + libadwaita through GirCore. Trimmed, not NativeAOT.
- `data/` — the app icon: Phosphor's *dresser* duotone, recoloured, MIT, keep the licence
  beside it and the credit in the README.
- `scripts/`, `site/`, `.github/workflows/` — packaging and publishing.

## Build and test

```sh
# Neither toolchain exists on a Silverblue host; both come from SDK extensions.
# org.gnome.Sdk//50 carries the same org.freedesktop.Sdk.Extension branch (25.08).
flatpak run --share=network --filesystem="$PWD" --command=sh org.gnome.Sdk//50 -c '
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
- **A prefix picks its own Wine, and the shim resolves it from `WINEPREFIX` alone.** `runners/`
  is a *sibling* of `prefixes/`, so `<prefix>/../../runners/<name>/bin/wine` is the entire
  lookup — moving either directory breaks the plugin-load path. The choice lives in
  `<prefix>/.cabinet-runner` rather than a config file of Cabinet's because an enrolled DAW is
  granted the prefixes directory and nothing else, and for the same reason the shim never
  stats the result: that would fail for a runner which works fine on the other side. A runner
  is just a directory with `bin/wine` in it, so unpacking a tarball is the whole install, and
  its `wineboot`/`winecfg` must be run from that same `bin/` — `PATH` still points at the
  bundled Wine. Create a prefix on the runner it will keep; `wineboot` writes a registry the
  next Wine inherits, and Wine refuses a prefix another `wineserver` still holds, so close the
  DAW before moving one.
- **Runners carry no Mono or Gecko, and Wine will ask the user to download them.** Those come
  from Flatpak extensions mounted at `/app/share/wine/{mono,gecko}`, which only the bundled
  Wine has, so `Runners` symlinks them into every runner it unpacks. Cabinet's own `wineboot`
  also runs with `WINEDLLOVERRIDES=mscoree=d;mshtml=d` so creating a prefix never blocks on a
  dialog — scoped to `wineboot`, so an installer that really wants .NET still gets it.
- **`wine-<v>-staging-amd64` has no fsync; `-staging-tkg-amd64` does.** Measured, not assumed:
  `WINEDEBUG=+fsync` produced nothing on plain Staging and 4685 lines on a TkG build, and the
  TkG one reports `( TkG Staging Esync Fsync )`. yabridge calls an fsync build *"the most
  important thing you can do"* and `WINEFSYNC` is forwarded by the shim for it, so
  `runners install` takes the TkG asset. Never `-wow64` or `-x86`: yabridge's 32-bit host is a
  32-bit winelib binary that new WoW64 cannot run, which is why `Runners` rejects an archive
  with no 32-bit tree.
- **yabridge 5.1.1's plugin editors need Wine 9.21 or older.** From 9.22 on, clicks land offset
  by the window's distance from the screen origin —
  [yabridge#382](https://github.com/robbert-vdh/yabridge/issues/382), which upstream
  acknowledged and has not fixed. The bundled Wine is 11.0, so any prefix with a usable editor
  wants `cabinet runners install 9.21`. That fixes VST2 outright — verified on Aalto. **VST3
  needs two more things**, neither of them Cabinet's: `editor_disable_host_scaling = true`
  under `["*"]` in `~/.vst3/yabridge/yabridge.toml` before clicks land at all. The repaint
  failure seen alongside it was a separate fault with its own fix — DXVK, below.
- **A JUCE editor that never repaints wants DXVK in its prefix.** Surge XT drew once and then
  only when the window moved. The fix is DXVK's `d3d11`, `dxgi`, `d3d10core` and `d3d9` copied
  into `system32` and `syswow64` and set to `native`, which is what `cabinet dxvk <prefix>`
  does — measured working: `DXVK: v2.7.1`,
  `Found device: AMD Radeon RX 5700 XT (RADV NAVI10)`, `D3D11InternalCreateDevice`. This is
  yabridge's own answer to that symptom (yabridge#212, #270, #384) and it needs a Vulkan driver
  reachable from inside the sandbox: `--device=dri` is already in the manifest, and the 32-bit
  ICD arrives with `org.freedesktop.Platform.GL32.default`.
- **surge#7636's recipe is wrong for a bridged plugin, in both directions.** It prescribes
  `WINEDLLOVERRIDES="d3dcompiler_47=n;dxgi=n,b"` with Wine 10.8, and explicitly *no* DXVK. The
  override does bite — `Library d3dcompiler_47.dll (which is needed by d2d1.dll) not found`,
  so Direct2D cannot load — and the editor stays frozen on 9.21 and on 10.8 alike, frozen even
  while a note sounds and nothing is being clicked. Worse, it blocks the very path DXVK
  accelerates. Wine 10.8 also costs what yabridge#382 says it costs: the pointer arrives at
  (2473, 443) in a 2077×1294 client area, past the right edge, so nothing is clickable at all.
  That advice is for bare Wine; under yabridge, DXVK is the fix and 9.21 is still the runner.
  Per-prefix overrides go in the prefix's own `HKCU\Software\Wine\DllOverrides`, written with
  `cabinet run <prefix> wine reg add` — the environment variable the issue quotes would have to
  be set on the DAW, which is every prefix at once.
- **Nothing in Cabinet writes `yabridge.toml`, and no experiment may leave one behind.** It is
  the user's file, it is not tracked here, and a stale one is invisible: yabridge only mentions
  it as `config from: …` deep in a debug log. One left over from an earlier debugging session
  cost real time in the investigation that found
  [yabridge#382](https://github.com/robbert-vdh/yabridge/issues/382) — checking for it was what
  finally ruled it out. If a session writes one to test an option, delete it in the same
  session and record the option here instead.
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
- **Plugin editors get absolute screen coordinates where they expect client-relative ones.**
  Clicks are delivered and dispatched correctly — they just land offset by exactly the plugin
  window's distance from the screen origin. Measured by clicking one corner and moving the
  window: reported `(642,254)` at screen `(641,253)`, `(0,66)` once dragged to the top left.
  Reproduces on Aalto (VST2 x64), Dexed (VST3 x64) and Surge XT (VST3 x86), and
  `editor_xembed = true` does not help. **Not Cabinet's**: the clicks cross the sandbox and
  reach the right window, and it is not
  [yabridge#506](https://github.com/robbert-vdh/yabridge/issues/506) either — there
  `WM_LBUTTONDOWN` never reaches a window procedure, here it does with a wrong lParam.
  Evidence in `~/cabinet-editor-coordinates-evidence.log`.

  A static UI hides this completely: Dexed and Surge look frozen — for their own reason, a
  missing DXVK — while Aalto's animated graphs make it obvious that drawing works and only
  input is misplaced. That is what made
  this so hard to see — **an earlier investigation blamed Surge**
  ([surge#7636](https://github.com/surge-synthesizer/surge/issues/7636), a real but separate
  repaint bug, closed upstream on a workaround that does not survive yabridge — above)
  **and wrongly recorded that Dexed was unaffected.** Before that it blamed GNOME
  focus-stealing, the embedding handshake, Wine's spinning `mmdevapi_midi_n` thread and
  fractional scaling. Six wrong answers, all reached by theorising. What finally
  worked was two commands: `YABRIDGE_DEBUG_LEVEL=1+editor` for the embedding, and
  `WINEDEBUG=+msg,+event` to decode the lParam of the delivered click. Measure the coordinate
  before proposing anything.
- **The runtime is GNOME, not freedesktop, and that is the GUI's doing.**
  `org.freedesktop.Platform//25.08` carries GTK3 but no GTK4 and no libadwaita, so a native
  GNOME app cannot be built on it. `org.gnome.Platform//50` carries both and keeps the
  `lib/i386-linux-gnu` mount point Wine's 32-bit tree needs. This is safe because it is
  exactly what Bottles ships — `runtime=org.gnome.Platform/x86_64/49` over
  `base=app/org.winehq.Wine/x86_64/stable-25.08`, the same base Cabinet uses. The three SDK
  extensions do **not** move: `org.gnome.Sdk//49` and `//50` both declare
  `[Extension org.freedesktop.Sdk.Extension]` at `version = 25.08`, which is the branch
  `dotnet10`, `llvm20` and `rust-stable` are already pinned to. Verified by running
  `flatpak remote-info --show-metadata`, not by reading documentation.
- **The GUI is GirCore, and it is deliberately not NativeAOT.** GirCore 0.8.1 binds GTK 4.22
  and libadwaita 1.9 and never claims NativeAOT support, so `Cabinet.Gui` publishes
  self-contained and trimmed while `Cabinet.Cli` keeps `PublishAot`. Measured, not assumed: a
  trimmed hello-world `Adw.ApplicationWindow` built in `org.gnome.Sdk//50` and presented a
  window under `org.gnome.Platform//50`, at 24 MB for the whole publish directory. Avalonia
  was the alternative and was rejected because it draws its own widgets with Skia — it would
  not inherit Adwaita's accent colour, font or dark-mode preference. GirCore's 14 packages are
  managed P/Invoke wrappers with no native blobs, so `nuget-sources.json` went from 6 entries
  to 20 rather than pulling in Skia and HarfBuzz binaries.
- **Cabinet now holds a Wayland socket, and Wine must never see it.** The GUI is GTK4 and
  wants Wayland; yabridge's editor embedding is built on X11. `Prefixes.Wine` therefore passes
  `WAYLAND_DISPLAY` as an empty string, and **`ProcessRunner` removes any variable whose value
  is empty** rather than setting it blank — blanking it would make Wine fail a connection to a
  socket named `""` instead of skipping Wayland. Do not "simplify" that rule away; it is the
  only thing keeping `--socket=wayland` from reaching Wine.
- **`IProcessRunner` streams, and that replaced the old `inherit` flag.** `inherit: true` handed
  the child the parent's stdout/stderr and returned an empty `ProcessResult` — invisible to a
  GUI, which has no console. The interface now takes an `Action<string>? onOutput` sink that
  is called per line while the process runs, and still collects the full output. The CLI
  passes `Console.WriteLine`, so its behaviour is unchanged; the GUI appends to a text view.
  One consequence: output is line-buffered now, so a child that draws progress with `\r` and
  no newline will not render the same way it did.
- **`Enrolment` creates the symlink; it did not used to.** The side effect lived inline in the
  CLI's `Enrol`, which meant a second front end would have had to copy it. `Enrolment.Link`
  now owns it and both call it. `Enrolment` still only *prints* the `flatpak override` — the
  GUI must not run it either, for the reason the README gives.
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

## Palette

The preferred colours for anything that has to choose its own:

```
espresso      #663322      
amber-flame   #ffbb00      
jungle-teal   #227d66
pale-sky      #bfdbf7      
watermelon    #ee4266
```

**GNOME's colours win wherever GNOME has one.** The GUI follows the user's system accent,
and `success`, `warning` and `error` are Adwaita style classes rather than hexes from here —
a `data/style.css` mapping these onto `--accent-bg-color` was written and removed, because
overriding the accent someone chose is the one case this rule excludes. Use the palette
where there is no GNOME colour to inherit: the app icon (espresso and amber-flame today),
the site, a diagram, anything drawn rather than themed.

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
