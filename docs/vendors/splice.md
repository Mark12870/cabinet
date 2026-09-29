# Splice under Cabinet

The Splice INSTRUMENT crash in REAPER is Wine's `RevokeDragDrop` releasing another process's
drop target (WineHQ bug 60225). `PATCHES.md` explains the mechanism and the two patches that
answer it, and says when each can go. This note keeps what the patches do not record: how the
crash was reproduced, and what was tried instead and did not help.

## Reproducing it

- It needs a **signed-in** Splice prefix and REAPER as the host. It crashes about ten seconds
  after the plugin loads, and on every editor open and close. Carla and a signed-out prefix never
  trigger it, so the runtime tests cannot reproduce it with a plugin; `DragAndDropTests` probes the
  Wine call directly instead.
- Splice's JUCE/WebView2 UI revokes drag-and-drop on its child windows, one of which belongs to
  `msedgewebview2.exe`. On Wine 11.0 the resulting fault recurses in the exception dispatcher until
  the host's main thread overflows; upstream fixed that recursion in Wine 11.7 (a55cddce98).
- The d2d1 fork's source already fixes the release itself (giang17/wine `d2d1-dcomp-11.0`,
  fafb443f85), but its only binary predates that commit. `DragAndDropTests` fails once a runner
  release carries the fix.

An isolated REAPER, so the user's own setup is not touched:

- copy the signed-in prefix into the runtime-test root with a reflink `cp`, then `cabinet sync`;
- start REAPER with `YABRIDGE_TEMP_DIR` on a short path under `$XDG_RUNTIME_DIR` (a long one fails
  with "File name too long") and `CABINET_RUNTIME_ROOT` pointing at that root;
- point its `data/yabridge` link at a plain copy of the bridge, because the Wine sandbox cannot see
  the isolated Flatpak's deploy directory;
- drive it with a ReaScript on the command line: `TrackFX_Show` with 3 and 2 in a `defer` loop.

## What did not help

Measured, so there is no need to try them again:

- **A runner with the ole32 fix** (G6rm0k/wine-ole32-fix, vanilla 11.0). It is 64-bit only.
  Splice's UI stays blank and REAPER freezes in Splice's `WaitForSingleObject` at +0xafdf9f, the
  same wait as on vanilla 11.8: every non-d2d1 runner deadlocks there, so Splice needs d2d1.
- **A newer runner.** Vanilla 11.8 and Kron4ek 11.17 deadlock at removal; Proton 11.0-2 freezes
  REAPER at load.
- **The Windows version.** Neither `msedgewebview2.exe` set to Windows 11 nor a global Windows 11
  changes anything.
- **An older WebView2** was not tested, but the cross-process window exists in every version.

Building yabridge's host for these patches needs about 5 GB of memory; close browsers first on a
16 GB machine.
