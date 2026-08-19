---
name: gui-shot
description: Screenshot a page of Cabinet's GTK4 GUI so a change to it can actually be looked at. Use whenever GUI work needs visual confirmation — "does the About page look right", "show me the window", "check the runners list renders" — and before calling any Cabinet.Gui change done. Also covers what does NOT work here, so dead ends are not re-attempted.
---

# Looking at Cabinet's GUI

`Cabinet.Gui` is GTK4 + libadwaita. Tests cover `Cabinet.Core`, and the compiler catches
GirCore misuse, but neither answers *does it look right*. This does.

## Take a screenshot

```sh
scripts/gui-shot.sh About about.png       # page: Prefixes | Runners | Doctor | About
scripts/gui-shot.sh Prefixes/aalto x.png  # a named row's own page, one level down
```

Then **read the PNG back** — that is the whole point; a script that ran is not a page that was
looked at. It prints the path it wrote and exits non-zero with a legible message if the page
name is unknown, the toolbox is missing, or no window appeared.

The script launches the *installed* Flatpak, so build and install first:

```sh
flatpak run org.flatpak.Builder --repo=repo --force-clean --disable-rofiles-fuse \
  --default-branch=stable build io.github.mark12870.cabinet.yml
flatpak install --user -y --reinstall cabinet-local io.github.mark12870.cabinet
```

If a Cabinet window is already open the script uses it and leaves it running; otherwise it
starts one and kills it afterwards.

## One-time setup

```sh
toolbox create cabinet-gui-test --image registry.fedoraproject.org/fedora-toolbox:44
toolbox run --container cabinet-gui-test \
  sudo dnf install -y xdotool ImageMagick python3-pyatspi gobject-introspection
```

`gobject-introspection` is not optional — without its DBus typelib, `import pyatspi` dies.
Override the container name with `CABINET_GUI_TOOLBOX`.

## How it works, and how to extend it

Pages are selected through **AT-SPI**, not by clicking coordinates: the view switcher exposes
its tabs as `page tab` nodes named exactly as they are titled, and `queryAction().doAction(0)`
switches to one. Nothing depends on window size, font or scale.

The same route reaches every other widget that exposes a `click` action. To drive a button, a
dialog or a row, walk the tree for the role and name and act on it — this dumps it. Keep the
depth cap generous: `Adw.NavigationView` put the prefix rows past a cap of 16, and a walk that
stops short reports the row missing rather than deep.

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

To see a page too tall for the default window, resize before capturing:

```sh
toolbox run --container cabinet-gui-test sh -c \
  'xdotool windowsize $(xdotool search --name "^Cabinet$" | head -1) 900 1150'
```

## Do not retry these

Measured, not assumed:

- **GNOME Shell's `Screenshot` D-Bus method** answers `AccessDenied` to a caller like this.
- **GTK Broadway** — the one bridge that would let a browser tool such as Playwright drive a
  GTK app — fails with *Failed to open display* in `org.gnome.Platform//50`, for `gtk4-demo`
  as much as for Cabinet. It is the runtime, not this app. Playwright otherwise does not
  apply: it drives browsers.
- **Editing the app to open on a given page** and rebuilding. That was the old way and it
  costs minutes per look.
- **Synthesising input from the toolbox.** `xdotool mousemove … click` at a widget's own
  coordinates does not reach it, with or without `windowactivate --sync`. AT-SPI's `getExtents`
  is no help in aiming either: `DESKTOP_COORDS` answered `0,0` for a window at `2715,391`.
  Everything goes through `doAction`, so a widget is reachable only if it offers a **`click`**
  action — and most do not. `Adw.ExpanderRow` and a bare `Adw.ActionRow` offer none at all; a
  `Gtk.Label` offers eight, all clipboard, so *take the action named `click`* rather than
  `doAction(0)`, which otherwise copies a label and reports success.
- **Capturing a popover.** An `Adw.ComboRow`'s list is a separate X window, so
  `import -window` photographs the page underneath it and a combo's choice cannot be confirmed
  visually. An `Adw.Dialog` draws in the window's own surface and captures fine — that is the
  route for anything a popover would have offered.
- **Reaching the root page while another is pushed.** `Adw.NavigationView` drops the hidden
  page from the accessibility tree, so the header's *Look at everything again* button is not
  there to click from a subpage. Trigger a refresh through an operation the subpage itself
  offers.

## Two traps

- **Never `pkill -f cabinet-gui`.** The pattern matches the shell running it and kills the
  session. Use `pkill -x cabinet-gui`.
- Under `set -o pipefail`, a `$(...)` whose pipeline ends in a failing `xdotool search`
  aborts the calling script through `set -e`. The script guards this with `|| true`.
