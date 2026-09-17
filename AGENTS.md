# Cabinet

Cabinet is a `linux-x64` Flatpak (`io.github.mark12870.cabinet`) that gives Windows VST plugins Wine prefixes of their
own, one per vendor or product family, and bridges them with patched upstream yabridge. There is deliberately no arm build.

## Important

- Don't create a new branch if not asked for that!
- Never modify my Cabinet data and prefixes without asking and approval. You must let me know in separate question,
  otherwise it is forbidden.
- Every feature must be available over GUI and over CLI
- README.md must be under 100 lines
- CLAUDE.md must be under 500 lines
- Never commit personal, account or machine-specific data, including usernames, home paths, credentials or local tool
  state, in source, tests, fixtures, documentation, generated files or commit messages; use neutral synthetic values
  instead.

### Code style

Keep it minimal and readable - no dead config, no scaffolding that isn't used. The same goes for prose: a doc fix should
not come out longer than what it replaced. Follow the basic Clean Code rules.

**The code carries no comments.** Not XML doc tags, not a header on every member, and above all not a note arguing for
why the code is written the way it is - that is a message to a reviewer, not to whoever maintains this next. Say it in
the name or the structure instead. Anything that genuinely will not fit there is a *gotcha*.

### SKILLS

- Never modify the CLAUDE.md, AGENTS.md or skills without asking and approval. You must let me know in separate question,
  otherwise it is forbidden.
- Skills should always include only the steps to produce the skill.

## Boundaries

- `Cabinet.slnx` is a .NET 10 solution. `src/Cabinet.Core` owns operations; `Cabinet.Cli` only parses and renders the
  command line, and `Cabinet.Gui` only wires GTK4/libadwaita through GirCore. The CLI is NativeAOT; the GUI is trimmed
  self-contained, not AOT. Keep Core AOT-safe, and put new behaviour there.
- Rust is limited to the dependency-free `shim/` (`cabinet-wine`); application code and tests are C#. The shim is Rust
  because it runs on the plugin-load path inside foreign sandboxes.
- All three source projects set `TreatWarningsAsErrors`; a warning fails the build.
- **A yabridge patch is the last resort, never the first fix.** Every patch in `patches/` is a build Cabinet has to
  carry, rebase and retire by hand. Exhaust what Cabinet already owns first: a prefix runner, DXVK, a Wine virtual
  desktop (`set <name> desktop`), a per-prefix variable (`set <name> env`), a `yabridge.toml` option beside the
  plugin, a manifest grant, or the shim. Patch only once those are shown not to work, say which were tried and why
  they failed, and ask before writing one.
- Identifiers and user-facing text use British spelling: `Enrolment`, `Licence`, `catalogue`.
  `enroll` exists only as a CLI alias for `enrol`.
- `data/io.github.mark12870.cabinet.svg` is the recoloured Phosphor dresser icon; retain
  `data/LICENSE.phosphor` and the README credit if it changes.
- `CLAUDE.md` points here.
- `scripts/`, `site/` and `.github/workflows/` own packaging and publishing; `.claude/skills/`
  holds one procedural skill per repository procedure. Skills should contain only the steps to produce the skill.

## Runtime architecture

- `Bootstrap.Ensure` runs from both entry points and creates Cabinet's prefixes directory plus the yabridgectl link.
  There is no `setup` command.
- Only Wine runs inside the Cabinet sandbox for the yabridge bridge. The DAW reads yabridge's host-side halves from the
  installed Flatpak's `current/active/files`; `enrol` creates the DAW's
  `data/yabridge` link and prints, but does not apply, the required `flatpak override` because it grants
  `org.freedesktop.Flatpak` and lets the DAW run commands on the host.
- The crossing is `$WINELOADER`: yabridge's winegcc wrapper execs `shim/src/main.rs`, which hands the plugin to a Wine
  session and exits with that job's status, so yabridge's liveness check on the loader PID tracks the real host. The
  manifest rewrites that wrapper's fallback from bare `wine` to the shim beside it, so a DAW that sets no `WINELOADER`
  bridges instead of aborting; the shim in turn forces `YABRIDGE_NO_WATCHDOG=1` (`FORCED`) rather than forwarding it,
  because the watchdog is PID-based and misfires across the boundary.
  Preserve `YABRIDGE_TEMP_DIR`. The shim duplicates three sets of constants, and
  `ShimParityTests` compares each against its C# side: marker names against `Layout`, sync and Cabinet-owned variables
  against
  `PrefixSettings`, blanked sockets against `Prefixes`. Prefixes need no registration: yabridge finds one by walking
  from a plugin `.dll` to its `dosdevices` directory.
- One Wine session per prefix, never one per plugin. Flatpak gives every `flatpak run` its own PID namespace but binds
  `/tmp` per app, so two sandboxes over one `WINEPREFIX` reach the same wineserver socket from either side of a
  namespace boundary and wedge the DAW; that is what froze REAPER on a project holding five Klevgrand plugins.
  `shim/src/session.rs` keys a session on the canonical `WINEPREFIX`, starts it once behind an `flock` in
  `YABRIDGE_TEMP_DIR`, and every later plugin sends its argv to that session over the session socket. Because the
  session outlives the shim that started it, it carries neither `--die-with-parent` nor `--watch-bus`. Once idle it
  retires and runs `wineserver -k` while still holding the session lock: Windows services a plugin started keep the
  old sandbox and wineserver alive otherwise, and the next session on that prefix crashes or hangs against them.
- A session reads `.cabinet-env` again for every job it starts, so `cabinet set <prefix> env` reaches the next plugin
  without restarting it. `.cabinet-sync` is fixed when the session starts, on the outer shim's `flatpak run`: every
  Wine process must use the sync mode of the wineserver it joins, and staging-based runners exit on a mismatch.
- Wine does not keep a job's processes under the process that started them, so a session finds and kills a job by
  process group, never by ancestry. `wineserver` and `winedevice.exe` put themselves in their own groups and so survive
  between jobs, which is exactly what sharing a session requires.
- A yabridge host is dead once its main thread is. Wine can lose that thread, to a stack overflow for one, while
  other threads keep the process and its pidfd alive; the leader shows as a zombie (`Zl`) and the DAW waits on it
  forever. `supervise` ends a job whose `yabridge-host` leader is a zombie; any other program may outlive its main
  thread. yabridge's plugin side then aborts the DAW at teardown, so this turns a hang into a crash, nothing more.
- Everything Cabinet owns, including prefixes, runners and native plugin files, stays under
  `~/.var/app/io.github.mark12870.cabinet/`; use that Bottles-style boundary for new code. yabridge sockets use
  `$XDG_RUNTIME_DIR/yabridge`. Other intentional external locations are DAW scan/link paths (`~/.vst3`, `~/.vst`,
  `~/.clap`, `~/.lv2`, each DAW's
  `~/.var/app/<daw>/data/yabridge`, and `~/.local/share/yabridge` for a DAW outside Flatpak) and a Library entry's
  declared `Data:` directory. Do not add arbitrary writes in
  `$HOME`.
- The manifest keeps `$HOME` read-only. A new Library `Data:` root also needs a matching
  `--filesystem=~/<root>:create` grant in `io.github.mark12870.cabinet.yml`.

## Writing code

- A Core operation is `sealed class X(Layout layout, IProcessRunner runner)`. Every path, marker name and bundled
  location comes from a `Layout` member; every subprocess goes through the injected `IProcessRunner`. Only each front
  end's `Program` constructs a real `ProcessRunner`, which keeps Core testable; do not call `Process.Start` or write a
  path literal in an operation.
- Tests are xunit. `Repo` walks up to `Cabinet.slnx`, so `CatalogueTests`, `ManifestTests` and
  `ShimParityTests` read the real tree rather than a fixture. Substitute a runner with
  `StubRunner`, `RecordingRunner` or `StreamingRunner` instead of a mocking library.
- Tests must be always deterministic. No conditions are allowed in them.
- A new CLI verb also belongs in `Program.Usage`. `--json` is stripped from the arguments before dispatch, and JSON is
  written by hand with `Utf8JsonWriter` in `src/Cabinet.Cli/Json.cs` because NativeAOT has no reflection-based
  serializer.
- In the GUI, user-visible operations with progress or logs use `Operation.Run`. Background page loads may use
  `Task.Run`, but every widget update from off the main loop must go through
  `Ui.OnMainLoop`. Take page chrome from `Ui` and icon names from `Icons`.

## Checks and builds

Enable the hook once in a clone:

```sh
git config core.hooksPath .githooks
```

Run the complete CI-equivalent check from the repository root:

```sh
scripts/checks.sh
```

It enters `org.gnome.Sdk//50` itself and runs formatting, both front-end builds, Core tests, AppStream validation, and
shim fmt/clippy/tests. The GUI build needs
`-p:UseSharedCompilation=false`; Core tests do not compile either front end.

A focused Core test runs against host `dotnet 10`:

```sh
dotnet test tests/Cabinet.Core.Tests --filter 'FullyQualifiedName~CatalogueTests'
```

On the intended Silverblue host, `cargo` comes from the `rust-stable` SDK extension rather than the host PATH. Run a
focused shim test in the same SDK shell:

```sh
flatpak run --filesystem="$PWD" --command=sh org.gnome.Sdk//50 -c \
  'export PATH=/usr/lib/sdk/rust-stable/bin:$PATH; cd shim; cargo test native_daw_does_not_hop_through_the_host'
```

For a front-end-only compile, use `dotnet build src/Cabinet.Cli --nologo -v q` or
`dotnet build src/Cabinet.Gui --nologo -v q -p:UseSharedCompilation=false`.

The pre-commit hook invokes `scripts/checks.sh --staged`; it is selective and skips `dotnet test`, so run the full
script before declaring work verified. Commit with the hook enabled; never pass
`--no-verify`.

For a local Flatpak build, use `--disable-rofiles-fuse` and update the installed app rather than relying on an old
commit:

```sh
flatpak run org.flatpak.Builder --repo=repo --force-clean --disable-rofiles-fuse \
  --default-branch=stable build io.github.mark12870.cabinet.yml
flatpak remote-add --user --if-not-exists --no-gpg-verify cabinet-local "file://$PWD/repo"
flatpak install --user -y --or-update cabinet-local io.github.mark12870.cabinet
```

yabridge compiles with `meson compile -j1`. Its 64- and 32-bit hosts are each one `--unity-size=1000` translation unit;
compiled in parallel they take about 10 GB, the OOM killer takes `cc1plus`, and the builder dies at yabridge's final
link looking like a timeout. A serial build of the whole Flatpak takes about ten minutes.

GUI changes need visual confirmation against the installed Flatpak:
`scripts/gui-shot.sh About about.png` (or `Prefixes/<row>`); its header and the `gui-shot` skill describe the required
toolbox.

`nuget-sources.json` feeds the Flatpak's offline build and is generated by
`flatpak-dotnet-generator.py`. It is required even with no third-party package, because NativeAOT pulls ILCompiler from
NuGet; regenerate it whenever a dependency changes.

## Runtime diagnosis

- Load `build-and-test` and `runtime-test` before any mutating catalogue runtime operation.
- Never install, launch, migrate, reconfigure or remove a catalogue entry through the host Cabinet data while testing.
  Use the isolated Toolbox and runtime root prepared by `scripts/setup-runtime-tests.sh`.
- Never change an existing host prefix for an experiment. Use a fresh isolated prefix for each runner or settings
  comparison, and change one variable at a time.
- Test managers through `cabinet library launch <id>`, not `cabinet run`; their launch and standard-stream paths differ.
- Before adding flags or changing configuration, compare the complete configuration of the nearest working entry.
  Check a relevant upstream or community installer when one exists.
- Treat a manager launch as successful only when its expected window and renderer or helper processes are present and
  its launch log has no fatal error. Process survival alone is insufficient.
- After two substantially different runtime attempts fail, use the debugger subagent before trying more runners or
  flags.
- An editor can be exercised without a DAW: drive Carla's `carla_backend` from Python in the isolated runtime
  (`engine_init("Dummy")`, `add_plugin`, `show_custom_ui`, a timed `engine_idle` loop, `engine_close`) with `DISPLAY`
  kept, under `timeout`. A stall in `engine_close` is the DAW freeze.
- Without `YABRIDGE_TEMP_DIR` yabridge puts its sockets in `$XDG_RUNTIME_DIR` itself, which the manifest cannot grant
  (`--filesystem=xdg-run` is refused; `xdg-run` needs a subdirectory). The shim grants that path by value on every
  `flatpak run`, so a DAW that sets nothing still reaches its sockets.
- A native DAW needs no enrolment: every successful bridge links `~/.vst3/cabinet`, `~/.clap/cabinet` and
  `~/.vst/cabinet` to Cabinet's own yabridgectl output, reports a path something else owns instead of replacing it,
  and takes out the per-file links older releases left in `~/.local/share/yabridge`. yabridge's chainloader never
  looks beside itself, and every yabridge puts the same names in `~/.local/share/yabridge`, so
  `patches/yabridge-chainloader-cabinet-first.patch` makes Cabinet's copies load Cabinet's installed library and host
  first; nothing is published beside the plugins, because `yabridgectl sync` prunes `.so` files it did not make.
  A findable bridge with no reachable loader aborts the DAW from yabridge's launch thread rather than failing the
  plugin; the manifest's `WINELOADER` fallback rewrite is what keeps the loader reachable. A long
  `YABRIDGE_TEMP_DIR` breaks the sockets with `File name too long`, so leave it under `/run/user/<uid>`.
  Redirecting `XDG_DATA_HOME` to isolate it also hides
  the user Flatpak installation from the shim's `flatpak run` (`app/io.github.mark12870.cabinet/x86_64/master not
  installed`); set `FLATPAK_USER_DIR=~/.local/share/flatpak` beside it. Native REAPER quit from a ReaScript
  (`Main_OnCommand(40004)`) with a bridged plugin loaded stalled for 9 s to over 100 s while `YABRIDGE_DEBUG_LEVEL=1`
  showed no bridge call outstanding; run it under `timeout` and read the ReaScript's own output, not the exit.
- Run `library install` with `DISPLAY` set: a vendor installer can install nothing without one, and the script then
  fails on the missing plugin.
- Wine runs every process elevated, and WebView2 ignores `WEBVIEW2_*` variables and `HKCU` policy for an elevated host.
  Browser flags go in `HKLM\Software\Policies\Microsoft\Edge\WebView2\AdditionalBrowserArguments`, valued by host
  executable (`yabridge-host.exe`); `CatalogueTests` refuses the other two. Read `msedgewebview2.exe`'s
  `/proc/<pid>/cmdline` before crediting a flag with any effect.

## Catalogue and safeguards

- `data/library/<vendor>/` contains that vendor's `.yml` entries, optional shared `.sh` installer, and artwork; the
  manifest installs the directory to `/app/share/cabinet/library/<vendor>/`. Entry IDs are global. Keep artwork
  provenance in `SOURCES.md`.
- A vendor directory that holds any `.md` is skipped by the manifest's install loop: the entry stays in the tree and
  under test but does not ship, and the `.md` says why (`data/library/spitfire-audio/SPITFIRE.md`). `CatalogueTests`
  guards the pairing.
- Prefer a working native entry. Windows entries use a tagged, SHA-256-pinned download; a
  `rolling` source has no checksum/version, and `byo` has no Cabinet download URL/checksum. Library scripts may only
  operate in the directories Cabinet passes them; Cabinet owns linking, recording, and removal.
- Write the guard, not the paragraph: `CatalogueTests`, `ManifestTests`, and `ShimParityTests`
  guard catalogue, sandbox permissions, and the Core/shim contract. Add a test for a tree invariant instead of
  documenting it only in prose. Put user-actionable failures in `doctor`, shared by the CLI and GUI, and report only
  facts true of the current machine.

## Releases

The newest `<release>` in `io.github.mark12870.cabinet.metainfo.xml` is Cabinet's only version; do not put a product
version in a project file. `scripts/update-yabridge.py` updates only the manifest URL/hash and does not choose a Cabinet
release.

Publishing is automatic and irreversible: every push to `main` runs `ci.yml`, which, once its checks pass, builds,
signs and deploys to Pages only when that newest `<release>` differs from the version already published, keeping ten
commits of rollback. So a metainfo bump on `main` is the release. Read the `bump` skill before changing the metainfo,
signing, or the published OSTree repository.

## When stuck

If progress depends on information, a runtime observation, or a decision that only the user can provide, ask the user
instead of guessing.

If two substantially different attempts fail, stop and either:

- delegate to the debugger subagent, or
- ask the user a focused question if user input could resolve the blocker.

Do not consume the remaining step budget repeating similar investigations.

## Subagents

- Use the `explore` subagent for open-ended repository searches or when the relevant files and conventions are not yet
  known.
- Use the `general` subagent for complex, independent multi-step work that can be completed without duplicating the main
  task.
- You MUST use the `debugger` subagent for difficult or uncertain reasoning, when verification or user feedback
  indicates that acceptance criteria are not met, and when investigating the root cause of bugs or unexpected behavior.
- You MUST use the `code-tester` subagent to design or run verification for a new feature or fix. Use it also for GUI
  verifications.
- You MUST run the `code-reviewer` subagent exactly once after all implementation, testing, and resulting fixes are
  complete, immediately before the final response, and never during intermediate changes. Run it only when you changed
  the code!
- Run independent subagent tasks in parallel when possible, except `code-reviewer`.
