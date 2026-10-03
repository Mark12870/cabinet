---
name: upstream-patch
description: Decide on, write, test, document or retire a patch Cabinet carries against yabridge or the test Carla. Use before touching `patches/`, `PATCHES.md`, or a yabridge or Carla source file.
---

# Carrying an upstream patch

1. Avoid the patch. Try what Cabinet already owns, one at a time, and note why each fails:
   a prefix runner, DXVK, a Wine virtual desktop (`set <name> desktop`), a per-prefix variable
   (`set <name> env`), a `yabridge.toml` option beside the plugin, a manifest grant, the shim,
   a Library script, a Core change. A patch for the test Carla only ever brings it closer to what
   a DAW does.

2. Ask the user before writing it. Say which of the above were tried and why each failed.

3. Find the upstream report. Search the project's open issues (for yabridge
   `https://api.github.com/search/issues?q=repo:robbert-vdh/yabridge+is:open+<terms>`, for Wine
   bugs.winehq.org). Link only an open issue. If the only report is closed, or there is none,
   draft a new issue (problem, reproduction, fix, what was tested) in the scratchpad and ask the
   user to file it, then link the issue they filed.

4. Write the patch on a clean tree: unpack the pinned source (for yabridge, the archive the
   manifest names, kept under `.flatpak-builder/downloads/<sha256>/`), commit it to a scratch
   git repository, apply every existing patch in the order Cabinet applies them, commit again,
   make the change, and save `git diff` as `patches/<project>/<project>-<what-it-does>.patch`.
   Match the upstream code's own style, including its comments. Change behaviour only where the
   bug is, and keep every other plugin on its old path.

5. Wire it in:
   - yabridge: a `- type: patch` line after the last patch in `io.github.mark12870.cabinet.yml`,
     and a `ManifestTests` fact that asserts its position after the previous patch and the lines
     that make it work.
   - Carla: `scripts/setup-runtime-tests.sh` applies every file in `patches/carla/`, so adding the
     file is enough.

6. Document it in `PATCHES.md`: the open issue link, then **Failure.**, **Patch.** and
   **When it can go.**, with how it was verified.

7. Verify:
   - `scripts/checks.sh`, then the Flatpak build for a yabridge patch, or
     `scripts/setup-runtime-tests.sh` for a Carla patch.
   - For a yabridge patch, before installing the build, open scratch copies of the affected
     projects in REAPER and record each plugin's parameter count and saved state sizes. If the
     patch changes a message between yabridge's two halves, make sure REAPER is closed, since a
     running DAW keeps the old half loaded. Install the build, reopen the same copies and compare:
     only the plugins the patch is for may differ.
   - Update the isolated runtime to the build and run the runtime general suite, plus the
     scenario or probe that covers the failure.

8. Retire a patch when its upstream issue is fixed in the pinned version: remove the patch, its
   manifest line and `ManifestTests` fact if it has them, and its `PATCHES.md` section, then
   verify as in step 7.
