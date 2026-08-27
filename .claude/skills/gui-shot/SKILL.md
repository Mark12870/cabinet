---
name: gui-shot
description: Capture and inspect Cabinet's installed GTK4/libadwaita GUI. Use for GUI changes, visual confirmation, page screenshots, or before declaring `Cabinet.Gui` work complete.
---

# Looking at Cabinet's GUI

`Cabinet.Gui` is GTK4 + libadwaita. Tests cover `Cabinet.Core`, and the compiler catches
GirCore misuse, but neither answers *does it look right*. This does, without editing the app
to open on the page you want and rebuilding — the old way, minutes per look.

## Take a screenshot

**1. Build and install.** The script drives the *installed* Flatpak.

```sh
flatpak run org.flatpak.Builder --repo=repo --force-clean --disable-rofiles-fuse \
  --default-branch=stable build io.github.mark12870.cabinet.yml
flatpak install --user -y --or-update cabinet-local io.github.mark12870.cabinet
```

**2. Capture the page.**

```sh
scripts/gui-shot.sh About about.png       # Library | Prefixes | Runners | Doctor | About
scripts/gui-shot.sh Prefixes/aalto x.png  # a named row's own page, one level down
```

It prints the path it wrote and exits non-zero with a legible message if the page name is
unknown, the toolbox is missing, or no window appeared. An open Cabinet window is reused and
left running; otherwise one is started and killed afterwards. Capture is X11 through
XWayland — GNOME Shell refuses its `Screenshot` D-Bus method to a caller like this one, and
GTK's Broadway backend — the one bridge a browser driver such as Playwright could have come
in through — does not open a display in `org.gnome.Platform//50`, for `gtk4-demo` as much as
for Cabinet.

**3. Read the PNG back.** That is the whole point; a script that ran is not a page that was
looked at.

**4. Prove the shot is current.** Only a *focused* window updates its backing pixmap: once
something else takes focus, `import -window` keeps returning the last frame it had, and a
page switch, a pushed page and an open dialog all vanish from the capture while AT-SPI shows
them present.

```sh
magick compare -metric AE before.png after.png   # 0 means the run proved nothing
```

`xprop _NET_WM_STATE` naming `_NET_WM_STATE_DEMANDS_ATTENTION` is the same condition, and
nothing recovers it from here: mutter refuses `xdotool windowactivate --sync`, and a resize
lands without a redraw — the old region keeps the stale frame and the new region is black.
Bring the window forward and shoot again, or read the page through AT-SPI instead.

## Drive a widget yourself

Pages are selected through **AT-SPI**, never by coordinates, so nothing depends on window
size, font or scale. It is also the only route: a synthesised `xdotool mousemove … click`
does not reach a widget, and `getExtents` cannot aim one either (`DESKTOP_COORDS` answered
`0,0` for a window at `2715,391`). A widget is reachable exactly when it offers a **`click`**
action — `Adw.ExpanderRow` and a bare `Adw.ActionRow` offer none.

**1. Dump the tree.**

```sh
toolbox run --container cabinet-gui-test python3 - <<'PY'
import pyatspi
for app in pyatspi.Registry.getDesktop(0):
    if (getattr(app, "name", "") or "") != "cabinet-gui":
        continue
    def walk(node, depth=0):
        if depth > 32:
            return
        print("  " * depth, node.getRoleName(), repr(node.name))
        for child in node:
            if child is not None:
                walk(child, depth + 1)
    walk(app)
PY
```

Keep the depth cap generous: `Adw.NavigationView` puts the prefix rows past a cap of 16, and
a walk that stops short reports a row missing rather than deep.

**2. Find the node** by role and name — `page tab` for a view-switcher tab, `list item` for a
row. A `Gtk.Button`'s own accessible name is empty, so match its `label` child and walk *up*
to the first ancestor offering `click`.

**3. Take the action named `click`**, not `doAction(0)`: a `Gtk.Label` offers eight, all
clipboard, so index 0 copies the label and reports success. For a row take the **last**
clickable descendant — the one an `Adw.ActionRow` makes its activatable widget. The first
reaches whatever button the row carries ahead of it, one of which opened a destructive dialog
instead of navigating.

`scripts/gui-shot.sh` does all three; copy its inline Python as the starting point. It also
guards two things worth keeping in anything derived from it: an empty window id makes
`import` wait for an interactive pick until the timeout kills it, and a `$(...)` whose
pipeline ends in a failing `xdotool search` aborts the caller under `set -euo pipefail`.

## For these cases

- **The window will not come forward** — verify through AT-SPI: page titles, label text,
  button names. A page that `queryComponent().getExtents()` reports as 0×0 is evidence about
  the window's focus, not the page's layout; the long-working Prefixes page reads 0×0 too.
- **A page too tall for the window** — resize it, while it still holds focus:

  ```sh
  toolbox run --container cabinet-gui-test sh -c \
    'xdotool windowsize $(xdotool search --name "^Cabinet$" | head -1) 900 1150'
  ```

- **A choice made in a combo** — read it back through AT-SPI, or offer it in an `Adw.Dialog`,
  which draws in the window's own surface and captures cleanly. An `Adw.ComboRow`'s popover is
  a separate X window, so `import -window` photographs the page underneath it.
- **An `Adw.AlertDialog`'s body** — capture it while the window is focused. Its text is
  unreadable through AT-SPI either way: the alert exposes two empty panels and no label, the
  same reason its body cannot be selected or copied.
- **Anything behind the file chooser** — confirm `Ui.ChooseFile` opened the portal by its
  AT-SPI frame title (`org.gnome.Nautilus`, native Wayland, so `xdotool` cannot find, focus
  or type into it), then exercise the operation itself through the CLI, which runs the same
  `Library.Install`.
- **The root page while a subpage is pushed** — trigger the refresh from an operation the
  subpage itself offers. `Adw.NavigationView` drops the hidden page from the accessibility
  tree, so Doctor's *Look at everything again* button is not there to click.
- **A stray GUI to kill** — `pkill -x cabinet-gui`. Never `-f`: the pattern matches the shell
  running it and kills the session.

## One-time setup

xdotool, ImageMagick and pyatspi are not on a Silverblue host, so they live in a toolbox that
shares the session's DISPLAY and D-Bus. `gui-shot.sh` refuses to run without it and names it
in the error; the two commands that create it are in the script's own header, which is where
that error sends you. `CABINET_GUI_TOOLBOX` overrides the name.
