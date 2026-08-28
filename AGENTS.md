# Cabinet

Cabinet is a `linux-x64` Flatpak (`io.github.mark12870.cabinet`) that gives Windows VST plugins one Wine prefix each and
bridges them with upstream yabridge. There is deliberately no arm build.

## Important

- Don't create a new branch if not asked for that!
- Every feature must be available over GUI and over CLI
- README.md must be under 100 lines
- CLAUDE.md must be under 500 lines
- Never commit personal, account or machine-specific data, including usernames, home paths, credentials or local tool state, in source, tests, fixtures, documentation, generated files or commit messages; use neutral synthetic values instead.

### Code style

Keep it minimal and readable - no dead config, no scaffolding that isn't used. The same goes for prose: a doc fix should
not come out longer than what it replaced. Follow the basic Clean Code rules.

**The code carries no comments.** Not XML doc tags, not a header on every member, and above all not a note arguing for
why the code is written the way it is - that is a message to a reviewer, not to whoever maintains this next. Say it in
the name or the structure instead. Anything that genuinely will not fit there is a *gotcha*.

### SKILLS

- Never modify the CLAUDE.md or Claude skills without asking and approval. You must let me know in separate question,
  otherwise it is forbidden.
- Skills should always include only the steps to produce the skill.

## Boundaries

- `Cabinet.slnx` is a .NET 10 solution. `src/Cabinet.Core` owns operations; `Cabinet.Cli` only parses and renders the
  command line, and `Cabinet.Gui` only wires GTK4/libadwaita through GirCore. The CLI is NativeAOT; the GUI is trimmed
  self-contained, not AOT. Keep Core AOT-safe, and put new behaviour there.
- Rust is limited to the dependency-free `shim/` (`cabinet-wine`); application code and tests are C#. The shim is Rust
  because it runs on the plugin-load path inside foreign sandboxes.
- All three source projects set `TreatWarningsAsErrors`; a warning fails the build.
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
- The crossing is `$WINELOADER`: yabridge's winegcc wrapper execs `shim/src/main.rs`, which launches Cabinet with
  `flatpak run`. Preserve `YABRIDGE_TEMP_DIR` and `YABRIDGE_NO_WATCHDOG`. The shim duplicates three sets of constants,
  and `ShimParityTests` compares each against its C# side: marker names against `Layout`, sync and Cabinet-owned
  variables against
  `PrefixSettings`, blanked sockets against `Prefixes`. Prefixes need no registration: yabridge finds one by walking
  from a plugin `.dll` to its `dosdevices` directory.
- Everything Cabinet owns, including prefixes, runners and native plugin files, stays under
  `~/.var/app/io.github.mark12870.cabinet/`; use that Bottles-style boundary for new code. yabridge sockets use
  `$XDG_RUNTIME_DIR/yabridge`. Other intentional external locations are DAW scan/link paths (`~/.vst3`, `~/.vst`,
  `~/.clap`, `~/.lv2`, and each DAW's
  `~/.var/app/<daw>/data/yabridge`) and a Library entry's declared `Data:` directory. Do not add arbitrary writes in
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

GUI changes need visual confirmation against the installed Flatpak:
`scripts/gui-shot.sh About about.png` (or `Prefixes/<row>`); its header and the `gui-shot` skill describe the required
toolbox.

`nuget-sources.json` feeds the Flatpak's offline build and is generated by
`flatpak-dotnet-generator.py`. It is required even with no third-party package, because NativeAOT pulls ILCompiler from
NuGet; regenerate it whenever a dependency changes.

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

Publishing is automatic and irreversible: a push to `main` touching
`io.github.mark12870.cabinet.*`, `src/**`, `shim/**` or `nuget-sources.json` runs
`build-publish.yml`, which builds, signs and deploys to Pages only when that newest `<release>`
differs from the version already published, keeping ten commits of rollback. So a metainfo bump on `main` is the
release. Read the `release` skill before changing the metainfo, signing, or the published OSTree repository.

## When stuck

If progress depends on information, a runtime observation, or a decision that only the user can provide, ask the user
instead of guessing.

If two substantially different attempts fail, stop and either:

- delegate to the debugger subagent, or
- ask the user a focused question if user input could resolve the blocker.

Do not consume the remaining step budget repeating similar investigations.

## Subagents

- Use an `explore` subagent for open-ended repository searches or when the relevant files and conventions are not yet
  known.
- Use a `general` subagent for complex, independent multi-step work that can be completed without duplicating the main
  task.
- Use `debugger` subagent for difficult or uncertain reasoning, or when verification or user feedback shows acceptance criteria
  are unmet. Apply its advice before asking, giving up, or finalising.
- You MUST use the `code-tester` subagent to design or run verification for a new feature or fix. Use it also for GUI
  verifications.
- You MUST run the `code-reviewer` subagent exactly once after all implementation, testing, and resulting fixes are
  complete, immediately before the final response, and never during intermediate changes.
- Run independent subagent tasks in parallel when possible, except `code-reviewer`.
