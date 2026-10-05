---
name: runtime-test
description: Test Cabinet catalogue entries through an isolated Toolbox and Carla.
---

# Test catalogue entries

1. Build the current Flatpak. Pass its exact OSTree commit as
   `CABINET_RUNTIME_CABINET_REF` when preparing the runtime; do not rely on an
   older installed commit.

2. Prepare the isolated Toolbox, private Cabinet and REAPER Flatpaks, pinned full Carla
   build, and catalogue fixtures:

   ```sh
   COMMIT=$(ostree --repo=repo rev-parse app/io.github.mark12870.cabinet/x86_64/stable)
   CABINET_RUNTIME_CABINET_REF="io.github.mark12870.cabinet/x86_64/stable/$COMMIT" \
     scripts/setup-runtime-tests.sh
   ```

3. Run the general runtime suite, the plugin matrix alone:

   ```sh
   toolbox run --container cabinet-runtime \
     dotnet build tests/Cabinet.Runtime.Tests --nologo \
     -m:1 -p:BuildInParallel=false -p:RestoreDisableParallel=true
   toolbox run --container cabinet-runtime \
     dotnet test --project tests/Cabinet.Runtime.Tests --no-build \
     --filter-not-namespace Cabinet.Runtime.Tests.Scenarios \
     --filter-not-namespace Cabinet.Runtime.Tests.Patches
   ```

   For one plugin entry, run its scenario alone as the `plugin-scenario` skill describes.

4. For a manager entry, create a fresh prefix in the isolated runtime for each
   configuration attempt. Install the entry into that prefix and launch it with
   `cabinet library launch <id>`.

   ```sh
   ROOT="${CABINET_RUNTIME_ROOT:-${XDG_CACHE_HOME:-$HOME/.cache}/cabinet-rt}"
   toolbox run --container cabinet-runtime env \
     HOME="$ROOT/home" XDG_RUNTIME_DIR="$ROOT/runtime" \
     XDG_DATA_HOME="$ROOT/home/.local/share" \
     FLATPAK_USER_DIR="$ROOT/home/.local/share/flatpak" \
     flatpak run --nofilesystem=home --filesystem="$ROOT":create \
     io.github.mark12870.cabinet library install <id> --prefix <fresh-prefix>
   toolbox run --container cabinet-runtime env \
     HOME="$ROOT/home" XDG_RUNTIME_DIR="$ROOT/runtime" \
     XDG_DATA_HOME="$ROOT/home/.local/share" \
     FLATPAK_USER_DIR="$ROOT/home/.local/share/flatpak" \
     flatpak run --nofilesystem=home --filesystem="$ROOT":create \
     io.github.mark12870.cabinet library launch <id>
   ```

5. Before changing configuration, compare the complete nearest working entry and
   any relevant upstream or community installation procedure. Change one variable
   at a time.

6. Treat a manager launch as successful only when its expected window, renderer or
   helper processes, and launch log all agree that it is usable. Process survival
   alone is insufficient.

7. Wrap every command run in the isolated runtime in a short `timeout`: that one step's
   measured duration plus a small margin (a removal 1 minute, an install 3), never one value
   shared by a whole script. When one fires, stop the Wine processes it left behind:
   those whose `/proc/<pid>/environ` names the isolated `WINEPREFIX`.

8. Remove only temporary prefixes in the isolated runtime after testing. Never
   mutate or clean a host prefix during runtime diagnosis.

9. Read the plugin matrix and the unsupported Windows LV2 combination in `docs/TESTS.md`.

10. A scenario's editor check drives the pointer on its own headless `weston`, so it needs no
   session display. When one fails, read the captures and `result.txt` in
   `$CABINET_RUNTIME_ROOT/tmp/scenarios/<id>/artefacts/editor/` before believing the number in the
   message, and copy them aside before any rerun.
