---
name: runtime-test
description: Test Cabinet plugin wrappers through the persistent Carla Toolbox.
---

# Test Cabinet plugins

1. Prepare the isolated Toolbox, private Cabinet and REAPER Flatpaks, pinned full Carla
   build, and catalogue fixtures:

   ```sh
   scripts/setup-runtime-tests.sh
   ```

2. Run the complete headless plugin matrix:

   ```sh
   toolbox run --container cabinet-runtime \
     dotnet test tests/Cabinet.Runtime.Tests --nologo \
     -m:1 -p:BuildInParallel=false -p:RestoreDisableParallel=true
   ```

3. Read the matrix and the unsupported Windows LV2 combination in `TESTS.MD`.
