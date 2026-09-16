# yabridge patches

Cabinet builds yabridge from a pinned commit and applies the files in `patches/` in the order the
manifest lists them. Each patch works around one failure that kills the Wine plugin host. When
the host dies, yabridge's plugin side aborts the DAW, or the DAW waits forever. Each section below
says what would make the patch unnecessary.

## yabridge-teardown-guard.patch

**Failure.** Some plugins fault in their own code while the host destroys them. Splice
INSTRUMENT's `hurricane::api::adapters::LoginHandler` destructor calls into a service object that
has already been freed, in about a quarter of removals.

On Wine 11.0, dispatching that access violation fails too. The collided-unwind path in
`call_seh_handlers` passes a NULL `HandlerData` to `RtlVirtualUnwind2`, which writes through it,
and the dispatch recurses until the host's main thread overflows its stack. Upstream Wine fixed
that in a55cddce98 (Wine 11.7), but the d2d1 fork still has it.

**Patch.** On 64-bit hosts, a vectored exception handler covers VST3 instance and view
destruction. It acts only on access violations in native code, not in Wine builtins. It resumes
after the destructor and leaves the half-destroyed object in memory.

**When it can go.** Only when the plugins stop faulting in teardown. A runner can't retire it: with
the dispatcher fixed, the same fault is unhandled and still ends the host. No deterministic test
can show this, because the fault is a race inside the plugin. Check by removing the plugin
repeatedly in Carla and watching the host's output for "crashed while being torn down".

## yabridge-foreign-drag-drop.patch

**Failure.** WineHQ bug 60225. `RegisterDragDrop` stores the raw `IDropTarget` pointer in a window
property. `RevokeDragDrop` releases that pointer even when the window belongs to another process,
where the pointer means nothing, and faults.

WebView2 editors hit this every time the editor opens or closes. The WebView2 browser process
registers drag-and-drop on its window, and the plugin then revokes it on every child window of
its own. Splice INSTRUMENT does this when logged in. On Wine 11.0 the fault then recurses as
described above.

**Patch.** After the plugin loads, the host redirects every native module's import of
`ole32!RevokeDragDrop` to a wrapper. The wrapper returns `DRAGDROP_E_INVALIDHWND` for another
process's window and calls the real function otherwise.

**When it can go.** Pinned runners never change, so the fix can only arrive in a new release.
`DragAndDropTests` in `Cabinet.Runtime.Tests` runs a small probe program on the newest release of
each runner family: the bundled Wine, mklnln's d2d1 build, Kron4ek and Soda. It asserts that each
still crashes. A failing case names the release that has the fix.

Once every entry that needs a fixed runner has moved to one, delete this patch, its manifest line
and its `ManifestTests` fact. WebView2 editors that render only on d2d1, such as Splice
INSTRUMENT, need a d2d1 release.

The d2d1 fork's source has had the fix since commit fafb443f85 (2026-07-23), but mklnln's only
build, v11.0, predates it. Upstream Wine had not fixed this as of 2026-09.

## yabridge-editor-window-origin.patch

**Failure.** Every bridged editor is placed one monitor width across from the wrapper window
hosting it, so the frame the DAW shows is blank and clicks land offset. See the yabridge source
note below for the measurements.

yabridge already forces the Wine window back to `0,0` in its `XCB_CONFIGURE_REQUEST` handler, but
that handler never runs: an editor trace over a full session records **zero** `ConfigureRequest`
events, although the wrapper does select `XCB_EVENT_MASK_SUBSTRUCTURE_REDIRECT`.

**Patch.** Pins the same invariant at the three points a trace proves do fire: `Editor::resize`,
the `ReparentNotify` handler, and immediately after the synthetic `ConfigureNotify` that tells
Wine where its window is. The wrapper exists only to host that one window, so its origin is the
only correct position, and Wine's own idea of the position is the wrapper's absolute origin —
putting the window there makes the two agree, which is what mouse coordinates depend on.

**When it can go.** When `ConfigureRequest` reaches yabridge's handler, or upstream keeps the
window at the origin itself. `EditorTests` covers it, and is what to re-run to find out: building
this tree with only this patch unwired was measured on 2026-09-16, and all six cases failed by
exactly `+1920` in both geometry and click accuracy. The patch is load-bearing, not belt and
braces.

**A prefix setting can replace it, and was measured too.**
`HKCU\Software\Wine\X11 Driver` `UseXRandR=N` stops Wine enumerating monitors through XRandR, so
it sees one screen rather than a layout whose second origin is at 1920, and the constant it was
adding disappears — which is why the offset was 1920 wherever the window sat. On the unpatched
build, setting it in one prefix passed exactly that prefix's two cases and left the rest failing;
setting it in all three passed all six.

It was not taken, because it is per prefix rather than one place: every existing prefix would need
it and `Prefixes.Create` would have to set it for new ones, with a migration for the prefixes
already on disk. Turning XRandR off also costs Wine its display-mode and DPI handling, which three
editors opening at their natural size does not clear for a whole catalogue. Revisit it as a
considered change, not as a swap.

# The yabridge Cabinet builds

The manifest pins one yabridge commit, and `ManifestTests` requires that commit to be named
here with a reason. Cabinet used to ship yabridge's prebuilt `5.1.1` release; `37a1dbe`
(2026-09-05) moved to a source build, which is what makes the patches above possible.

## ba7022df0aee1e91cde62d7f0e940d3bc43a82b0

`robbert-vdh/yabridge`, the `new-wine10-embedding` branch behind
[yabridge#409](https://github.com/robbert-vdh/yabridge/issues/409). It is not a release, and it
was chosen deliberately: `5.1.1` (Nov 2024) predates Wine 9.22's editor-embedding changes, and on
it a bridged editor drew but received absolute screen coordinates where it expected
client-relative ones, so clicks landed offset by the window's distance from the screen origin.
Cabinet's runners are Wine 9.21-tkg and 11.0. **Do not revert this pin to chase the defect below**
— that trades a blank VST2 editor for every bridged editor mis-routing its clicks.

**Defect found and fixed on 2026-09-16**, by
`patches/yabridge-editor-window-origin.patch` above. Every bridged editor was placed one monitor
width across from the window hosting it — `+1920+0` on a 4480x1440 X screen, for VST2 and VST3
alike. The plugin rendered the whole time; it rendered off the frame, so the frame was flat grey,
and because Wine converts screen coordinates to client ones using the position it was given,
every click landed the same distance out.

Only **VST2** looked broken in a DAW, because `check_size_mismatch` is polled from `vst3.cpp` and
pulls a VST3 editor back into agreement. VST2 has no such path, and `Editor::resize` set the Wine
window's size but never its position. `EditorTests` covers both halves and both formats.

This is the same embedding problem the branch set out to fix, not a separate one and not
something Cabinet owns: the coordinate is still wrong, it now misplaces the window rather than
the clicks, and VST3 hides it. Aalto's editor drew on `5.1.1` on 2026-08-17, so what changed is
which symptom shows. Fixing it belongs upstream in #409; before bumping this pin, re-run the
editor coverage in `TESTS.MD` and expect those cases to go green when it is fixed. Neither patch
above applies to the `5.1.1` tree.
