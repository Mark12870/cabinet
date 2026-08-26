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

```sh
# The GUI needs the compiler server off, or every GirCore assembly comes back as CS0006.
dotnet build src/Cabinet.Gui -p:UseSharedCompilation=false
```

## Build and install the flatpak

```sh
flatpak run org.flatpak.Builder --repo=repo --force-clean --disable-rofiles-fuse \
  --default-branch=stable build io.github.mark12870.cabinet.yml
flatpak install --user --or-update cabinet-local io.github.mark12870.cabinet  # file://$PWD/repo
```

- Print `BUILD_EXIT=$?` and read it.
- Never chain the two lines with `&&`/`||`.
- `flatpak install --or-update`, always.
- Never poll the build; wait for the event.

Confirm with `flatpak info --user io.github.mark12870.cabinet` against
`ostree --repo=repo rev-parse app/io.github.mark12870.cabinet/x86_64/stable`.

```sh
# Look at a page of the installed GUI. Needs the toolbox its header describes, once.
scripts/gui-shot.sh About about.png
```
