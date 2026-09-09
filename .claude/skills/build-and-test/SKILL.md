---
name: build-and-test
description: Build and verify Cabinet with CI checks, focused .NET/Rust commands, or a local Flatpak install. Use when working with `scripts/checks.sh`, `dotnet format`, `Cabinet.Cli`, `Cabinet.Gui`, Flatpak builds, or before pushing.
---

# Building and checking Cabinet

`scripts/checks.sh` is exactly what `.github/workflows/checks.yml` runs. Keep the two lists
identical: a check that only CI runs is a check that only fails after a push, which is why the
script exists rather than a block to copy. Both toolchains come from SDK extensions the script
enters on its own.

You must use the code-tester subagent for this.

## Run the checks

**1. Enable the hook, once per clone.**

```sh
git config core.hooksPath .githooks
```

`.githooks/pre-commit` runs the script as `--staged`: it reformats the staged code and stages
the fix, skips the halves nothing is staged for, and skips `dotnet test` to stay under about
seven seconds. A file that is only partly staged is reformatted but *not* staged, and the
commit stops so the diff can be looked at.

**2. Run everything CI runs.**

```sh
scripts/checks.sh            # tests included
```

The two formatters are the half that is easy to forget — `dotnet format` failed CI on a
four-space overhang no test could catch.

**3. Compile both front ends.** Neither is reached by anything else in the list: `dotnet test`
reaches only `Cabinet.Core`, and `dotnet format` does not fail on a broken build. A CLI that
would not compile once passed every check and died in the flatpak build twenty minutes later.
That is what the two `dotnet build` steps in the script are for.

**4. Verify catalogue runtime behaviour.**

The runtime test writes a private `.carxp` project and Carla settings for each case,
then runs the full Carla frontend with `--no-gui`, the Dummy audio driver, and no
display variables. Windows plugins are loaded from their yabridge wrapper paths;
native plugins use their installed paths or LV2 URI. Run the complete matrix with
the `runtime-test` skill after setup.

Never install, launch, or remove a catalogue entry through the host-installed
Cabinet Flatpak during verification. Build the current Flatpak first, pass its
exact OSTree commit to `scripts/setup-runtime-tests.sh`, and run all mutating
catalogue operations in that isolated Toolbox.

```sh
COMMIT=$(ostree --repo=repo rev-parse app/io.github.mark12870.cabinet/x86_64/stable)
CABINET_RUNTIME_CABINET_REF="io.github.mark12870.cabinet/x86_64/stable/$COMMIT" \
  scripts/setup-runtime-tests.sh
```

The host Flatpak may be installed or rebuilt for GUI smoke shots, but do not use
its normal user data for catalogue or runtime tests.

## Build and install the flatpak

```sh
flatpak run org.flatpak.Builder --repo=repo --force-clean --disable-rofiles-fuse \
  --default-branch=stable build io.github.mark12870.cabinet.yml
BUILD_EXIT=$?
printf 'BUILD_EXIT=%s\n' "$BUILD_EXIT"
if [ "$BUILD_EXIT" -ne 0 ]; then exit "$BUILD_EXIT"; fi
```

- Capture and inspect the builder exit status before using the repository.
- Never poll the build; wait for the event.

For a host GUI shot only, add the local repository with
`flatpak remote-add --user --if-not-exists --no-gpg-verify cabinet-local "file://$PWD/repo"`,
then install with `flatpak install --user --or-update cabinet-local io.github.mark12870.cabinet`.
Do not use that host installation for catalogue runtime tests.

Confirm the test ref with:
`ostree --repo=repo rev-parse app/io.github.mark12870.cabinet/x86_64/stable`.

```sh
# Look at a page of the installed GUI. Needs the toolbox its header describes, once.
scripts/gui-shot.sh About about.png
```
