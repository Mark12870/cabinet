---
name: build-and-test
description: Build and verify Cabinet with CI checks, focused .NET/Rust commands, or a local Flatpak install. Use when working with `scripts/checks.sh`, `dotnet format`, `Cabinet.Cli`, `Cabinet.Gui`, Flatpak builds, or before pushing.
---

# Building and checking Cabinet

## Run the checks

1. Enable the hook, once per clone:

   ```sh
   git config core.hooksPath .githooks
   ```

   `.githooks/pre-commit` runs `scripts/checks.sh --staged`: it reformats the staged code and stages the fix, skips the
   halves nothing is staged for, and skips `dotnet test`. A partly staged file is reformatted but not staged, and the
   commit stops so the diff can be looked at.

2. Run everything CI runs, from the repository root:

   ```sh
   scripts/checks.sh
   ```

   It enters `org.gnome.Sdk//50` itself and runs formatting, both front-end builds, the unit tests, AppStream
   validation, and shim fmt/clippy/tests. Work is verified only once the full script passes; the hook alone is not
   enough.

3. For a focused loop, use host `dotnet 10`:

   ```sh
   dotnet test --project tests/Cabinet.Library.Tests --filter-class '*CatalogueTests'
   dotnet build src/Cabinet.Cli --nologo -v q
   dotnet build src/Cabinet.Gui --nologo -v q -p:UseSharedCompilation=false
   ```

   No test project compiles a front end, so a front-end change needs its `dotnet build`. A shim test runs in the SDK
   shell, where `cargo` comes from the `rust-stable` extension:

   ```sh
   flatpak run --filesystem="$PWD" --command=sh org.gnome.Sdk//50 -c \
     'export PATH=/usr/lib/sdk/rust-stable/bin:$PATH; cd shim; cargo test <name>'
   ```

## Build and install the Flatpak

1. Build it in the background and wait for the completion event:

   ```sh
   flatpak run org.flatpak.Builder --repo=repo --force-clean --disable-rofiles-fuse \
     --default-branch=stable build flatpak/io.github.mark12870.cabinet.yml
   ```

   The build is done when the builder exits 0. yabridge compiles with `meson compile -j1`: its 64- and 32-bit hosts
   are each one `--unity-size=1000` translation unit, and in parallel they take about 10 GB, the OOM killer takes
   `cc1plus`, and the builder dies at yabridge's final link looking like a timeout. A serial build takes about ten
   minutes.

2. Install the new commit to the host:

   ```sh
   flatpak remote-add --user --if-not-exists --no-gpg-verify cabinet-local "file://$PWD/repo"
   flatpak install --user -y --or-update cabinet-local io.github.mark12870.cabinet
   ```

   The host install is for using the app and for `gui-shot`. Catalogue installs and runtime tests go through the
   `runtime-test` skill's isolated Toolbox, built from this same commit.
