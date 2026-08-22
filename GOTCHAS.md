# Gotchas

Everything that has bitten this project, kept where it is findable. These were all found by
something failing, not by reading documentation, so each one is evidence rather than advice:
read it before theorising about a failure, and add what the next failure teaches here.

## The sandbox and its masks

- **`--device=all` does not include `/dev/shm`.** yabridge's audio buffers are `shm_open`, so
  `--device=shm` is required on Cabinet *and* on every bridged DAW. A sandbox without it sees
  an empty `/dev/shm` while the host has entries — that is the check `doctor` makes.
- **Flatpak masks `~/.var/app/<other-app>` even under `--filesystem=home`.** The chainloader
  resolves yabridge through the DAW's `$XDG_DATA_HOME`, which is exactly that masked path, so
  Wine is handed a path that does not exist on its side and fails with
  `failed to open …yabridge-host.exe.so`. **The shim `realpath`s everything it forwards** — do
  not "simplify" that away. An explicit `--filesystem=~/.var/app` is honoured.
- **`~/.local/share/flatpak` is masked too**, so `doctor` reads override files through an
  explicit `--filesystem=~/.local/share/flatpak/overrides:ro`.
- **A DAW cannot reach prefixes in `~/.var/app/<cabinet>` by default.** yabridgectl's bundles
  symlink into the prefix and libyabridge walks up for `dosdevices`, both in the *DAW's*
  process; REAPER reported only "failed to scan". `enrol` grants `--filesystem=<prefixes>:ro`,
  and anything that moves the prefixes must move that grant too.
- **A DAW's `WINELOADER` reaches Cabinet through `flatpak run`**, so anything Cabinet starts
  would exec the shim and re-enter its own sandbox. Every Wine invocation pins
  `WINELOADER=/app/bin/wine`.
- **`app-path` in `/.flatpak-info` is content-addressed**, so it must never be baked into a
  DAW's override. `Layout.StableAlias` rewrites it to the `current/active` alias.
- **`--filesystem=home:ro` plus `--filesystem=~/.vst3:create` works**: the specific grant wins.
  Verified, not assumed.
- **`/.flatpak-info` does not say which remote the app came from.** The origin is the first
  NUL-terminated string of the deploy GVariant at `<app-dir>/current/active/deploy`, reached by
  the existing `--filesystem=~/.local/share/flatpak/app/<id>:ro`. The name alone cannot tell a
  published build from a local one — the user chooses it — so `about` also reads the remote's
  `url` from `<install-root>/repo/config` through its own `--filesystem=…/repo/config:ro`; a
  **single-file** grant is honoured through the mask, verified by reading it from inside.
  `file://` is the local build, any other scheme the published one, and a url that cannot be
  read is *unknown* rather than local — an install predating the grant must not be mislabelled.

## The manifest and the runtime

- **`base:` copies the base app's files but not its extension declarations.** Inheriting
  multilib Wine loses `org.freedesktop.Platform.Compat.i386` and it dies on
  `/lib/ld-linux.so.2: could not open`. The manifest re-declares them, *including*
  `org.winehq.Wine.gecko` and `.mono` — another app's extensions are re-declarable, as Bottles
  does against the same base.
- **The runtime is GNOME, not freedesktop, and that is the GUI's doing.**
  `org.freedesktop.Platform//25.08` carries GTK3 but no GTK4 and no libadwaita;
  `org.gnome.Platform//50` carries both and keeps the `lib/i386-linux-gnu` mount point Wine's
  32-bit tree needs. Safe because it is what Bottles ships —
  `runtime=org.gnome.Platform/x86_64/49` over `base=app/org.winehq.Wine/x86_64/stable-25.08`,
  the same base Cabinet uses. The three SDK extensions do **not** move: `org.gnome.Sdk//49` and
  `//50` both declare `[Extension org.freedesktop.Sdk.Extension]` at `version = 25.08`, the
  branch `dotnet10`, `llvm20` and `rust-stable` are already pinned to. Verified with
  `flatpak remote-info --show-metadata`.
- **`stable-25.08`, not `wow64-25.08`.** yabridge's 32-bit host is a 32-bit *winelib* binary,
  which new WoW64 cannot run — that would silently drop most of the older VST2 catalogue.
- **The shim is not statically linked**, though it should be: the freedesktop SDK ships no
  static libc and rust-stable carries only gnu targets. `--cabinet-self-test` exists so a DAW on
  an older runtime gives a legible error instead of a plugin that will not scan.
- **`nuget-sources.json` must be regenerated whenever the C# packages move**, with
  `flatpak-dotnet-generator.py` from `flatpak/flatpak-builder-tools` (the `flatpak` org, not
  `flathub`). Required even with zero third-party packages: NativeAOT pulls ILCompiler from
  NuGet. Keep the shim dependency-free and the Rust side needs no equivalent.
- **Never use `flatpak-builder --install`**, and never re-add `--generate-static-deltas` — see
  `../beeper-flatpak/CLAUDE.md`, whose publishing gotchas all apply here unchanged.
- Fedora's system `flathub` remote is filtered; add a user-scoped one to install SDKs.
- **The running build reads its own version back out of `/app/share/metainfo/`.** That keeps the
  metainfo the one place the version lives; nothing goes into a csproj `Version`, which a
  `<release>` bump could not reach.

## Wine, prefixes and runners

- **`org.winehq.Wine` bakes `WINEPREFIX=/var/data/wine` into its metadata.** Always pass an
  explicit prefix rather than assuming an unset one.
- **A prefix picks its own Wine, and the shim resolves it from `WINEPREFIX` alone.** `runners/`
  is a *sibling* of `prefixes/`, so `<prefix>/../../runners/<name>/bin/wine` is the entire
  lookup — moving either directory breaks the plugin-load path. The choice lives in
  `<prefix>/.cabinet-runner` rather than in a config file of Cabinet's because an enrolled DAW
  is granted the prefixes directory and nothing else, and for the same reason the shim never
  stats the result: that would fail for a runner which works fine on the other side. A runner is
  just a directory with `bin/wine` in it, so unpacking a tarball is the whole install, and its
  `wineboot`/`winecfg` must be run from that same `bin/` — `PATH` still points at the bundled
  Wine. Create a prefix on the runner it will keep: `wineboot` writes a registry the next Wine
  inherits, and Wine refuses a prefix another `wineserver` still holds, so close the DAW before
  moving one.
- **A Wine installer's window can open off-screen and blank, and `explorer /desktop=` was built
  for that and then removed.** FabFilter's setup put its 512×370 window at x=2944 on a 2560-wide
  display (`xdotool getwindowgeometry`), so `library install` looked hung; moving it back left
  it mapped but blank, a shadow with no drawable that `import -window` refused.
  `wine explorer /desktop=<prefix>,1024x768` fixes it completely, verified by photographing the
  wizard inside it. It is **not** what ships: a later run in another prefix placed the same
  window correctly with no desktop at all, so the fault is intermittent and a virtual desktop
  would put every installer in a fake Windows desktop to insure against it. `Prefixes.Install`
  runs the `.exe` plainly; if an installer goes missing again, that one-line wrapper is the
  answer.
- **FabFilter's installer has no silent mode.** Measured: `/S`, `/SILENT`, `/VERYSILENT`, `/qn`,
  `/quiet` and `-s` each opened the wizard and installed nothing, `Common Files/VST3` empty
  after every one. It is FabFilter's own installer, not NSIS or Inno, and its packed image
  carries no switch strings to find. That is FabFilter's installer, not the rule: Serum 2's is
  NSIS — `file` says so — and `/S` installs all 1.9 GB of it unattended, which is what
  `xfer-records/serum.sh` does. Check the installer before assuming either way; where there is
  no silent switch, a wizard somebody clicks through is the answer.
- **FabFilter's VST2 lands where yabridge does not look.** Its installer put all fourteen VST3
  bundles in `Common Files/VST3` and all fourteen CLAPs in `Common Files/CLAP` — both in
  `Layout.PrefixPluginDirs`, 28 plugins in one `sync` — but the VST2 `.dll`s in
  `Program Files/FabFilter/<Product>/`, a vendor directory nothing scans, because a fresh prefix
  has no VST2 folder in its registry for an installer to read and it invents one. The entry says
  `Formats: VST3, CLAP` for that reason: what a DAW will actually find. Do not widen
  `PrefixPluginDirs` to chase a vendor directory — a *conventional* VST2 directory is a
  different question, and `Steinberg\VstPlugins` below is one.
- **An installer that never ran still exits 0.** With the wizard sitting unseen on its first
  page, the whole run around it reported success — DXVK installed, sync set, yabridge synced,
  the id appended to `.cabinet-plugins` — and `Common Files/VST3` was empty. A user who cancels
  one says the same thing to Cabinet. Read the prefix, not the status.
- **Runners carry no Mono or Gecko, and Wine will ask the user to download them.** Those come
  from Flatpak extensions mounted at `/app/share/wine/{mono,gecko}`, which only the bundled Wine
  has, so `Runners` symlinks them into every runner it unpacks. Cabinet's own `wineboot` also
  runs with `WINEDLLOVERRIDES=mscoree=d;mshtml=d` so creating a prefix never blocks on a dialog
  — scoped to `wineboot`, so an installer that really wants .NET still gets it.
- **`wine-<v>-staging-amd64` has no fsync; `-staging-tkg-amd64` does.** Measured:
  `WINEDEBUG=+fsync` produced nothing on plain Staging and 4685 lines on a TkG build, which also
  reports `( TkG Staging Esync Fsync )`. yabridge calls an fsync build *"the most important
  thing you can do"* and the shim forwards `WINEFSYNC` for it, so `runners install` takes the
  TkG asset. Never `-wow64` or `-x86`: yabridge's 32-bit host is a 32-bit winelib binary new
  WoW64 cannot run, which is why `Runners` rejects an archive with no 32-bit tree.
- **The versions list must not touch `api.github.com`.** Unauthenticated callers get 60 requests
  an hour *per address*, shared with everything else on the machine, and each dialog opening
  spent two; the list then failed with `could not reach …`, which was a lie — GitHub answered,
  with 403. It now reads Bottles' generated index `bottlesdevs/components/main/index.yml` over
  `raw.githubusercontent.com`, which has no such cap, and `Fetch` reports the three cases apart:
  curl's exit for a transport failure, the status off `-D /dev/stderr` for an HTTP one, the body
  otherwise. `scripts/update-yabridge.py` stays on the API on purpose — one call, run by hand.
- **That index names the family first, so a prefix alone no longer filters it.** `soda-` matches
  `soda-mcsoda-11.0-4` and `soda-experimental_8.0`; what rejects them is `VersionOf` requiring
  the remainder to start with a digit, and `ReleasesFrom` also drops the `unstable` channel.
  Kron4ek needs its `-staging-tkg-amd64` suffix too, which keeps `kron4ek-wine-proton-*` out.
  The list beats the feed it replaced — 114 Kron4ek builds against a 100-release page — and
  reaches back far enough for `runners install 9.21`.
- **`index.yml` carries no download URL; the per-runner manifest does.** `runners/wine/<name>.yml`
  is fetched at install time only, for its `url` and its `file_checksum` — **MD5**, verified
  against the real `soda-11.0-5` archive. Soda is checked now, where it used to be taken on the
  strength of its https download alone; Kron4ek keeps its stronger `sha256sums.txt`. Soda 11.0-5
  is wine-tkg over Valve's experimental Wine with a full old-style 32-bit tree, so it clears the
  same bar the TkG asset does — except for fsync. **Soda's own `wine --version` says
  `( TkG Plain )`** where both Kron4ek builds say `( TkG Staging Esync Fsync )`; whether Valve's
  base carries fsync of its own is unmeasured. The Runners page shows that string, which is how
  it was noticed.
- **A per-prefix setting has to be added on both sides, or it only half exists.** Cabinet's own
  Wine goes through `Prefixes.Wine`; the *plugin-load* path goes through the Rust shim, and that
  is the one that matters. So `.cabinet-sync` and `.cabinet-env` are parsed twice, in
  `PrefixSettings` and in `shim/src/main.rs`, exactly as `.cabinet-runner` already was. A DLL
  override needs none of that — it lives in the prefix's own registry, so both paths get it for
  free, which is why `PrefixRegistry` writes there rather than inventing a third marker file.
  The shim emits its `--env=` flags *after* the `FORWARD` loop so a prefix wins over the DAW,
  and refuses the four keys Cabinet owns so an env file cannot break the crossing.
- **Sync `system` means inherit, not off.** It emits nothing at all, so the shim keeps relaying
  whatever `WINEFSYNC` the DAW was launched with. The other three modes set their own variable
  to `1` **and the other two to `0`**, because a half-set choice would silently lose to a DAW
  that exports `WINEFSYNC=1`. Defaulting new prefixes to fsync was rejected: Soda reports
  `( TkG Plain )` and has none.
- **`File.ResolveLinkTarget` throws when the path does not exist** — the normal first run.
  `new DirectoryInfo(p).LinkTarget` answers `null` instead.
- **Wine symlinks the prefix's profile out to `$HOME`, and Cabinet holds `$HOME` read-only.**
  `wineboot --init` points `Documents`, `Desktop`, `Music`, `Pictures`, `Videos` and `Downloads`
  at the host's own, so a Windows plugin that keeps its content under the user profile writes to
  a path the sandbox refuses — and **the installer still exits 0**. Serum 2 recorded
  `PresetsDir=C:\users\marek\Documents\Xfer\Serum 2 Presets` and laid down nothing but its
  18.9 MB binary: a 514 MB prefix against 1.9 GB once the same installer ran with a real
  directory there, the missing 1.4 GB being the wavetables, presets and `Skins`. **Not** a TkG
  defect, which was the first guess: the bundled stock WineHQ 11.0 makes the same six links, and
  Soda escapes only because it is Proton-derived — a `steamuser` profile with real directories.
  So it bites the bundled default and every Kron4ek/TkG runner. `Prefixes.ContainProfile`
  replaces any profile link pointing outside the prefix with a directory, which is `winecfg`'s
  own unlink and keeps everything Cabinet owns inside `~/.var/app`; the alternative, a
  `--filesystem=~/Documents` grant, would put a vendor's content in `$HOME` for good. It runs on
  every `Create`, so an older prefix is repaired the next time one is installed into, and
  **`wineboot -u` does not put a link back where a real directory stands** — measured, which is
  why containment is not repeated after the update in `Dxvk.Remove`.

## DXVK and plugin editors

- **yabridge 5.1.1's plugin editors need Wine 9.21 or older.** From 9.22 on, clicks land offset
  by the window's distance from the screen origin —
  [yabridge#382](https://github.com/robbert-vdh/yabridge/issues/382), acknowledged upstream and
  unfixed. The bundled Wine is 11.0, so any prefix with a usable editor wants
  `cabinet runners install 9.21`. That fixes VST2 outright — verified on Aalto. **VST3 needs two
  more things**, neither of them Cabinet's: `editor_disable_host_scaling = true` under `["*"]`
  in `~/.vst3/yabridge/yabridge.toml` before clicks land at all, and the repaint failure seen
  alongside it, which is DXVK's, below. **The versions list says none of this on purpose**:
  flagging every release from 9.22 on read as Cabinet grading upstream Wine.
- **A JUCE editor that never repaints wants DXVK in its prefix.** Surge XT drew once and then
  only when the window moved. The fix is DXVK's `d3d11`, `dxgi`, `d3d10core` and `d3d9` copied
  into `system32` and `syswow64` and set to `native` — what `cabinet dxvk <prefix>` does,
  measured working: `DXVK: v2.7.1`, `Found device: AMD Radeon RX 5700 XT (RADV NAVI10)`,
  `D3D11InternalCreateDevice`. This is yabridge's own answer to that symptom (yabridge#212,
  #270, #384) and it needs a Vulkan driver reachable from inside the sandbox: `--device=dri` is
  in the manifest, and the 32-bit ICD arrives with `org.freedesktop.Platform.GL32.default`.
- **surge#7636's recipe is wrong for a bridged plugin, in both directions.** It prescribes
  `WINEDLLOVERRIDES="d3dcompiler_47=n;dxgi=n,b"` with Wine 10.8 and explicitly *no* DXVK. The
  override does bite — `Library d3dcompiler_47.dll (which is needed by d2d1.dll) not found`, so
  Direct2D cannot load — the editor stays frozen on 9.21 and 10.8 alike, frozen even while a
  note sounds and nothing is being clicked, and it blocks the very path DXVK accelerates. Wine
  10.8 also costs what yabridge#382 costs: the pointer arrives at (2473, 443) in a 2077×1294
  client area, past the right edge. That advice is for bare Wine; under yabridge, DXVK is the
  fix and 9.21 is still the runner. Per-prefix overrides go in the prefix's own
  `HKCU\Software\Wine\DllOverrides`, written with `cabinet run <prefix> wine reg add` — the
  environment variable the issue quotes would have to be set on the DAW, which is every prefix
  at once.
- **DXVK overwrote Wine's own `d3d*`/`dxgi` in place, so the switch needs backups.** `Install`
  moves each replaced DLL to `<prefix>/.cabinet-dxvk-backup/{system32,syswow64}/` first, and
  `Remove` moves it back. `wineboot -u` alone will not undo an install: it will not replace a
  DLL whose version resource is newer than the runner's, which DXVK's are. **A missing backup
  does not mean Wine had no such DLL** — that was assumed, and it destroyed a real prefix:
  `aalto` was DXVK'd before backups existed, so turning the switch off deleted all five outright
  and left `system32` with no Direct3D. `Remove` now reports whether every library came back and
  runs `wineboot -u` when one did not, which does restore an *absent* DLL; any prefix predating
  the backup directory takes that path.
- **`Remove` had to be told DXVK was ever installed, or it deleted Wine's own Direct3D.**
  `Restore` treats every `d3d*`/`dxgi` in the prefix as DXVK's, so with no backup beside it the
  DLL was deleted as if it were: `cabinet set aalto dxvk off` twice, measured, left all ten
  missing. The recovery never ran either, because **`wine reg delete` exits 1 for a value that
  is not there** (`reg: Unable to find the specified registry value`) and every `reg` call was
  checked, so the second `off` threw on `d3d8` before reaching `wineboot -u`. `Remove` now
  refuses a prefix whose marker is absent, `Unset` ignores a delete for an override Wine has
  already forgotten, and the marker is deleted *after* the recovery — a prefix `wineboot` could
  not fix must not read as DXVK-free.
- **A DLL-override editor was built and then removed, and the registry is why.** The registry is
  the truth for *Wine* but not a list of anyone's decisions: `aalto` carried 29 overrides nobody
  typed — `msvcp*`, `ucrtbase`, `atl*`, `api-ms-win-crt-*` — written by the VC++ redistributable,
  because setting `native,builtin` is *how* a dependency makes Wine load what it copied in.
  Installing a dependency and adding an override are one mechanism, so they cannot be told apart
  afterwards. Wine also has **no path field** — an override is a load order for a module name —
  so "point at a DLL" cannot be expressed without copying the file in first, which is what the
  DXVK switch already does. Bottles' own feature (`frontend/windows/dlloverrides.py`) is name
  plus `("b", "n", "b,n", "n,b", "d")` for the same reason. Rebuild it only with a use case.
- **Plugin editors get absolute screen coordinates where they expect client-relative ones.**
  Clicks are delivered and dispatched correctly — they land offset by exactly the plugin
  window's distance from the screen origin. Measured by clicking one corner and moving the
  window: reported `(642,254)` at screen `(641,253)`, `(0,66)` once dragged to the top left.
  Reproduces on Aalto (VST2 x64), Dexed (VST3 x64) and Surge XT (VST3 x86), and
  `editor_xembed = true` does not help. **Not Cabinet's**: the clicks cross the sandbox and
  reach the right window, and it is not
  [yabridge#506](https://github.com/robbert-vdh/yabridge/issues/506) either — there
  `WM_LBUTTONDOWN` never reaches a window procedure, here it does with a wrong lParam. Evidence
  in `~/cabinet-editor-coordinates-evidence.log`. A static UI hides this: Dexed and Surge look
  frozen — for their own reason, a missing DXVK — while Aalto's animated graphs make it obvious
  that drawing works and only input is misplaced. An earlier investigation blamed Surge
  ([surge#7636](https://github.com/surge-synthesizer/surge/issues/7636), a real but separate
  repaint bug) and wrongly recorded Dexed as unaffected; before that, GNOME focus-stealing, the
  embedding handshake, Wine's spinning `mmdevapi_midi_n` thread and fractional scaling. Six
  wrong answers, all reached by theorising; what worked was `YABRIDGE_DEBUG_LEVEL=1+editor` for
  the embedding and `WINEDEBUG=+msg,+event` to decode the lParam. Measure the coordinate before
  proposing anything.

## The Library

- **FabFilter publishes no checksum, and a self-computed one would be theatre.**
  `cdn-b.fabfilter.com/downloads/fftotalbundlex64.exe` is one path with no version in it whose
  bytes change every time any of the fourteen plugins updates — 95 810 344 bytes,
  `last-modified: 25 Jun 2026`, off the CDN's own headers. Pinning a sha256 there is not a pin
  but a fuse: every install would die on `failed its checksum` between a FabFilter update and
  Cabinet's next release, and the number would only be what whoever wrote the entry happened to
  download. The per-plugin installers that *do* carry a version (`ffproq413x64.exe`) would mean
  fourteen entries and fourteen wizards. So `Source: rolling`, which **refuses** `Sha256`
  outright and warns instead: nothing can verify what arrives, only that it came from the vendor
  over HTTPS. Both front ends say that before the download starts, from the one sentence in
  `Library.Unverifiable` — which the GUI has **two** dialogs to say it in, `Prospect` for a
  prefix and `ConfirmInstall` for a native plugin. A rolling entry states no `Version` either.
  `download` is unchanged and still strict — a mismatch deletes the file and throws — and stays
  the rule for anything with a version in its filename. The bundle installer is itself a
  **32-bit** PE that lays down 64-bit plugins, one more thing a runner with no 32-bit tree would
  break.
- **FabFilter's plugins run best on `soda-11.0-5`, not on 9.21.** That is the runner the entry
  pins, a measured preference rather than a derivation from the editor rule above: 9.21 is what
  an editor with the yabridge#382 click offset needs, and FabFilter's do not behave better for
  it. Soda reports `( TkG Plain )` rather than `Esync Fsync`, which costs nothing here, so the
  entry stays on `Sync: system`. Anything that changes the entry's `Runner:` changes what was
  actually tried.
- **An account-gated download looks like a URL and is not one.** Vital's is
  `account.vital.audio/VitalInstaller.zip?idToken=<JWT>&version=1_0_7`: the token is one user's,
  expires an hour after it is issued, and redirects to a presigned R2 URL good for 900 seconds —
  read off the `302` and its `X-Amz-Expires`. There is nothing there to pin, not a checksum and
  not even a path that resolves for a second user, so `Source: rolling` does not cover it either
  — rolling still assumes one stable vendor URL anyone can fetch. `Source: byo` does, and
  `Account:` names the page to go and get it from. **Cabinet cannot do the download itself**,
  and that is architecture rather than effort: the session lives in the user's browser, the
  sandbox cannot reach its storage, and the alternatives are holding a vendor password — Vital's
  is Firebase, so one POST to `identitytoolkit` with a scraped API key, one vendor's worth of
  bespoke auth in Core — or embedding WebKit, which `org.gnome.Platform//50` does carry. Both
  rejected. So Cabinet opens the page and takes the file the user comes back with, which is also
  why `Account` is refused on an entry Cabinet downloads.
- **Vital's zip needs no script, measured by unpacking it.** `VitalBinaries/` holds `Vital.vst3`,
  `Vital.lv2` and a bare `Vital.so` beside the `vital` standalone, all one level down, exactly
  as deep as `Bundles` walks. The standalone has no plugin extension so it is not linked, and
  1.0.7 predates CLAP. Nothing is laid outside `data/native/vital/` either: the free version's
  factory content is the *plugin's* to write at runtime, in the DAW's sandbox, so the entry
  needs no `Data:` and no manifest grant.
- **A native Linux plugin cannot be sandboxed from here, and `bwrap` is not the missing piece.**
  It was asked for and the answer is no on architecture, not tooling: `bwrap` and
  `flatpak-spawn` are both in `org.gnome.Platform//50` and the host allows unprivileged user
  namespaces — checked, not assumed. But a native plugin is a `.so` the DAW `dlopen`s into its
  own address space, and `bwrap` namespaces a *process*; there is nothing there to wrap. Doing
  it would mean an out-of-process host behind a stub `.so` — yabridge's entire architecture,
  which yabridge does not offer for native plugins — spawned from inside the DAW's own sandbox
  through the `--talk-name=org.freedesktop.Flatpak` permission `enrol` deliberately refuses to
  apply, and the sandbox would have to grant back PipeWire, X11, presets, licence files and
  network to be usable at all. So a native plugin gets its **files** isolated and nothing else:
  its own `data/native/<id>/`, symlinked into `~/.vst3`, `~/.clap`, `~/.lv2` and `~/.vst`. Clean
  uninstall, not a boundary — do not describe it as one.
- **A Flatpak DAW cannot follow a link into `data/native/` either.** Same mask as the prefixes
  gotcha above: the symlink Cabinet writes into `~/.vst3` points at
  `~/.var/app/io.github.mark12870.cabinet/data/native/<id>/…`, masked for every other app, so
  the DAW sees a dangling link and the plugin does not appear. `enrol` grants
  `--filesystem=<native>:ro` beside the prefixes grant and `doctor` checks for it — anything
  that moves that directory has to move both.
- **A Library entry describes the install, and may name a script for one step of it.** That step
  is the unpack — or, for a Windows entry, the installer run — and nothing else: the checksummed
  download, the links into `~/.vst3` and friends, the `.cabinet-plugins` record, DXVK and the
  yabridge sync stay in `Library`, so what an install leaves behind does not depend on what a
  script did, and removal never runs one. `Script:` names a file the build ships in the entry's
  own vendor directory under `/app/share/cabinet/library/`, never a path; the script is `sh -e`,
  run **in the destination directory** so a `tar -xf` with no `-C` still lands inside, and told
  everything else through `CABINET_ARCHIVE`, `CABINET_DEST`, `CABINET_DATA`, `CABINET_WORK` and,
  for a prefix, everything `Prefixes.Wine` sets. It must not download: the archive is already
  checksummed and a second fetch would not be. A `Data:` directory is Cabinet's, not the
  script's — it creates it, refuses to install over one it did not create, and deletes it on
  removal.
- **u-he ships no bundle, and its plugins read `$HOME/.u-he/<Product>` by name.** The tarball is
  one `<Product>.64.so` plus `Data`, `Presets` and the rest, and upstream's `install.sh` builds
  the VST3 around it. The binary's own strings are `%s/.%s/%s/Data` with `u-he` — it does *not*
  resolve its resources beside itself, so keeping them in `data/native/<id>/` would give every
  u-he plugin a GUI with no images and no presets. `u-he.sh` therefore assembles
  `<Product>.vst3/Contents/x86_64-linux/<Product>.so` in `CABINET_DEST` and fills
  `CABINET_DATA`, which the entry points at `.u-he/<Product>`. Whether a product is also a CLAP
  is decided by `grep -qa clap_entry` on the binary rather than by copying upstream's hardcoded
  list — measured to agree with it on all seven free products. `install.sh` itself is never run:
  it copies into `~/.vst`, `~/.vst3` and `~/.u-he` on its own terms, the nondeterminism the
  split exists to avoid. **Cabinet holds `$HOME` read-only**, so a `Data:` directory needs its
  own `--filesystem=~/<dir>:create` in the manifest — `~/.u-he` has one, and a second vendor
  needs a second grant rather than widening `home`.
- **`Bundles` walks two levels and never descends into a bundle.** A Linux `.vst3` and an `.lv2`
  are both *directories* carrying a plain `.so` inside, so a recursive search links that inner
  `.so` into `~/.vst` as if it were a VST2 — found by inspecting
  `surge-xt-linux-1.3.4-pluginsonly.tar.gz`, whose top level holds `Surge XT.lv2/libSurge XT.so`
  beside `Surge XT.vst3` and `Surge XT.clap`. So a path matching a plugin extension is yielded
  whole and never walked into, and `BundleDirectories` catches the bundle formats Cabinet does
  *not* link — `.vst`, `.lxvst`. Do not replace that test with "any directory with an
  extension": a folder named `Surge XT 1.3.4` has extension `.4`. Dexed's zip is the flat case,
  `Dexed.vst3/` and `Dexed.clap` at the top beside a standalone binary and a `LICENSE`.
- **The catalogue lists a plugin once, and prefers its Linux build.** A Windows entry costs a
  prefix, a pinned old runner, DXVK and a bridge on the audio path; a native one costs a
  symlink. So Surge XT and Dexed ship as `Kind: native` only, and the Windows section is
  effectively the commercial plugins with no Linux build. The rule and how to check it live in
  the `library-entry` skill.
- **LV2 is linked but never bridged.** yabridge does VST2, VST3 and CLAP, so there is no such
  thing as a Windows LV2 entry — `.lv2` reaches `~/.lv2` only through a `Kind: native` plugin,
  and the manifest's `--filesystem=~/.lv2:create` exists for that alone. Cabinet writes the
  bundle directory as one symlink; it does not merge into an existing `~/.lv2` bundle.
- **An existing prefix keeps its runner, and the runner is not fetched for one.**
  `library install <id> <existing>` says what the plugin would rather have and leaves the prefix
  alone, because moving it means `wineboot -u` on someone else's working setup. `EnsureRunner`
  is therefore called only when the prefix is being created — reaching for it first downloads a
  100 MB Wine that is then thrown away. Same reason the entry's sync mode is written only for a
  prefix that call created.
- **A runner is matched offline.** `Answers` derives the installed directory name from the
  family and the version — `Runners.DeriveName(family.AssetFor("9.21"))` is
  `wine-9.21-staging-tkg` — rather than asking Bottles' index which build `9.21` is. Resolving
  it upstream would make every install need the network, and fail when Cabinet already had the
  runner sitting there.
- **Which plugin a prefix holds is recorded, not guessed.** A Windows entry reads as installed
  only because `InstallWindows` appends its id to `.cabinet-plugins` in the prefix, beside
  `.cabinet-runner` and the rest. Guessing from the entry's `Prefix:` field was rejected and is
  wrong both ways: installing into a differently-named prefix would read as not installed, and
  an empty prefix sharing the name would read as installed. The record lives inside the prefix,
  so deleting the prefix takes it with it. A native plugin needs no record: its
  `data/native/<id>/` directory *is* the fact.
- **`Kind: native` with `Source: byo` is refused.** `Install` ignores the installer argument for
  a native plugin and `Fetch` dereferences `Url`, so the combination was an entry nothing could
  install — and the GUI's install dialog, which names the download host, had nothing to name.
  Both front ends now fail at `LibraryEntry.Parse` instead of at the download. A paid Linux
  plugin would need a real answer here, not this one.
- **Removing a Windows plugin runs the plugin's own uninstaller, and the prefix question comes
  first.** A prefix may hold more than one plugin — three Valhalla entries share `valhalla` — so
  deleting it is not the same act as removing one plugin. Both front ends therefore ask *delete
  the prefix?* before anything runs, but only when `.cabinet-plugins` records nothing else in
  it; a yes takes the Wine tree, the registry and the settings with it and no uninstaller runs
  at all. Otherwise `Library.Remove` runs what the installer registered, found in the prefix's
  own registry. Removal is still **never scripted** — a `Script:` is install-only.
- **The uninstall registry is not a list of a prefix's plugins.** `aalto` registers *Wine Mono
  Runtime* and *Wine Mono Windows Support* beside *Aalto version 1.9.4* — Wine's own components,
  installed by `wineboot`, and uninstalling one would break the prefix. So attributing every
  entry in a prefix to the one plugin Cabinet recorded there was tried and is **wrong**.
  `Library.Candidates` drops anything already attributed to another recorded plugin, anything
  whose name begins `Wine `, then keeps only what resembles the entry's own `Name` with
  non-alphanumerics squashed out — which matches all four real cases (`Aalto version 1.9.4`,
  `Xfer Records Serum 2`, `FabFilter Total Bundle`, `ValhallaSupermassive version 5.0.0`) and no
  Wine component. Find nothing and Cabinet says so and stops; it does not offer a list to guess
  from.
- **`QuietUninstallString` is what makes an uninstall silent, and it is not always there.**
  Measured in the real prefixes: Valhalla's Inno Setup entry publishes `"…\unins000.exe" /SILENT`
  beside the plain string, so it uninstalls with no window at all; Serum and FabFilter publish
  only `UninstallString`, so a wizard opens. Prefer the quiet one, and never append a silent
  switch of your own — the same rule as installers.
- **An `UninstallString` is a Windows *command line*, and there is no way to hand one to `wine`
  as argv.** Two shapes have to survive: Valhalla's `"…\unins000.exe" /SILENT`, quoted with an
  argument, and FabFilter's `C:\Program Files\FabFilter\Uninst.exe`, unquoted *with a space in
  it*. Splitting on whitespace breaks the second. Passing the whole string as one argv element
  to `wine cmd /c` breaks the first, measured rather than reasoned: Wine builds the Windows
  command line back out of argv and escapes the embedded quotes so `CommandLineToArgvW`
  round-trips, but `cmd` reads the raw tail instead, so it saw
  `\"C:\ProgramData\…\unins000.exe\" /SILENT` and answered *Can't recognize … as an internal or
  external command*. So `Library.Uninstall` writes the line to `drive_c\cabinet-uninstall.bat`
  and runs `wine cmd /c C:\cabinet-uninstall.bat`, letting Windows parse it exactly as Windows
  would, and deletes the file in a `finally`. Do not "simplify" that back into an argv split or
  a bare `cmd /c <string>`.
- **All three real entries live under `Wow6432Node`**, so both the plain and the 32-bit branch
  of `…\CurrentVersion\Uninstall` have to be read, in `system.reg` (HKLM) *and* `user.reg`
  (HKCU).
- **`PrefixRegistry` parses `system.reg` and `user.reg` directly rather than shelling to
  `wine reg query`.** They are plain UTF-8 in the prefix, it needs no wineserver, and it is the
  only version of this that `Cabinet.Core.Tests` can exercise — tests never run Wine. The
  fixtures are text lifted verbatim from the real prefixes.
- **An uninstaller that never ran exits 0, exactly as an installer does.** So removal snapshots
  `PrefixPluginDirs` before and after and refuses to drop the `.cabinet-plugins` record when
  nothing disappeared — a cancelled uninstaller looks identical from the exit code alone.
- **`.cabinet-plugins` is tab-separated now**: the id first, the uninstall keys after it. A file
  of bare ids — every prefix predating this — still parses and carries no key, which is the case
  `Candidates` exists to cover. `Record` finally has its inverse, `Forget`.
- **A published Linux build is not a loadable one, and Sitala 1 is the case that proves it.**
  Its `.deb` carries a VST2 `.so`, exactly what a `Kind: native` entry wants — and it links
  against `libcurl-gnutls.so.4`, which is *Debian's* name for the library. Fedora ships
  `libcurl.so.4`, `org.freedesktop.Platform//25.08` ships `libcurl.so.4`, and nothing anywhere
  answers to the other name. Measured, not read: `ctypes.CDLL` on the `.so` from a shell inside
  `fm.reaper.Reaper` fails with *cannot open shared object file*, and loads on the first try
  with an `LD_LIBRARY_PATH` pointing at a symlink — a variable Cabinet cannot set on somebody
  else's DAW. No `$ORIGIN` route either: the binary carries neither `RPATH` nor `RUNPATH`, so a
  symlink dropped beside it in `data/native/<id>/` would never be looked at. So *prefer the
  Linux build* turns on **loadable**, and the way to settle it is a `dlopen` from inside the
  DAW's own runtime, not the presence of a `.tar.gz` on a download page. Both Sitala entries are
  `Kind: windows` for this reason.
- **`Steinberg\VstPlugins` is scanned now, and it is a convention rather than a vendor's
  directory.** Sitala's WiX installer puts its VST2 there and nowhere else, and version 1 has no
  VST3 at all, so without it that entry would bridge nothing. Everything less invasive was tried
  and is worth not retrying: the installer ignores `HKLM\Software\VST\VSTPluginsPath` outright,
  and its `VSTNATIVE_DIR` cannot be steered either, because **Wine 9.21's `msiexec` ignores
  command-line properties altogether** — measured with `INSTALLDIR` alongside it, which also
  stayed at its default whatever the argument order and quoting. Moving the `.dll` afterwards
  was rejected on removal: `RemoveWindows` compares `PrefixPluginDirs` before and after, and a
  vendor's uninstaller deletes what *it* installed, never what Cabinet moved, so every removal
  would fail on *left every plugin where it was*. `Program Files\Steinberg\VstPlugins` is the
  historical Windows VST2 folder and a sibling of the `Program Files\VstPlugins` already in the
  list; `Program Files\FabFilter\<Product>` is a vendor's own, and that line still holds.
- **An `.msi` is not something `Prefixes.Install` can run.** It hands the file to `wine` as an
  executable, which an installer database is not, so a Windows entry that downloads one needs a
  `Script:` and `"$WINE" msiexec /i "$CABINET_ARCHIVE" /qn`. `/qn` is the vendor's own silent
  switch — every WiX package takes it, and Sitala's two were measured taking it.
- **yabridgectl names a bridge after the `.dll`, so one basename may reach the scanned
  directories once — across every prefix.** Sitala hit this twice. Version 1's installer lays
  `Sitala.dll` in both `Steinberg\VstPlugins` directories, 64-bit and 32-bit, and `sync`
  cheerfully reported *2 plugins (2 new)* while `~/.vst/yabridge` held exactly one `Sitala.so`;
  then, with both entries installed, version 2's `Sitala.dll` in *its* prefix overwrote version
  1's, so which one a DAW loads was whichever synced last. Neither case says a word. `sitala.sh`
  therefore deletes the 32-bit twin always, and version 2's VST2 whenever the VST3 landed — a
  `-e` test, because a *Windows* `.vst3` is a plain file where a Linux one is a bundle — and
  version 2's entry says `Formats: VST3` for that reason, while version 1, which has no VST3 at
  all, keeps the only `Sitala.dll` there is. Read `~/.vst/yabridge` after a sync, not
  yabridgectl's count.
- **Wine's `msiexec /I{ProductCode}` opens the *install* wizard, not maintenance mode, so an MSI
  plugin is removed by deleting its prefix.** Sitala's registered `UninstallString` is
  `MsiExec.exe /I{74B609F8-…}`, which on Windows offers Change, Repair and Remove; under Wine
  9.21 it offers *Welcome to the Sitala Setup Wizard*, photographed rather than assumed. Cabinet
  runs it faithfully and there is nothing to rewrite: turning `/I` into `/X` would be Cabinet
  inventing a command, the same rule that forbids inventing a silent switch. Both front ends
  already ask *delete the prefix?* first, and for an MSI that is the answer — the other path
  ends, correctly, in *left every plugin in … where it was*. No `QuietUninstallString` here
  either.
- **A Wine registry value carries its type, and `PrefixRegistry` used to require a bare string.**
  Sitala's `.cabinet-plugins` came out holding a bare id with no uninstall key, because an MSI
  writes `"UninstallString"=str(2):"MsiExec.exe /I{…}"` — REG_EXPAND_SZ, so Wine prints the type
  before the quote — and `Value` insisted on a `"` immediately after the `=`. It found no
  command and dropped the whole entry silently, which reads as *this prefix has no uninstaller*
  rather than as a parse failure. Inno and NSIS write plain `REG_SZ`, which is why every earlier
  entry worked and the gap survived four of them. `Untyped` now steps over a `str…:` prefix. A
  wrong theory came first: `system.reg` gained the key **30 seconds after** Cabinet recorded,
  which looked like Wine holding an unflushed registry, so a `Prefixes.FlushRegistry` was built
  around `wineserver -k`. Instrumenting the call settled it — `wineserver -k` exited **1**, *no
  server to kill*, on both sides of every install, so the registry had been on disk all along.
  The flush was removed again. Instrument the call before believing the timestamps.

## yabridge

- **Nothing in Cabinet writes `yabridge.toml`, and no experiment may leave one behind.** It is
  the user's file, it is not tracked here, and a stale one is invisible: yabridge only mentions
  it as `config from: …` deep in a debug log. One left over from an earlier session cost real
  time in the investigation that found yabridge#382 — checking for it was what finally ruled it
  out. If a session writes one to test an option, delete it in the same session and record the
  option here instead.
- **`yabridgectl set` panics in 5.1.1**, on every invocation: `--path-auto` is declared as
  taking a value and then read as a flag, so clap aborts and `--path` is unreachable.
  `Bootstrap` symlinks Cabinet's own `$XDG_DATA_HOME/yabridge` instead. Re-check on the next
  yabridge bump.

## The front ends

- **`command:` is `cabinet`, so the CLI with no arguments has to open the window.** A bare
  `flatpak run io.github.mark12870.cabinet` runs the default command, and GNOME Software's
  *Open* falls back to exactly that when its cached appstream predates the desktop entry — so
  printing usage there means clicking Open does nothing at all, with no window and no output.
  `Program.LaunchGui` execs `/app/bin/cabinet-gui`; usage moved to `--help`. The alternative was
  `command: cabinet-gui`, which would have rewritten every `flatpak run $cabinet <verb>` line in
  the README.
- **The GUI is GirCore, and it is deliberately not NativeAOT.** GirCore 0.8.1 binds GTK 4.22 and
  libadwaita 1.9 and never claims NativeAOT support, so `Cabinet.Gui` publishes self-contained
  and trimmed while `Cabinet.Cli` keeps `PublishAot`. Measured: a trimmed hello-world
  `Adw.ApplicationWindow` built in `org.gnome.Sdk//50` presented a window under
  `org.gnome.Platform//50`, at 24 MB for the whole publish directory. Avalonia was rejected
  because it draws its own widgets with Skia — it would not inherit Adwaita's accent colour,
  font or dark-mode preference. GirCore's 14 packages are managed P/Invoke wrappers with no
  native blobs, so `nuget-sources.json` went from 6 entries to 20 rather than pulling in Skia
  and HarfBuzz binaries.
- **Cabinet now holds a Wayland socket, and Wine must never see it.** The GUI is GTK4 and wants
  Wayland; yabridge's editor embedding is built on X11. `Prefixes.Wine` therefore passes
  `WAYLAND_DISPLAY` as an empty string, and **`ProcessRunner` removes any variable whose value
  is empty** rather than setting it blank — blanking it would make Wine fail a connection to a
  socket named `""` instead of skipping Wayland. Do not "simplify" that rule away; it is the
  only thing keeping `--socket=wayland` from reaching Wine.
- **Download progress is curl's `--progress-bar`, and it is readable only by accident of
  `ReadLine`.** `Http.ToFile` used `-sS`, so a 100 MB runner fetch printed one line and then
  nothing. curl still draws the bar when stderr is a pipe rather than a tty, and
  `StreamReader.ReadLine` — what `ProcessRunner.Drain` uses — treats a bare `\r` as a line
  terminator, so each redraw arrives as its own `onOutput` call. Two things `od -c` showed that
  reading the flag would not: the `\r` **leads** each draw rather than trailing it, so the first
  line of every download is empty, and before the percentages curl draws a **fly spinner** —
  `#=#=#`, `##O#- #` — for the stretch where it has no size yet. Neither carries a `%`, so
  `Drawn` drops a line made only of ` #=O-`. With an `Action<double>` sink the percentages
  become fractions for the GUI's progress bar; with none they become one `Downloading… NN%` line
  per tens digit, which is how the **CLI** got progress without a line of CLI code. Do not
  "simplify" it back to `-sS`.
- **A progress sink must be throttled, and the symptom of not throttling is a dialog that
  stops.** `Operation.Show` first did what `Write` does — one `Ui.OnMainLoop` per call — and
  curl draws a 7 MB
  download a few hundred times. Twice the install *finished on disk*, curl exited, and the
  dialog sat with a stale bar and a truncated log; the window still repainted, so it read as a
  slow download rather than an idle queue that had stopped dispatching. It is intermittent, so
  the cause is a race in the callback path and **is not proven**; what is measured is that it
  appears only under hundreds of `Ui.OnMainLoop` calls and never under the ten or so an ordinary
  operation makes. `Show` therefore coalesces (latest fraction under a lock, one callback
  outstanding) *and* redraws at most every 100 ms. If a queue-stops-dispatching hang shows up
  again, this is the first place to look, and the real fix is delegate lifetime in
  `Ui.OnMainLoop`.
- **`IProcessRunner` streams, and that replaced the old `inherit` flag.** `inherit: true` handed
  the child the parent's stdout/stderr and returned an empty `ProcessResult` — invisible to a
  GUI, which has no console. The interface now takes an `Action<string>? onOutput` sink called
  per line while the process runs, and still collects the full output. The CLI passes
  `Console.WriteLine`; the GUI appends to a text view. Output is line-buffered now, which is
  what makes curl's meter parseable, above.
- **A prefix's settings live on a pushed page because a rebuilt expander collapses.** Every
  mutation ends in `RefreshAll`, which clears and rebuilds the prefixes list, so settings held
  in an `Adw.ExpanderRow` vanished the moment one was changed. `Adw.NavigationView` is now the
  window's content with the header, switcher and stack as its root page; `PrefixesPage.Refresh`
  refills the open `PrefixPage` in place and pops it only when its prefix is gone.
- **An `Adw.ActionRow` with no activatable widget is not clickable.** `SetActivatable(true)`
  changes nothing on its own — it is the `Gtk.ListBoxRow` default. Measured: the row emitted no
  `activated`, offered AT-SPI no `click` action, and a pointer click did nothing. So a
  navigating row carries a `Ui.RowButton` suffix that is also its `SetActivatableWidget`.
- **`Enrolment` creates the symlink; it did not used to.** The side effect lived inline in the
  CLI's `Enrol`, which meant a second front end would have had to copy it. `Enrolment.Link` now
  owns it and both call it. `Enrolment` still only *prints* the `flatpak override` — the GUI
  must not run it either, for the reason the README gives.
- **The enrol command has to be copyable, and an `Adw.AlertDialog` body is not.** Its body is a
  plain label with no selection, so the command `enrol` exists to hand you could be read and not
  taken — the GUI half of the feature was useless. `EnrolmentDialog` shows each command in its
  own card with a Copy button, and `Ui.Report` is left for short error text.
- **A `Gtk.TextView` collapses to nothing inside a page that scrolls.** The first fix reused
  `Operation`'s monospace non-editable TextView and it rendered as a 1px strip, because a
  TextView's *minimum* height is zero — it expects to be the thing being scrolled, not a block
  inside one. AT-SPI showed the buffer holding the text all along, which is what told layout
  from content apart. Static text that must size to itself is a `Gtk.Label` with
  `SetSelectable(true)` and `Pango.WrapMode.WordChar`.
- **A per-prefix Wayland/X11 switch was asked for and deliberately not built.** yabridge embeds
  plugin editors by X11 reparenting, so the setting could never affect a bridged plugin — only
  `winecfg`, installers and `cabinet run`. `WAYLAND_DISPLAY = ""` stays unconditional.

## Working in this repo

- **`dotnet` leaves two servers running, and inside `flatpak run` they hang the next run.**
  MSBuild worker nodes (`nodeReuse:true`) and Roslyn's `VBCSCompiler` both outlive the command
  that started them by 10–15 minutes, and because `bwrap` waits for every process in its
  namespace they keep that sandbox open long after the check "finished". A second run then finds
  the first one's compiler socket in the shared `/tmp` and connects to a server in a namespace
  that cannot see its files — `scripts/checks.sh` sat at **zero CPU for eight minutes**, which
  reads as slow rather than stuck. It exports `MSBUILDDISABLENODEREUSE=1` and
  `UseSharedCompilation=false` for that reason; the whole run is about 25 seconds, so there is
  nothing for the servers to save. Also the reason never to run a one-off `dotnet` in a sandbox
  of its own beside a check that is already running.
- **A screenshot of an unfocused window is a stale frame, not a broken change.** Chasing one
  cost four rebuilds: the plugin page was pushing correctly all along while `import -window`
  returned a frame from before the click, so the change looked absent and two innocent lines
  were reverted looking for it. AT-SPI showed the truth throughout. The `gui-shot` skill has the
  detail; the rule here is that a GUI change is unproven until the shot is confirmed *current*,
  and that AT-SPI is what to fall back on when the window cannot be brought forward.
- **Check the build's exit code, and do not hide it behind `&&`/`||`.** `flatpak-builder … &&
  flatpak install … || tail -4 log` exits 0 whatever happens, so a failed build reported success
  and the next half hour tested a *stale* install. Two failure modes have shown up unprompted
  and both passed on a plain retry: ILLink dying with `dotnet exited with code 139` during
  `PublishTrimmed`, and `Extension org.freedesktop.Platform.GL.default has invalid merge-dirs`.
  Print `BUILD_EXIT=$?` and read it before believing anything about the installed app. A managed
  assembly stores literals as UTF-16, so `grep` for an ASCII string cannot confirm what shipped
  — search for `text.encode('utf-16-le')` instead.
- **`flatpak install` skips a ref that is already installed**, printing
  `Skipping: … is already installed` and exiting 0, so the second half of the build block does
  nothing and the old build keeps running under a terminal that reported success — the same
  stale install, reached without a failed build. `--or-update` installs or updates, so one line
  covers the first run and every rebuild, and `flatpak info --user <id>` names the commit
  actually deployed — compare it against `ostree --repo=repo rev-parse app/<id>/x86_64/stable`.
- **Never poll for a background command here — the harness already notifies.** Waiting on the
  flatpak build with a `pgrep`/`sleep` loop burned the full 600 s timeout for nothing: the builder
  runs as `flatpak run org.flatpak.Builder`, which `pgrep -f flatpak-builder` does not match, so
  the loop kept sleeping *after* the build had already exited 0. The second attempt failed the
  other way — `while pgrep -f "flatpak.Builder" >/dev/null; do :; done` spun for thirteen minutes,
  because `-f` tests the whole command line and the shell running the loop carries the pattern
  in its own. One pattern is too broad and the other too narrow; there is no third worth
  finding. Start the long command in the background and wait for its completion event. And do
  not pipe a long build through `tail`: the pipeline buffers, so the output file stays empty for
  the whole run and progress cannot be checked at all.
