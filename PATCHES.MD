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
