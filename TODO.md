# Open work

State as of 2026-08-16. The architecture is proven end to end — Surge XT bridges into Flatpak
REAPER from several prefixes at once, 32-bit included — and the list below is what is
genuinely unverified or unbuilt. Nothing here is speculative polish; each item is something
that was skipped, deferred, or left untested.

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
- [ ] **A 32-bit plugin actually loads.** `Surge XT (32-bit).vst3` is bridged and present in
      `~/.vst3/yabridge`; nothing has yet confirmed REAPER scans it and that
      `yabridge-host-32.exe` is what starts.

## Untested code

- [ ] **`cabinet install` has not been completed once.** It gets as far as drawing the
      wizard — the prefix is created, Wine starts the `.exe`, and Inno Setup unpacks into the
      prefix's own `C:\users\…\Temp` and builds its UI — but the window then processed no
      input at all (0.4 s of CPU in ten minutes, nothing written to the prefix). Wine was
      healthy throughout: `explorer.exe /desktop` and `services.exe` up, and `winecfg` opened
      in the same prefix on demand.

      The suspicion is the launch context, not Cabinet: it was started from a detached
      background shell, and GNOME's focus-stealing prevention left the window
      `_NET_WM_STATE_HIDDEN` while a `winecfg` started the same way came up
      `_NET_WM_STATE_DEMANDS_ATTENTION` — neither ever got focus. **Unconfirmed.** Settle it
      by running `cabinet install` from an ordinary interactive terminal; if the wizard is
      still dead, the X11 input path into the sandbox is a real bug.

      `install` also takes no trailing arguments, so a silent install (`/VERYSILENT`, `/S`)
      is not expressible — which is why this could not simply be driven headlessly instead.

## Decisions deferred

- [ ] **Wine's Mono and Gecko are not bundled.** They are extensions of the upstream Wine
      Flatpak and `base:` does not inherit extension declarations. Installers needing .NET or
      an embedded browser will fail. Fixable by adding the MSIs as `archive` sources into
      `share/wine/{gecko,mono}`, at roughly 150 MB. Worth doing when a real plugin needs it,
      not before.
- [ ] **`sync` never removes a plugin directory.** `Yabridgectl.SyncPrefixes` only ever calls
      `yabridgectl add`, so deleting a prefix leaves a stale entry in `config.toml`.
      Harmless today — `sync` skips a missing directory — and removing it means either
      parsing `yabridgectl status` or editing a file format Cabinet deliberately does not
      own. Left until it bites.

## Done

- **First `build-publish.yml` run**, and Pages enabled by hand. The site is live, the repo
  serves `config`, `summary` and a signed app ref, and the `.flatpakrepo` carries `GPGKey=`.
- **Install from the published remote**, over HTTPS with `gpg-verify` on — and against a
  *second* published commit, so the repo-seeding step is proven too: `c52bcb71` carries
  `Parent: 69c4d1d7`, and `remote-info --log` offers both, so a rollback has somewhere to go.
- **`cabinet new` and `sync`**, including a 32-bit VST3 unpacked into
  `Program Files (x86)\Common Files\VST3` and bridged. Closing this needed three fixes found
  by running it:
  - `yabridgectl set --path=` **panics** in yabridge 5.1.1 (`--path-auto` is declared as
    taking a value and then read as a flag), so `setup` had never pointed yabridgectl
    anywhere. `setup` now symlinks its own `$XDG_DATA_HOME/yabridge` at the export instead,
    and `doctor` checks it.
  - `WINELOADER` from the login session leaks into Cabinet's own sandbox, so yabridgectl's
    `wine --version` probe went back out through the shim. Pinned to `/app/bin/wine`.
  - Only one directory per prefix was ever registered, so a 32-bit or VST2 plugin could not
    be bridged at all.
- **Plugin-load latency**, measured: the shim round trip is **~130 ms** from inside a
  sandboxed DAW, `flatpak-spawn --host` hop included, and ~120 ms from the host. yabridge
  does this twice per plugin instance, so roughly a quarter second — far enough under the
  one-second threshold that plugin groups are not worth documenting as a mitigation.
- **The memlock warning has a remedy attached.** `doctor` names the systemd drop-in, not
  `limits.conf`: `pam_limits` does not reach anything the systemd user manager starts, which
  is every Flatpak DAW launched from the desktop — verified by pipewire still showing 8 MB
  despite its own 4 GB `limits.d` entry.
- **`enrol` prints the shim self-test** for the DAW's own sandbox, which is as close as
  Cabinet can get to checking it from outside. Confirmed against REAPER: `cabinet-wine 0.1.0 ok`.

## Out of scope for v1

Not scheduled, listed so the boundary stays explicit: portal-based isolation (the point here
is packaging, not sandboxing), prefix templates and reproducible prefix manifests, Bottles
integration, arm64, and the Avalonia GUI. `Cabinet.Core` is a library and the CLI has
`--json` precisely so the GUI stays cheap to add later.
