# Open work

State as of 2026-08-16, after the first implementation landed on `main`. The architecture is
proven end to end — Surge XT bridges into Flatpak REAPER from two prefixes at once — but the
list below is what is genuinely unverified or unbuilt. Nothing here is speculative polish;
each item is something that was skipped, deferred, or left untested.

## Unverified — needs a running DAW

The probe used during bring-up instantiates a plugin and reads its factory, but never
processes audio and never opens an editor. So three things remain unobserved:

- [ ] **Editor embedding.** The plugin window must be embedded in the DAW's window, not
      floating. This crosses two sandboxes via XEmbed and is the most likely thing to be
      subtly broken.
- [ ] **Audio renders without xruns** at a normal buffer size.
- [ ] **`/dev/shm` populated during processing.** `ls /dev/shm | grep -i yabridge` should be
      non-empty while a plugin runs. The whole `--device=shm` requirement rests on this, and
      it has been reasoned about but never watched. Buffers are allocated when processing
      starts, which is why the probe never triggered it.

## Untested code

- [ ] **`cabinet install` has never been completed.** `new` and `sync` have now run. `install`
      creates the prefix and Inno Setup draws its wizard, but the window then processes no
      input at all — cause unresolved, and Wine is healthy in that prefix (`winecfg` opens on
      demand). Suspect the launch context rather than the sandbox: it was started from a
      detached background shell and never got focus. Rerun from an interactive terminal.
- [ ] **The 32-bit path has not been loaded.** `stable-25.08` was chosen over `wow64-25.08`
      specifically so 32-bit VST2 plugins keep working, and `Compat.i386` is declared for it.
      `Surge XT (32-bit).vst3` is now bridged into `~/.vst3/yabridge`, but nothing has
      confirmed a DAW scans it and starts `yabridge-host-32.exe`.

## Decisions deferred

- [ ] **Wine's Mono and Gecko are not bundled.** They are extensions of the upstream Wine
      Flatpak and `base:` does not inherit extension declarations. Installers needing .NET or
      an embedded browser will fail. Fixable by adding the MSIs as `archive` sources into
      `share/wine/{gecko,mono}`, at roughly 150 MB. Worth doing when a real plugin needs it,
      not before.

## Out of scope for v1

Not scheduled, listed so the boundary stays explicit: portal-based isolation (the point here
is packaging, not sandboxing), prefix templates and reproducible prefix manifests, Bottles
integration, arm64, and the Avalonia GUI. `Cabinet.Core` is a library and the CLI has
`--json` precisely so the GUI stays cheap to add later.
