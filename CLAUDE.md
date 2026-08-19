# CLAUDE.md

Wine packaged as a Flatpak so Windows VST plugins run on Fedora Silverblue, one Wine prefix
per plugin, bridged into a DAW by upstream yabridge. App ID `io.github.mark12870.cabinet`.
`x86_64` only — the maintainer runs one machine, so there is deliberately no arm build.

Packaging follows `../beeper-flatpak`: self-hosted GPG-signed OSTree repo on GitHub Pages,
daily version-bump workflow, same publishing gotchas.

## Important

Don't create a new branch if not asked for that!

**Every feature reaches both front ends.** An operation belongs to `Cabinet.Core`; the CLI
and the GUI are two renderings of it, and one that lands in only one of them is unfinished.
Neither front end may hold logic of its own — that is why enrolment's symlink lives in
`Enrolment.Link` rather than in the CLI's `Enrol`, where it started. Adding a capability
means a verb in `Cabinet.Cli` *and* an affordance in `Cabinet.Gui`, in the same change.

### Code style

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

## Layout

- `shim/` — `cabinet-wine`, the `$WINELOADER` shim. Std-only, no dependencies.
- `src/Cabinet.Core/` — every operation, as a library. Both front ends reference this.
- `src/Cabinet.Cli/` — argument parsing and rendering only, NativeAOT.
- `src/Cabinet.Gui/` — GTK4 + libadwaita through GirCore. Trimmed, not NativeAOT.
- `data/` — the app icon: Phosphor's *dresser* duotone, recoloured, MIT, keep the licence
  beside it and the credit in the README.
- `scripts/`, `site/`, `.github/workflows/` — packaging and publishing.
- `.claude/skills/` — `gui-shot` for looking at the GUI, `releasing` for publishing one.

## Build and test

`scripts/checks.sh` is exactly what `.github/workflows/checks.yml` runs, and the two
formatters are the half that is easy to forget — `dotnet format` failed CI on a four-space
overhang no test could catch. Keep the two lists identical: a check that only CI runs is a
check that only fails after a push, which is why the script exists rather than a block to
copy. Both toolchains come from SDK extensions the script enters on its own.

`.githooks/pre-commit` runs it as `--staged`: it reformats the staged code and stages the
fix, skips the halves nothing is staged for, and skips `dotnet test` to stay under about
seven seconds. A file that is only partly staged is reformatted but *not* staged, and the
commit stops so the diff can be looked at. Enable the hook once per clone:

```sh
git config core.hooksPath .githooks

scripts/checks.sh            # everything CI runs, tests included

# The GUI needs the compiler server off, or every GirCore assembly comes back as CS0006.
dotnet build src/Cabinet.Gui -p:UseSharedCompilation=false

flatpak run org.flatpak.Builder --repo=repo --force-clean --disable-rofiles-fuse \
  --default-branch=stable build io.github.mark12870.cabinet.yml
flatpak install --user cabinet-local io.github.mark12870.cabinet   # remote: file://$PWD/repo

# Look at a page of the installed GUI. Needs the toolbox its header describes, once.
scripts/gui-shot.sh About about.png
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

### The sandbox and its masks

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
- **A DAW's `WINELOADER` reaches Cabinet through `flatpak run`**, so anything Cabinet starts
  would exec the shim and re-enter its own sandbox. Every Wine invocation pins
  `WINELOADER=/app/bin/wine`.
- **`app-path` in `/.flatpak-info` is content-addressed**, so it must never be baked into a
  DAW's override. `Layout.StableAlias` rewrites it to the `current/active` alias.
- **`--filesystem=home:ro` plus `--filesystem=~/.vst3:create` works**: the specific grant
  wins. Verified, not assumed.
- **`/.flatpak-info` does not say which remote the app came from.** The origin is the first
  NUL-terminated string of the deploy GVariant at `<app-dir>/current/active/deploy`, which the
  existing `--filesystem=~/.local/share/flatpak/app/<id>:ro` already reaches. The name alone
  cannot tell a published build from a local one — the user chooses it — so `about` also reads
  the remote's `url` from `<install-root>/repo/config`, which needs its own
  `--filesystem=…/repo/config:ro`. A **single-file** grant is honoured through the mask over
  `~/.local/share/flatpak`; verified by reading it from inside, not assumed. `file://` is the
  local build, any other scheme the published one, and a url that cannot be read is *unknown*
  rather than local — an install predating the grant must not be mislabelled.

### The manifest and the runtime

- **`base:` copies the base app's files but not its extension declarations.** Inheriting
  multilib Wine loses `org.freedesktop.Platform.Compat.i386` and it dies on
  `/lib/ld-linux.so.2: could not open`. The manifest re-declares them, *including*
  `org.winehq.Wine.gecko` and `.mono` — another app's extensions are re-declarable, as
  Bottles does against the same base.
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
- **The running build reads its own version back out of `/app/share/metainfo/`.** That keeps the
  metainfo the one place the version lives; nothing goes into a csproj `Version`, which would be
  a second copy that a `<release>` bump could not reach.

### Wine, prefixes and runners

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
- **The versions list must not touch `api.github.com`.** Unauthenticated callers get 60 requests
  an hour *per address*, shared with everything else on the machine, and each dialog opening
  spent two of them; the list then failed with `could not reach …`, which was a lie — GitHub
  answered, with 403. The list now reads Bottles' own source, the generated index
  `bottlesdevs/components/main/index.yml` over `raw.githubusercontent.com`, which has no such
  cap, and `Fetch` reports the three cases apart: curl's exit for a transport failure, the
  status off `-D /dev/stderr` for an HTTP one, the body otherwise. `scripts/update-yabridge.py`
  stays on the API on purpose — one call, run by hand.
- **That index names the family first, so a prefix alone no longer filters it.** `soda-` matches
  `soda-mcsoda-11.0-4` and `soda-experimental_8.0`; what rejects them is `VersionOf` requiring
  the remainder to start with a digit, and `ReleasesFrom` also drops the `unstable` channel.
  Kron4ek needs its `-staging-tkg-amd64` suffix too, which is what keeps `kron4ek-wine-proton-*`
  out. The list beats the feed it replaced — 114 Kron4ek builds against a 100-release page —
  and reaches back far enough for `runners install 9.21`.
- **`index.yml` carries no download URL; the per-runner manifest does.** `runners/wine/<name>.yml`
  is fetched at install time only, for its `url` and its `file_checksum` — **MD5**, verified
  against the real `soda-11.0-5` archive. Soda is checked now, where it used to be taken on the
  strength of its https download alone; Kron4ek keeps its stronger `sha256sums.txt`. Soda 11.0-5
  is wine-tkg over Valve's experimental Wine with a full old-style 32-bit tree, so it clears the
  same bar the TkG asset does — except for fsync. **Soda's own `wine --version` says
  `( TkG Plain )`** where both Kron4ek builds say `( TkG Staging Esync Fsync )`, so its tkg
  config does not turn them on; whether Valve's base carries fsync of its own is still
  unmeasured. The Runners page shows that string, which is how it was noticed.
- **A per-prefix setting has to be added on both sides, or it only half exists.** Cabinet's own
  Wine goes through `Prefixes.Wine`; the *plugin-load* path goes through the Rust shim, and that
  is the one that matters. So `.cabinet-sync` and `.cabinet-env` are parsed twice, in
  `PrefixSettings` and in `shim/src/main.rs`, exactly as `.cabinet-runner` already was. A DLL
  override needs none of that — it lives in the prefix's own registry, so both paths get it for
  free, which is why `PrefixRegistry` writes there rather than inventing a third marker file.
  The shim emits its `--env=` flags *after* the `FORWARD` loop so a prefix wins over the DAW,
  and refuses the four keys Cabinet owns so an env file cannot break the crossing.
- **Sync `system` means inherit, not off.** It emits nothing at all, so the shim keeps relaying
  whatever `WINEFSYNC` the DAW was launched with — the behaviour before any of this existed.
  The other three modes set their own variable to `1` **and the other two to `0`**, because a
  half-set choice would silently lose to a DAW that exports `WINEFSYNC=1`. Defaulting new
  prefixes to fsync was rejected: Soda reports `( TkG Plain )` and has none.
- **`File.ResolveLinkTarget` throws when the path does not exist** — the normal first run.
  `new DirectoryInfo(p).LinkTarget` answers `null` instead.

### DXVK and plugin editors

- **yabridge 5.1.1's plugin editors need Wine 9.21 or older.** From 9.22 on, clicks land offset
  by the window's distance from the screen origin —
  [yabridge#382](https://github.com/robbert-vdh/yabridge/issues/382), which upstream
  acknowledged and has not fixed. The bundled Wine is 11.0, so any prefix with a usable editor
  wants `cabinet runners install 9.21`. That fixes VST2 outright — verified on Aalto. **VST3
  needs two more things**, neither of them Cabinet's: `editor_disable_host_scaling = true`
  under `["*"]` in `~/.vst3/yabridge/yabridge.toml` before clicks land at all. The repaint
  failure seen alongside it was a separate fault with its own fix — DXVK, below. **The
  versions list says none of this on purpose**: flagging every release from 9.22 on read as
  Cabinet grading upstream Wine. Here is where it lives.
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
- **DXVK overwrote Wine's own `d3d*`/`dxgi` in place, so the switch needs backups.** `Install`
  moves each replaced DLL to `<prefix>/.cabinet-dxvk-backup/{system32,syswow64}/` first, and
  `Remove` moves it back. `wineboot -u` alone is not enough to undo an install: it will not
  replace a DLL whose version resource is newer than the runner's, which DXVK's are.
  **A missing backup does not mean Wine had no such DLL** — that was assumed, and it destroyed
  a real prefix: `aalto` was DXVK'd before backups existed, so turning the switch off deleted
  all five outright and left `system32` with no Direct3D at all. `Remove` now reports whether
  every library came back and runs `wineboot -u` when one did not, which does restore an
  *absent* DLL. Any prefix predating the backup directory takes that path.
- **`Remove` had to be told DXVK was ever installed, or it deleted Wine's own Direct3D.**
  `Restore` treats every `d3d*`/`dxgi` in the prefix as DXVK's, so with no backup beside it the
  DLL was deleted as if it were. On a prefix DXVK was already *off* in, that is Wine's own copy:
  `cabinet set aalto dxvk off` twice, measured, left all ten missing. The recovery never ran
  either, because **`wine reg delete` exits 1 for a value that is not there** (`reg: Unable to
  find the specified registry value`) and every `reg` call was checked, so the second `off`
  threw on `d3d8` before reaching `wineboot -u`. That is the original aalto destruction reached
  by a second route. `Remove` now refuses a prefix whose marker is absent, `Unset` ignores a
  delete for an override Wine has already forgotten, and the marker is deleted *after* the
  recovery rather than before — a prefix that `wineboot` could not fix must not read as
  DXVK-free.
- **A DLL-override editor was built and then removed, and the registry is why.** The registry is
  the truth for *Wine* but it is not a list of anyone's decisions: `aalto` carried 29 overrides
  nobody typed — `msvcp*`, `ucrtbase`, `atl*`, `api-ms-win-crt-*` — written by the VC++
  redistributable, because setting `native,builtin` is *how* a dependency makes Wine load what it
  copied in. Installing a dependency and adding an override are one mechanism, so they cannot be
  told apart afterwards; showing the registry showed noise, and Cabinet had to keep its own list
  to show anything useful. Wine also has **no path field** — an override is a load order for a
  module name, nothing more — so "point at a DLL" cannot be expressed without copying the file in
  first, which is what the DXVK switch already does. Bottles' own feature
  (`frontend/windows/dlloverrides.py`) is name plus `("b", "n", "b,n", "n,b", "d")` for the same
  reason. Removed for want of a use case; rebuild it only with one.
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

### yabridge

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

### The front ends

- **`command:` is `cabinet`, so the CLI with no arguments has to open the window.** A bare
  `flatpak run io.github.mark12870.cabinet` runs the default command, and GNOME Software's
  *Open* falls back to exactly that when its cached appstream predates the desktop entry —
  so printing usage there means clicking Open does nothing at all, with no window and no
  output. `Program.LaunchGui` execs `/app/bin/cabinet-gui`; usage moved to `--help`. The
  alternative was `command: cabinet-gui`, which would have rewritten every `flatpak run
  $cabinet <verb>` line in the README.
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
- **A prefix's settings live on a pushed page because a rebuilt expander collapses.** Every
  mutation ends in `RefreshAll`, which clears and rebuilds the prefixes list, so settings held
  in an `Adw.ExpanderRow` vanished the moment one was changed. `Adw.NavigationView` is now the
  window's content with the header, switcher and stack as its root page; `PrefixesPage.Refresh`
  refills the open `PrefixPage` in place and pops it only when its prefix is gone.
- **An `Adw.ActionRow` with no activatable widget is not clickable.** `SetActivatable(true)`
  changes nothing on its own — it is the `Gtk.ListBoxRow` default. Measured: the row emitted no
  `activated`, offered AT-SPI no `click` action, and a pointer click on it did nothing. So a
  navigating row carries a `Ui.RowButton` suffix that is also its `SetActivatableWidget`, which
  is what makes the whole row activate — the shape the action rows already used.
- **`Enrolment` creates the symlink; it did not used to.** The side effect lived inline in the
  CLI's `Enrol`, which meant a second front end would have had to copy it. `Enrolment.Link`
  now owns it and both call it. `Enrolment` still only *prints* the `flatpak override` — the
  GUI must not run it either, for the reason the README gives.
- **A per-prefix Wayland/X11 switch was asked for and deliberately not built.** yabridge embeds
  plugin editors by X11 reparenting, so the setting could never affect a bridged plugin — only
  `winecfg`, installers and `cabinet run`. `WAYLAND_DISPLAY = ""` stays unconditional; see the
  Wayland gotcha above for why the GUI holding a Wayland socket makes that load-bearing.

### Working in this repo

- **`dotnet` leaves two servers running, and inside `flatpak run` they hang the next run.**
  MSBuild worker nodes (`nodeReuse:true`) and Roslyn's `VBCSCompiler` both outlive the command
  that started them by 10–15 minutes, and because `bwrap` waits for every process in its
  namespace they keep that sandbox open long after the check "finished". A second run then
  finds the first one's compiler socket in the shared `/tmp` and connects to a server in a
  namespace that cannot see its files — `scripts/checks.sh` sat at **zero CPU for eight
  minutes**, which reads as slow rather than stuck. It exports `MSBUILDDISABLENODEREUSE=1`
  and `UseSharedCompilation=false` for that reason; the whole run is about 25 seconds, so
  there is nothing for the servers to save. Same server the GUI's `-p:UseSharedCompilation=false`
  turns off under *Build and test*, and the reason never to run a one-off `dotnet` in a sandbox
  of its own beside a check that is already running.
- **Never poll for a background command here — the harness already notifies.** Waiting on the
  flatpak build with a `pgrep`/`sleep` loop burned the full 600 s timeout for nothing: the
  builder runs as `flatpak run org.flatpak.Builder`, which `pgrep -f flatpak-builder` does not
  match, so the loop kept sleeping *after* the build had already exited 0. Start the long
  command in the background and wait for its completion event instead. And do not pipe a long
  build through `tail`: the pipeline buffers, so the output file stays empty for the whole run
  and progress cannot be checked at all.

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

## Releasing

Version, signing key and the publishing rules live in the **`releasing` skill**
(`.claude/skills/releasing/`). Read it before editing the metainfo: the version lives only in
the newest `<release>` there, bumping is manual on purpose, and a published version cannot be
corrected afterwards.
