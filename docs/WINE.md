# Wine behaviour worth knowing

Findings that are not about one vendor. Each was measured, and each cost a session to find.

## Qt 6 applications

Found with Roland Cloud Manager (Qt 6.9 and QtWebEngine, installed by a Qt Installer Framework
installer). Runners 9.21 and 11.0 failed identically, so none of this is runner-specific.

- **A Qt IFW installer throws "Unknown exception caught." on every command.** Qt's
  network-information backend calls WinRT `NetworkInformation.GetInternetConnectionProfile`, which
  is a Wine stub. `WINEDLLOVERRIDES=netprofm=d` makes `CoCreateInstance(NetworkListManager)` fail
  first, so Qt never reaches WinRT. IFW then runs headless with
  `install --accept-licenses --default-answer --confirm-command`.
- **A Qt 6 GUI app dies silently at window creation**, in `qwindows.dll`; its crash handler
  swallows it. Started with its working directory on `Z:` (the Unix `/`), Qt6Gui finds
  `/.flatpak-info`, puts `xdgdesktopportal` first, gets a bare `QPlatformTheme`, and
  `QWindowsTheme::instance()` is NULL. `QT_QPA_PLATFORMTHEME=windows` fixes it.
- **Wine never passes `QT_*` variables into the Windows environment.** Declare them as
  `WINEQT_<name>` and they arrive as `QT_<name>`; `cabinet run <prefix> wine cmd /c set` shows it.
  `CatalogueTests.AQtVariableIsDeclaredUnderTheNameWinePassesToWindows` guards the catalogue.

So a Qt 6 Windows entry starts from these two `Env:` lines:

```yaml
Env:
  WINEDLLOVERRIDES=netprofm=d
  WINEQT_QPA_PLATFORMTHEME=windows
```

## Every Wine window invisible

When every Wine window stays invisible, including `wine notepad` and Wine's own
`explorer /desktop`, the desktop shell may be wedged. Nothing in Cabinet or the prefix will fix
that; logging out and in, or a reboot, does.

Seen on GNOME Shell and mutter 50.4 under Wayland with Xwayland. GNOME's "Application is not
responding" dialog throws when a window has no surface (`closeDialog.js:95 TypeError: ...
surfaceActor is null`), and an unmapped Wine window provokes exactly that. Mutter is then left with
a window stuck in `unmanaging` and logs `meta_window_can_ping: assertion '!window->unmanaging'
failed` for the rest of the session. From then on it answers every Wine map request with
`WM_STATE=Iconic`, and Wine correctly minimises the window to -32000,-32000.

Confirm it before touching code:

1. `journalctl --user -b | grep -c meta_window_can_ping`: a large count, starting when things
   broke.
2. Wine's own account, with `WINEDEBUG=+x11drv`: `window_set_wm_state ... 0 -> 0x1` answered by
   `mismatch WM_STATE 0x3`, then `window_update_client_state minimizing win`.
3. A GTK client on the same Xwayland still maps: `flatpak run --env=GDK_BACKEND=x11
   io.github.mark12870.cabinet`.

Don't try to restore the windows from X11. A window Wine has hidden on the Win32 side can't be
brought back from outside: `XMapRaised` is accepted and Wine unmaps the window again, and forging
`WM_STATE` only fools whoever reads it.

## Diagnosing a crash or a hang

- `WINEDEBUG=+seh` shows the first exception, its faulting address, and the program's own
  `OutputDebugString` text as `warn:seh` lines.
- `WINEDEBUG=+ole` shows `RegisterDragDrop` and `RevokeDragDrop` per thread.
- `x86_64-w64-mingw32-objdump` is in the runtime-test toolbox, for reading a DLL around a
  faulting address.
- A host whose main thread died stays a zombie while its other threads live. To read its stack,
  start it with a direct `WINELOADER` so no session reaps it, then read
  `/proc/<pid>/task/<live tid>/mem`. For a Windows backtrace, run `winedbg` with `bt all` through
  `flatpak enter <host pid>`.
- An exception thrown from native code that Wine cannot unwind shows as endless
  `unknown CFA opcode` or `unhandled opcode` lines, and the thread spins instead of failing.
  DXVK failing on a display it cannot present to ends this way.
- A `timeout` around `toolbox run` ends only the outer command; the probe or sandbox inside keeps
  running. Check for and end leftovers yourself before the next experiment.
