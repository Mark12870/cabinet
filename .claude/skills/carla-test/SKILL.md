---
name: carla-test
description: Test Cabinet plugin wrappers through the persistent Carla Toolbox.
---

# Test Cabinet plugins

1. Create or reuse the persistent Toolbox, build the pinned CLAP-capable Carla, and
   prepare the deterministic catalogue fixtures:

   ```sh
   scripts/setup-carla-tests.sh
   ```

2. Run the complete headless Linux and Windows VST2, VST3, CLAP and LV2-compatible matrix:

   ```sh
   CABINET_RUN_CARLA_TESTS=1 dotnet test tests/Cabinet.Runtime.Tests --nologo
   ```

3. Read the matrix and the unsupported Windows LV2 combination in `TEST.MD`.
