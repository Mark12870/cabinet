# Runtime architecture

- `Bootstrap.Ensure` runs from both entry points, creates Cabinet's prefixes directory plus the yabridgectl link,
  and clears `Staging` directories whose owner's lock is free.
- Only Wine runs inside the Cabinet sandbox for the yabridge bridge. The DAW reads yabridge's host-side halves from the
  installed Flatpak's `current/active/files`; `enrol` writes nothing and prints the required `flatpak override` for the
  user to apply, because the DAW then loads what Cabinet's Windows code can write. A sandboxed DAW starts a session by
  activating `io.github.mark12870.cabinet.Bridge` (`shim/src/dispatch.rs`), which starts the broker in Cabinet's own
  sandbox, so the DAW needs no host access. Older enrolments grant `org.freedesktop.Flatpak` and cannot reach the
  bridge; the shim then falls back to `flatpak-spawn --host flatpak run`, and so does a Cabinet a test control
  redirects (`REDIRECTS`), which must never reach the installed bridge. The chainloader patch finds Cabinet's yabridge
  by its installed path, so a DAW needs no `data/yabridge` link and the manifest grants nothing under `~/.var/app`.
- The crossing is `$WINELOADER`: yabridge's winegcc wrapper execs `shim/src/main.rs`, which hands the plugin to a Wine
  session and exits with that job's status, so yabridge's liveness check on the loader PID tracks the real host. The
  manifest rewrites that wrapper's fallback from bare `wine` to the shim beside it, so every DAW reaches the shim,
  whatever its own `WINELOADER`; the shim in turn forces `YABRIDGE_NO_WATCHDOG=1` (`FORCED`), because the watchdog is
  PID-based and misfires across the boundary. Preserve `YABRIDGE_TEMP_DIR`. The shim duplicates three sets of constants,
  and `ShimParityTests` compares each against its C# side: marker names against `Layout`, sync and Cabinet-owned
  variables against `PrefixSettings`, blanked sockets against `Prefixes`. yabridge finds a plugin's prefix by walking
  from its `.dll` to the `dosdevices` directory.
- One Wine session per prefix. Flatpak gives every `flatpak run` its own PID namespace but binds
  `/tmp` per app, so two sandboxes over one `WINEPREFIX` reach the same wineserver socket from either side of a
  namespace boundary and wedge the DAW; that is what froze REAPER on a project holding five Klevgrand plugins.
  `shim/src/session.rs` keys a session on the canonical `WINEPREFIX`, starts it once behind an `flock` in
  `YABRIDGE_TEMP_DIR`, and every later plugin sends its argv to that session over the session socket. The session
  outlives the shim that started it, and its sandbox stays up while it runs; the bridge holds every session it started
  in one sandbox. Once idle a session retires and, still holding the session lock, kills its own descendants, so
  Windows services a plugin started end with it and the next session on that prefix starts clean. Wine that Cabinet
  started itself lives outside that tree and keeps running, and Cabinet's own commands join a live session.
- One owner changes a prefix at a time, and the locks beside the socket in `YABRIDGE_TEMP_DIR` say who it is.
  `cabinet-wine --cabinet-paths` prints every one of them, and Core reads that rather than deriving the names itself. A
  plugin-side shim holds `<key>.busy` shared for its whole life; a Cabinet change takes `<key>.change` and
  `<key>.busy` exclusively, plus `<key>.apps`, which `Library.Launch` holds shared while a manager runs, and then waits
  for `<key>.sock` to go away, because a session fixes the runner and sync mode it started with. A shim that joins takes
  no lock, so Cabinet's own jobs and a change never collide. Every refusal is a `PrefixInUseException`
  naming who holds the prefix, and both front ends print its message. The claim is what makes the check atomic: while it
  is held, a plugin load fails at once with a diagnostic instead of racing the change. Never reach past it with direct
  Wine, and never let a shipped script end Wine itself; `Library.Settle` does that under the claim, once the script has
  exited. A script's output goes to a log Cabinet follows rather than a pipe, because a Wine process the script leaves
  behind inherits that pipe and would hold the install open until it died. Two paths take no claim on purpose:
  `cabinet run`, which is the user's own job and joins a session like a plugin, and the runner version probe, which
  reads a runner in its own `.probe` prefix.
- A session writes `<key>.session` with its prefix and runner, and takes it away when it retires, so
  `runners rm` can see a live broker still using a runner that no prefix marker names. It also points its own stderr at
  `<key>.log` and writes every diagnostic without panicking, including a copy to the failing job's own error stream; a
  broker that wrote to a client's closed pipe used to abort and take every other job in the session with it.
- A session reads `.cabinet-env` again for every job it starts, so `cabinet set <prefix> env` reaches the next plugin in
  a running session. `.cabinet-sync` is fixed when the session starts, on the outer shim's `flatpak run`: every Wine
  process must use the sync mode of the wineserver it joins, and staging-based runners exit on a mismatch.
- Wine re-parents a job's processes away from the process that started them, so a session finds and kills a job by its
  process group. `wineserver` and `winedevice.exe` put themselves in their own groups and so survive between jobs, which
  is exactly what sharing a session requires.
- A yabridge host is dead once its main thread is. Wine can lose that thread, to a stack overflow for one, while other
  threads keep the process and its pidfd alive; the leader shows as a zombie (`Zl`) and the DAW waits on it forever.
  `supervise` ends a job whose `yabridge-host` leader is a zombie; any other program may outlive its main thread.
  yabridge's plugin side then aborts the DAW at teardown, so this turns a hang into a crash.
- A program an installer starts on its way out (a "launch now" box) inherits the installer's stdout and stderr, so
  draining them would wait until the user quits it. `ProcessRunner` names such holders once the started process has
  exited and hands `Lingering.Watcher` a way to stop them; Wine's own system processes are left to wineserver. Do not
  stop waiting on exit alone: a bootstrapper that exits early keeps the real installer running on the same pipes.
- yabridge puts its sockets in `$XDG_RUNTIME_DIR` when `YABRIDGE_TEMP_DIR` is unset, and a manifest grants only
  `xdg-run` subdirectories. The shim grants that path by value on every `flatpak run`, so every DAW reaches its sockets.
  A long `YABRIDGE_TEMP_DIR` breaks the sockets with `File name too long`, so leave it under `/run/user/<uid>`.
- Every successful bridge sets up native DAWs: it makes `cabinet/windows` in `~/.vst3`, `~/.clap` and `~/.vst`
  Cabinet's own yabridgectl output (never native plugins inside it: `sync` prunes them), reports a path something
  else owns and leaves it in place, and takes out the per-file links older releases left in `~/.local/share/yabridge`.
  yabridge's chainloader searches `$PATH` and `~/.local/share/yabridge`, where every yabridge puts the same names, so
  `patches/yabridge/yabridge-chainloader-cabinet-first.patch` makes Cabinet's copies load Cabinet's installed library
  and host first; both stay in Cabinet's installed files, because `yabridgectl sync` prunes every `.so` beside the
  plugins that lacks its own `.dll`. A findable bridge with an unreachable loader aborts the DAW from yabridge's launch
  thread; the manifest's `WINELOADER` fallback rewrite keeps the loader reachable.
