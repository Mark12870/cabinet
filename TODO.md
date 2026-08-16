# Open work

State as of 2026-08-16, after the first implementation landed on `main`. The architecture is
proven end to end — Surge XT bridges into Flatpak REAPER from two prefixes at once — but the
list below is what is genuinely unverified or unbuilt. Nothing here is speculative polish;
each item is something that was skipped, deferred, or left untested.

## Unverified — needs a running DAW

The probe used during bring-up instantiates a plugin and reads its factory, but never
processes audio and never opens an editor. So three things remain unobserved:

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
