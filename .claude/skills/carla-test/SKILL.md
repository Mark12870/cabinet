---
name: carla-test
description: Test Cabinet plugin wrappers through the persistent Carla Toolbox.
---

# Test Cabinet plugins

1. Prepare the isolated Toolbox, private Cabinet and REAPER Flatpaks, pinned CLAP-capable
   Carla build, and catalogue fixtures:

   ```sh
   scripts/setup-carla-tests.sh
   ```

2. Run the complete headless plugin matrix:

   ```sh
   dotnet test tests/Cabinet.Runtime.Tests --nologo
   ```

3. Read the matrix and the unsupported Windows LV2 combination in `TESTS.MD`.
