# CLAUDE.md

Wine packaged as a Flatpak so Windows VST plugins run on Fedora Silverblue, one Wine prefix
per plugin, bridged into a DAW by upstream yabridge. App ID `io.github.mark12870.cabinet`.
`x86_64` only — the maintainer runs one machine, so there is deliberately no arm build.

Packaging follows `../beeper-flatpak`: self-hosted GPG-signed OSTree repo on GitHub Pages,
daily version-bump workflow, same publishing gotchas.

## Important

- Don't create a new branch if not asked for that!
- Every feature must be available over GUI and over CLI
- README.md must be under 100 lines
- CLAUDE.md must be under 500 lines

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

### SKILLS

- Never modify the CLAUDE.md or Claude skills without asking and approval.
- Skills should always include only the steps to produce the skill.

## Layout

- `shim/` — `cabinet-wine`, the `$WINELOADER` shim. Std-only, no dependencies.
- `src/Cabinet.Core/` — every operation, as a library. Both front ends reference this.
- `src/Cabinet.Cli/` — argument parsing and rendering only, NativeAOT.
- `src/Cabinet.Gui/` — GTK4 + libadwaita through GirCore. Trimmed, not NativeAOT.
- `data/` — the app icon: Phosphor's *dresser* duotone, recoloured, MIT, keep the licence
  beside it and the credit in the README. `data/library/` is the plugin catalogue, **one
  directory per vendor** holding its `.yml` entries, the `.sh` its installs need and the icon
  and screenshot its pages show, installed to `/app/share/cabinet/library/<vendor>/`.
- `scripts/`, `site/`, `.github/workflows/` — packaging and publishing.
- `.claude/skills/` — one skill per procedure in this repo.

## The architecture, in one paragraph

yabridge's DAW-side halves cannot live in the sandbox, because the DAW does not — but they
are not copied out either: the DAW reads them from the installed Flatpak's own
`current/active/files`, granted read-only by `enrol`'s override. Only Wine runs inside. There
is **no `setup` command**; `Bootstrap.Ensure` makes the one link Cabinet needs on every
invocation. The crossing happens at `$WINELOADER`, which yabridge's winegcc wrapper execs —
upstream supports this through `YABRIDGE_TEMP_DIR` and `YABRIDGE_NO_WATCHDOG`. Prefixes need
no registration: yabridge walks up from the plugin `.dll` for a `dosdevices` directory.

**Everything Cabinet owns stays in `~/.var/app/io.github.mark12870.cabinet/`, prefixes
included** — the Bottles model, and the standard to hold new code to. Three paths outside it
are unavoidable: `~/.vst3/yabridge/…` (where DAWs scan), `~/.var/app/<daw>/data/yabridge`
(the chainloader's compiled-in search path), and a Library entry's `Data:` directory, for a
plugin that reads a fixed path under `$HOME` and would otherwise load without its presets.
Anything else in `$HOME` is a bug.

## Gotchas

Every gotcha this project has cost — the sandbox and its masks, the manifest and the runtime,
Wine prefixes and runners, DXVK and plugin editors, the Library, yabridge, the front ends and
working in this repo — is in [GOTCHAS.md](GOTCHAS.md). Read it before theorising about a
failure, and add a new one there rather than here, so this file stays short.
