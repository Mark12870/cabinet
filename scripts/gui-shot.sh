#!/usr/bin/env bash
# Screenshot one page of the GUI, so a change to it can be looked at without a rebuild.
#
#   scripts/gui-shot.sh About [out.png]
#   scripts/gui-shot.sh Prefixes/aalto [out.png]   a row's own page, one level down
#
# The window is driven through AT-SPI rather than by clicking coordinates: the view
# switcher's tabs are exposed as named "page tab" nodes, so a page is selected by its
# label and nothing here depends on the window's size, font or scale. A name after a
# slash is a row on that page, activated the same way, which pushes its own page.
# GNOME refuses the Screenshot D-Bus method to callers like this one, and GTK's
# Broadway backend does not open a display in org.gnome.Platform//50, so capture is
# X11: the app is started with GDK_BACKEND=x11 through XWayland, which the manifest's
# --socket=x11 already allows.
#
# A row is activated through its LAST clickable descendant, which is the one an
# Adw.ActionRow makes its activatable widget. Taking the first instead reaches
# whatever button the row carries ahead of it, and a row's other buttons act rather
# than navigate -- one of them opened a destructive dialog instead of the page asked
# for.
#
# xdotool, ImageMagick and pyatspi are not on a Silverblue host, so they live in a
# toolbox that shares the session's DISPLAY and D-Bus. Create it once with:
#
#   toolbox create cabinet-gui-test --image registry.fedoraproject.org/fedora-toolbox:44
#   toolbox run --container cabinet-gui-test \
#     sudo dnf install -y xdotool ImageMagick python3-pyatspi gobject-introspection
#
# gobject-introspection is not optional: without its DBus typelib, `import pyatspi` dies.
set -euo pipefail

APP=io.github.mark12870.cabinet
BOX=${CABINET_GUI_TOOLBOX:-cabinet-gui-test}
PAGE=${1:-Prefixes}
OUT=${2:-$(printf '%s' "$PAGE" | tr '[:upper:]/' '[:lower:]-').png}

box() { toolbox run --container "$BOX" "$@"; }

window() { box xdotool search --name '^Cabinet$' 2>/dev/null | head -1 || true; }

if ! podman container exists "$BOX" 2>/dev/null; then
  echo "gui-shot: no toolbox '$BOX' — see the header of this script" >&2
  exit 1
fi

started=no
cleanup() {
  rm -f "${select_page:-}"
  if [ "$started" = yes ]; then
    pkill -x cabinet-gui || true
  fi
}
trap cleanup EXIT

if [ -z "$(window)" ]; then
  flatpak run --env=GDK_BACKEND=x11 "$APP" >/dev/null 2>&1 &
  started=yes
fi

id=
for _ in $(seq 40); do
  id=$(window)
  if [ -n "$id" ]; then
    break
  fi
  sleep 0.5
done

if [ -z "$id" ]; then
  echo "gui-shot: no Cabinet window appeared — is $APP installed?" >&2
  exit 1
fi

select_page=$(mktemp /tmp/gui-shot-XXXXXX.py)

cat > "$select_page" <<'PY'
import sys
import pyatspi

tab_name, _, row_name = sys.argv[1].partition("/")


def find(node, role, wanted, depth=0):
    if depth > 32:
        return None
    if node.getRoleName() == role and (node.name or "").casefold() == wanted.casefold():
        return node
    for child in node:
        if child is not None:
            found = find(child, role, wanted, depth + 1)
            if found is not None:
                return found
    return None


def clickable(node, depth=0):
    found = None
    if depth <= 32:
        try:
            action = node.queryAction()
            for i in range(action.nActions):
                if action.getName(i) == "click":
                    found = node, i
        except Exception:
            pass
        for child in node:
            if child is not None:
                deeper = clickable(child, depth + 1)
                if deeper is not None:
                    found = deeper
    return found


def activate(root, role, wanted, what):
    found = find(root, role, wanted)
    if found is None:
        sys.exit(f"no {what} called {wanted!r}")
    clicked = clickable(found)
    if clicked is None:
        sys.exit(f"{what} {wanted!r} has nothing to click")
    node, index = clicked
    node.queryAction().doAction(index)


for app in pyatspi.Registry.getDesktop(0):
    if (getattr(app, "name", "") or "") != "cabinet-gui":
        continue
    activate(app, "page tab", tab_name, "page")
    if row_name:
        activate(app, "list item", row_name, "row")
    sys.exit(0)

sys.exit("cabinet-gui is not on the accessibility bus")
PY

box python3 "$select_page" "$PAGE"
sleep 1
box import -window "$id" "$(realpath -m "$OUT")"

echo "$OUT"
