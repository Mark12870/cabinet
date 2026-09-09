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

3. For plugin entries, run the complete headless matrix:

   ```sh
   toolbox run --container cabinet-runtime \
     dotnet test tests/Cabinet.Runtime.Tests --nologo \
     -m:1 -p:BuildInParallel=false -p:RestoreDisableParallel=true
   ```

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
     io.github.mark12870.cabinet library install <id> <fresh-prefix>
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

7. Remove only temporary prefixes in the isolated runtime after testing. Never
   mutate or clean a host prefix during runtime diagnosis.

8. Read the plugin matrix and the unsupported Windows LV2 combination in `TESTS.MD`.
