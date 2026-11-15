# Cabinet

Cabinet is a `linux-x64` Flatpak (`io.github.mark12870.cabinet`) that gives Windows VST plugins Wine prefixes of their
own, one per vendor or product family, and bridges them with patched upstream yabridge.

## Goals

- **Ease of use:** plugins install and work without the user configuring Wine, yabridge or compatibility settings.
  Prefer a catalogue entry or a default to a manual command.
- **Reliability:** vendors stay in isolated prefixes on known-good runners, and every fix gets a test or scenario that
  catches it regressing.

## Important

- Branch only when asked; commits go on `main`.
- Each harness takes its permissions from its own config: `opencode.json`, `.codex/config.toml`,
  `.claude/settings.local.json`.
- Cabinet data and prefixes on this machine are the user's. Ask in a separate question before changing any of them.
- AGENTS.md and the skills change only after the user approves it in a separate question. A skill holds the steps of its
  procedure and only the reference those steps need.
- Every feature is available over both the GUI and the CLI.
- README.md stays under 100 lines; every other `*.md` under 500.
- Source, tests, fixtures, docs, generated files and commit messages carry neutral synthetic values only: no
  usernames, home paths, credentials or local tool state.

### Code style

Keep it minimal and readable - no dead config, no scaffolding that isn't used. The same goes for prose: a doc fix should
not come out longer than what it replaced. Follow the basic Clean Code rules.

**The code carries no comments.** Not XML doc tags, not a header on every member, and above all not a note arguing for
why the code is written the way it is - that is a message to a reviewer, not to whoever maintains this next. Say it in
the name or the structure instead. Anything that genuinely will not fit there is a *gotcha*.

## Boundaries

- `Cabinet.slnx` is a .NET 10 solution. `src/Cabinet.Core` owns prefixes, runners, the bridge and the sandbox;
  `src/Cabinet.Library` the catalogue, installs and prefix setups; `src/Cabinet.Doctor` diagnosis and About, over both.
  Dependencies point one way: Core ← Library ← Doctor ← Cli, Gui. `Cabinet.Cli` only parses and renders the command
  line, and `Cabinet.Gui` only wires GTK4/libadwaita through GirCore. The CLI is NativeAOT; the GUI is trimmed and
  self-contained. Keep all three operation projects AOT-safe, and put new behaviour in the lowest one that can own it.
- The languages are C#, Rust and Bash. Rust is limited to the dependency-free `shim/` (`cabinet-wine`), which runs on
  the plugin-load path inside foreign sandboxes, and `tools/wine-probes`, Windows programs the runtime tests run in a
  prefix. `msi-shim/` is the only C. Application code and tests are C#.
- `tools/` (`Tools.slnx`) holds what never ships: `Cabinet.Ci` for the CI reports, `Cabinet.Probes` for the runtime
  tests' Carla and lock probes, and `wine-probes`. Nothing in it reaches the Flatpak.
- `msi-shim/` is a 32-bit C DLL built with the SDK's LLVM (clang, lld-link), with no C runtime. It stands in for
  Wine's msi.dll only in Kontakt 8's installer, whose Windows Installer call wedges under Wine, and forwards everything
  else to the runner's own msi.dll. Keep it that narrow.
- Every project sets `TreatWarningsAsErrors`; a warning fails the build.
- **A yabridge patch is the last resort.** Load `upstream-patch` before touching `patches/`; it lists what Cabinet
  already owns to try first, and the user approves every new patch.
- Identifiers and user-facing text use British spelling: `Enrolment`, `Licence`, `catalogue`.
  `enroll` exists only as a CLI alias for `enrol`.
- `flatpak/io.github.mark12870.cabinet.svg` is the recoloured Phosphor dresser icon; retain
  `flatpak/LICENSE.phosphor` and the README credit if it changes.
- `scripts/`, `site/` and `.github/workflows/` own packaging and publishing; `.agents/skills/` (linked as
  `.claude/skills`) holds one procedural skill per repository procedure.

## Architecture and filesystem boundary

Read `docs/ARCHITECTURE.md` before changing the shim, sessions, prefix locks, the bridge, enrolment, process
supervision, or yabridge's sockets and `YABRIDGE_TEMP_DIR`. Two rules bind every change:

- Everything Cabinet owns, including prefixes and runners, stays under `~/.var/app/io.github.mark12870.cabinet/`;
  use that Bottles-style boundary for new code. The exception is what a DAW loads: yabridgectl's output and the
  native plugin bundles live in the DAW scan paths themselves (`cabinet/windows` and `cabinet/native` in `~/.vst3`,
  `~/.vst`, `~/.clap`; `~/.lv2` flat, as hosts scan it one level deep), so an enrolled DAW needs no grant into
  Cabinet's data for them. yabridgectl still writes through `bridge/<scan dir>/yabridge`, a link to the scan path,
  and each native install keeps links to its bundles under `native/<id>`. yabridge sockets use
  `$XDG_RUNTIME_DIR/yabridge`. Other intentional external locations are the per-file links older releases left in
  `~/.local/share/yabridge`, which bridging removes, and a Library entry's declared `Data:` directory. Add no other
  writes in `$HOME`.
- The manifest grants no `home`: Wine maps `Z:` to `/`, so every installer and plugin would read all of `$HOME`. A
  new path Cabinet reads or writes needs its own grant; a new Library `Data:` root needs a matching
  `--filesystem=~/<root>:create` grant in `flatpak/io.github.mark12870.cabinet.yml`. Files the user picks come through the
  FileChooser portal, which exposes only the chosen file, so an installer that needs the `.bin` or `.cab` files beside
  it needs a `flatpak override --filesystem=<dir>:ro` for its folder.

## Writing code

- An operation is `sealed class X(Layout layout, IProcessRunner runner)`. Every path, marker name and bundled
  location comes from a `Layout` member; every subprocess goes through the injected `IProcessRunner`. Only each front
  end's `Program` constructs a real `ProcessRunner`, which keeps Core testable; do not call `Process.Start` or write a
  path literal in an operation.
- Tests are xunit, one project per source project, sharing helpers from `tests/Cabinet.Testing`. `Repo` walks up to
  `Cabinet.slnx`, so `CatalogueTests`, `ManifestTests` and `ShimParityTests` read the real tree. Substitute a runner
  with `StubRunner`, `RecordingRunner` or `StreamingRunner` instead of a mocking library.
- Tests are deterministic and straight-line: no conditions in them.
- A new CLI verb also belongs in `Program.Usage`. Every command parses its arguments through `CommandLine` before
  bootstrap or any side effect; `--json` is accepted only where a command asks for it with `Then(json => …)`, and
  nothing after `run <name>` or `winetricks <name>` is parsed. JSON is written by hand with `Utf8JsonWriter` in
  `src/Cabinet.Cli/Json.cs` because NativeAOT trims reflection-based serialization.
- In the GUI, user-visible operations with progress or logs use `Operation.Run`. Background page loads may use
  `Task.Run`, but every widget update from off the main loop must go through
  `Ui.OnMainLoop`. Take page chrome from `Ui` and icon names from `Icons`.

## Checks and builds

Load `build-and-test` to check, build or install Cabinet; `scripts/checks.sh` is the full CI-equivalent check, and the
pre-commit hook (`git config core.hooksPath .githooks`) runs it on what is staged. Commit with the hook; never pass
`--no-verify`.

GUI changes need visual confirmation against the installed Flatpak: load `gui-shot`.

`flatpak/nuget-sources.json` feeds the Flatpak's offline build and is generated by
`flatpak-dotnet-generator.py`. NativeAOT pulls ILCompiler from NuGet, so every build needs it; regenerate it whenever a
dependency changes.

## Runtime testing

Load `runtime-test` before any runtime test or diagnosis, and `plugin-scenario` for one entry's scenario. Every mutating
catalogue operation runs in the isolated Toolbox that `scripts/setup-runtime-tests.sh` prepares, never against the
host Cabinet data. `Cabinet.Runtime.Tests` holds three kinds of test, and `RuntimeSuiteTests` refuses
any other: `CarlaTests`, the plugin matrix run on every push; the plugin scenarios in `Scenarios/`, run by the `Plugins`
workflow; and the probes in `Patches/`, which say when a `PATCHES.md` patch can go. Locally, run only the scenarios a
change touches, together with `--parallel collections`.

## Catalogue and safeguards

- `data/library/<vendor>/` contains that vendor's `.yml` entries, optional shared `.sh` installer, and artwork; the
  manifest installs the directory to `/app/share/cabinet/library/<vendor>/`. Entry IDs are global. Keep artwork
  provenance in `SOURCES.md`. Load `library-entry` to add or change an entry.
- A vendor directory that holds any `.md` stays in the tree and under test, the manifest's install loop leaves it out of
  the build, and the `.md` says why. `CatalogueTests` guards the pairing.
- When a runner is pinned or updated, move every prefix that works on it to the newest runner, bumping
  its `Revision:`, so users download as few runners as possible. Keep a prefix on an older one only for a
  reason its vendor doc records.
- Prefer a working native entry. Windows entries use a tagged, SHA-256-pinned download; a `rolling` source carries only
  a URL, and `byo` takes the file the user fetched from the vendor's page. Library scripts may only operate in the
  directories Cabinet passes them; Cabinet owns linking, recording, and removal.
- Write the guard, not the paragraph: `CatalogueTests`, `ManifestTests`, and `ShimParityTests`
  guard catalogue, sandbox permissions, and the Core/shim contract. Add a test for a tree invariant instead of
  documenting it only in prose. Put user-actionable failures in `doctor`, shared by the CLI and GUI, and report only
  facts true of the current machine.

## Releases

The newest `<release>` in `flatpak/io.github.mark12870.cabinet.metainfo.xml` is Cabinet's only version; do not put a product
version in a project file. `scripts/update-yabridge.sh` updates the manifest URL and hash.

Publishing is automatic and irreversible: every push to `main` runs `ci.yml`, which, once its checks pass, builds, signs
and deploys to Pages only when that newest `<release>` differs from the version already published, keeping ten commits
of rollback. So a metainfo bump on `main` is the release. Read `.agents/skills/bump/SKILL.md` before changing the
metainfo, signing, or the published OSTree repository.

Flatpak fetches an app's `add-extensions` only from the remote the app came from, so `scripts/mirror-extensions.sh`
re-commits the auto-downloaded Flathub ones into Cabinet's repo on every release, and a manual run
republishes the repo already out when one of them changed. That refresh never rebuilds the app, but it renders the
site from `main`. `ManifestTests` keeps the mirrored list in step with the manifest.

## Subagents and escalation

Use whatever agents the harness offers for these roles; the names in brackets are OpenCode's and Claude Code's.

- **Explorer** (`explore`, `Explore`), read-only, for open-ended searches when the relevant files and conventions are
  not yet known.
- **General** (`general`, `general-purpose`) for complex, independent multi-step work that does not duplicate the main
  task.
- **Implementer** (`implementer`, `general-purpose`) for a well-scoped change with clear acceptance criteria; keep
  trivial or tightly coupled work inline.
- **Tester** (`code-tester`, `general-purpose`) to design and run verification for a feature or fix, GUI checks
  included.
- **Debugger** (`debugger`, `general-purpose`), an independent second opinion, when the root cause is unclear after one
  focused investigation, when a fix or verification fails, or after two substantially different attempts fail.
  Escalate rather than repeat speculative fixes; ask the user instead when only they hold the missing fact,
  observation or decision.
- **Reviewer** (`code-reviewer`, `general-purpose`), exactly once, after a code change is implemented, tested and
  fixed, immediately before the final response.

Run independent subagent tasks in parallel, the reviewer excepted.
