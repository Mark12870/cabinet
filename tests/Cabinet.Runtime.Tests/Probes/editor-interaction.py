"""Report whether a plugin's editor draws its interface and reacts to the pointer.

Carla loads the plugin and opens its editor. The editor window is whichever top-level window
appears across that call, so a native plugin is found the same way a bridged one is and no
yabridge trace is needed. The window is captured, the pointer is swept over a grid of points
inside it, and four of those points are clicked and dragged. When none of them answers, the first
parameters are moved from the host one at a time, and wherever the editor redraws one is clicked
and dragged in turn, so an editor whose controls fall between the grid points is still reached.
The same located controls are then aimed at: the pointer is pressed and dragged where a parameter
is drawn, and the host must see that parameter move.
Every capture is compared against the first, and against one taken with the pointer held still:
a plugin that animates on its own must not pass for one that answers the pointer.

A frame of one flat colour is the blank editor a DAW shows when the plugin draws somewhere else.
A sweep that never changes a pixel is an editor that is drawn but receives nothing. Both are
invisible in a log. Every call into the host is timed as well: a host that stops servicing its
loop while an editor is open is the DAW freeze, and a stall in engine_close is where it shows.
"""

import itertools
import os
import re
import subprocess
import sys
import time

CARLA = sys.argv[1]
PLUGIN = sys.argv[2]
FORMAT = sys.argv[3]
LOG = sys.argv[4]
SHOTS = sys.argv[5]

sys.path.insert(0, os.path.join(CARLA, "share", "carla"))

os.environ["YABRIDGE_DEBUG_LEVEL"] = "1+editor"
os.environ["YABRIDGE_DEBUG_FILE"] = LOG

from carla_backend import (  # noqa: E402
    BINARY_NATIVE,
    ENGINE_OPTION_PATH_BINARIES,
    ENGINE_OPTION_PROCESS_MODE,
    ENGINE_OPTION_TRANSPORT_MODE,
    ENGINE_PROCESS_MODE_CONTINUOUS_RACK,
    ENGINE_TRANSPORT_MODE_INTERNAL,
    PARAMETER_INPUT,
    PARAMETER_IS_ENABLED,
    PARAMETER_IS_READ_ONLY,
    PLUGIN_CLAP,
    PLUGIN_LV2,
    PLUGIN_VST2,
    PLUGIN_VST3,
    CarlaHostDLL,
)

TYPES = {"vst2": PLUGIN_VST2, "vst3": PLUGIN_VST3, "clap": PLUGIN_CLAP, "lv2": PLUGIN_LV2}

SETTLE = 20
DRAW = 20
ENOUGH = 0.002
COLUMNS = 5
ROWS = 4
INSET = 0.15
SPREAD = (0.25, 0.5, 0.75)
LIMIT = 10
LOCATE = 8
SWING = (-10, -20, -30, -10, 10, 20, 30)
BLOBS = 4
SPECK = 50
FUZZ = "2%"


STARTED = time.monotonic()


def note(phase):
    print(f"[{time.monotonic() - STARTED:7.1f}s] {phase}", file=sys.stderr, flush=True)


def run(arguments):
    try:
        return subprocess.run(
            arguments, capture_output=True, text=True, check=False, timeout=LIMIT)
    except subprocess.TimeoutExpired:
        note(f"timed out: {arguments[0]}")
        return subprocess.CompletedProcess(arguments, 1, "", "")


def visible():
    said = run(["xdotool", "search", "--onlyvisible", ""]).stdout
    return {line.strip() for line in said.splitlines() if line.strip()}


def geometry(window):
    said = run(["xdotool", "getwindowgeometry", str(window)]).stdout
    at = re.search(r"Position:\s*(-?\d+),(-?\d+)", said)
    size = re.search(r"Geometry:\s*(\d+)x(\d+)", said)

    if not at or not size:
        return None

    return (int(at.group(1)), int(at.group(2)), int(size.group(1)), int(size.group(2)))


def area(window):
    found = geometry(window)
    return found[2] * found[3] if found else 0


def capture(window, name):
    path = os.path.join(SHOTS, name + ".png")
    raw = os.path.join(SHOTS, name + ".xwd")

    for stale in (path, raw):
        if os.path.exists(stale):
            os.remove(stale)

    target = ["-root"] if window == "root" else ["-id", str(window)]
    run(["xwd", "-silent", *target, "-out", raw])

    if not os.path.exists(raw) or os.path.getsize(raw) == 0:
        if os.path.exists(raw):
            os.remove(raw)

        return None

    run(["magick", raw, "png:" + path])
    os.remove(raw)
    return path if os.path.exists(path) else None


def colours(path):
    said = run(["identify", "-format", "%k", path]).stdout.strip()
    return int(said) if said.isdigit() else 0


def difference(before, after):
    said = run([
        "magick", before, after, "-compose", "difference", "-composite",
        "-colorspace", "Gray", "-threshold", FUZZ, "-format", "%[fx:mean]", "info:"]).stdout
    found = re.search(r"([0-9.eE+-]+)", said)
    return float(found.group(1)) if found else 0.0


class Loop:
    def __init__(self, host):
        self.host = host
        self.worst = 0.0
        self.blind = 0
        self.closed = False

    def turn(self, times=1):
        for _ in range(times):
            started = time.monotonic()
            self.host.engine_idle()
            self.worst = max(self.worst, time.monotonic() - started)
            time.sleep(0.05)

    def timed(self, call):
        started = time.monotonic()
        call()
        return time.monotonic() - started


def alive(window):
    return str(window) in visible() and area(window) > 1


def look(window, name, loop):
    shot = capture(window, name)

    if shot is None:
        if not alive(window):
            loop.closed = True
            note(f"{window} is gone at {name}: the editor answered by closing")
            return None

        loop.blind += 1
        note(f"blind at {name}: {window} is still mapped but gave no capture")

    return shot


def managed(window):
    said = run(["xprop", "-id", str(window), "WM_STATE"]).stdout
    return "window state" in said.lower()


def editor_window(before, loop):
    for _ in range(DRAW):
        loop.turn(10)
        appeared = visible() - before
        clients = [window for window in appeared if managed(window)]
        ranked = sorted(clients or appeared, key=area, reverse=True)

        if ranked and area(ranked[0]) > 1:
            loop.turn(20)
            return ranked[0]

    return None


def points(window):
    left, top, width, height = geometry(window)

    for column in range(COLUMNS):
        for row in range(ROWS):
            yield (
                left + int(width * (INSET + (1 - 2 * INSET) * column / (COLUMNS - 1))),
                top + int(height * (INSET + (1 - 2 * INSET) * row / (ROWS - 1))),
                f"{column}-{row}")


def settle():
    run(["xdotool", "mouseup", "1"])


def drag(window, before, loop, at, name):
    x, y = at
    run(["xdotool", "mousemove", str(x), str(y), "mousedown", "1"])
    loop.turn(4)
    seen = 0.0

    for step in range(1, 5):
        run(["xdotool", "mousemove", str(x), str(y - step * 10)])
        loop.turn(3)
        shot = look(window, f"drag-{name}-{step}", loop)

        if loop.closed:
            break

        if shot:
            seen = max(seen, difference(before, shot))
            before = shot

    run(["xdotool", "mouseup", "1"])
    loop.turn(6)
    return seen, before


def regions(before, after, still, again):
    said = run([
        "magick",
        "(", before, after, "-compose", "difference", "-composite",
        "-colorspace", "Gray", "-threshold", FUZZ, ")",
        "(", still, again, "-compose", "difference", "-composite",
        "-colorspace", "Gray", "-threshold", FUZZ, "-negate", ")",
        "-compose", "multiply", "-composite",
        "-define", "connected-components:verbose=true",
        "-define", f"connected-components:area-threshold={SPECK}",
        "-connected-components", "8", "null:"]).stdout
    found = re.findall(r"\d+x\d+\+\d+\+\d+ ([0-9.]+),([0-9.]+) (\d+) gray\(255\)", said)
    ranked = sorted(((int(size), float(x), float(y)) for x, y, size in found), reverse=True)
    return [(round(x), round(y)) for _, x, y in ranked[:BLOBS]]


def controls(host):
    for index in range(host.get_parameter_count(0)):
        data = host.get_parameter_data(0, index)
        if (data["type"] == PARAMETER_INPUT and data["hints"] & PARAMETER_IS_ENABLED
                and not data["hints"] & PARAMETER_IS_READ_ONLY):
            yield index


def located(window, loop):
    host = loop.host
    left, top, _, _ = geometry(window)

    for index in itertools.islice(controls(host), LOCATE):
        ranges = host.get_parameter_ranges(0, index)
        current = host.get_current_parameter_value(0, index)
        far = ranges["min"] if current - ranges["min"] > ranges["max"] - current else ranges["max"]
        host.set_parameter_value(0, index, far)
        loop.turn(10)
        moved = look(window, f"locate-{index}", loop)
        host.set_parameter_value(0, index, current)
        loop.turn(10)
        restored = look(window, f"locate-{index}-restored", loop)
        loop.turn(10)
        still = look(window, f"locate-{index}-still", loop)

        if loop.closed:
            return

        found = regions(restored, moved, restored, still) if moved and restored and still else []
        note(f"parameter {index} drew {found}")

        for blob, (x, y) in enumerate(found):
            yield (left + x, top + y, f"parameter-{index}-{blob}", still, index)


def sweep(window, before, loop, noise):
    reaction = 0.0
    ranked = []
    enough = ENOUGH + noise

    for x, y, name in points(window):
        run(["xdotool", "mousemove", str(x), str(y)])
        loop.turn(4)

        if not ranked:
            under = run(["xdotool", "getmouselocation", "--shell"]).stdout
            note("pointer over " + re.sub(r"\s+", " ", under).strip())

        shot = look(window, f"hover-{name}", loop)

        if loop.closed:
            return reaction

        seen = difference(before, shot) if shot else 0.0
        before = shot or before
        reaction = max(reaction, seen)
        ranked.append((seen, x, y, name))
        note(f"hover {name} at {x},{y} moved {seen:.6f}")

        if reaction >= enough:
            note(f"hover {name} answered {reaction:.6f}")
            return reaction

    ranked.sort(reverse=True)
    left, top, width, height = geometry(window)
    targets = [(ranked[0][1], ranked[0][2], ranked[0][3], None)] + [
        (left + int(width * across), top + int(height * 0.5), f"spread-{index}", None)
        for index, across in enumerate(SPREAD)]

    for x, y, name, rested, *_ in itertools.chain(targets, located(window, loop)):
        before = rested or before
        settle()
        note(f"click {name} at {x},{y}")
        run(["xdotool", "mousemove", str(x), str(y), "click", "1"])
        loop.turn(10)
        shot = look(window, f"click-{name}", loop)

        if loop.closed:
            return reaction

        if shot:
            reaction = max(reaction, difference(before, shot))
            before = shot

        if reaction >= enough:
            note(f"click {name} answered {reaction:.6f}")
            return reaction

        dragged, before = drag(window, before, loop, (x, y), name)
        reaction = max(reaction, dragged)

        if loop.closed:
            return reaction

        note(f"drag {name} {reaction:.6f}")
        settle()
        loop.turn(4)

        if reaction >= enough:
            return reaction

    return reaction


def moved(host, index, original):
    return abs(host.get_current_parameter_value(0, index) - original) > 1e-6


def pressed(loop, index, x, y):
    host = loop.host
    original = host.get_current_parameter_value(0, index)
    settle()
    run(["xdotool", "mousemove", str(x), str(y), "mousedown", "1"])
    loop.turn(4)
    hit = False

    for swing in SWING:
        run(["xdotool", "mousemove", str(x), str(y + swing)])
        loop.turn(3)
        hit = hit or moved(host, index, original)

    run(["xdotool", "mouseup", "1"])
    loop.turn(4)
    hit = hit or moved(host, index, original)
    host.set_parameter_value(0, index, original)
    loop.turn(6)
    return hit


def aim(window, loop):
    for x, y, name, _, index in located(window, loop):
        note(f"aim {name} at {x},{y}")

        if loop.closed:
            break

        if pressed(loop, index, x, y):
            note(f"aim {name} moved parameter {index}")
            return name

    return "none"


def main():
    os.makedirs(SHOTS, exist_ok=True)
    library = os.path.join(CARLA, "lib", "carla", "libcarla_standalone2.so")
    host = CarlaHostDLL(library, False)
    host.set_engine_option(ENGINE_OPTION_PROCESS_MODE,
                           ENGINE_PROCESS_MODE_CONTINUOUS_RACK, "")
    host.set_engine_option(ENGINE_OPTION_TRANSPORT_MODE,
                           ENGINE_TRANSPORT_MODE_INTERNAL, "")
    host.set_engine_option(ENGINE_OPTION_PATH_BINARIES,
                           0, os.path.join(CARLA, "lib", "carla"))

    if not host.engine_init("Dummy", "cabinet-editor-interaction"):
        print("ENGINE=failed " + host.get_last_error())
        return 1

    loop = Loop(host)

    try:
        by_uri = FORMAT == "lv2"
        if not host.add_plugin(BINARY_NATIVE, TYPES[FORMAT], "" if by_uri else PLUGIN, "",
                               PLUGIN if by_uri else "", 0, None, 0):
            print("PLUGIN=failed " + host.get_last_error())
            return 1

        note("loaded")
        loop.turn(SETTLE)
        before = visible()
        opening = loop.timed(lambda: host.show_custom_ui(0, True))
        window = editor_window(before, loop)
        note(f"editor {window}")

        if window is None:
            print("EDITOR=none")
            return 1

        found = geometry(window)
        note(f"geometry {found}")
        run(["xdotool", "mousemove", "0", "0"])
        loop.turn(10)
        baseline = capture(window, "baseline")

        if not baseline:
            print(f"EDITOR={window} CAPTURE=failed")
            return 1

        # Raised as well as activated: a capture comes from the window's own pixmap and looks
        # right even when something else is on top of it, while the pointer goes to whatever is
        # topmost, and the sweep then measures a window nothing is touching.
        run(["xdotool", "windowactivate", str(window)])
        run(["xdotool", "windowraise", str(window)])
        loop.turn(30)
        resting = capture(window, "resting")
        loop.turn(30)
        again = capture(window, "resting-again")
        noise = difference(resting, again) if resting and again else 0.0
        note(f"noise {noise:.6f}")

        note("sweeping")
        reaction = sweep(window, again or resting or baseline, loop, noise)
        note(f"reaction {reaction:.6f}")
        aimed = aim(window, loop)
        note(f"aimed {aimed}")

        # The same measurement with nothing touching it: an editor that changes on its own does it
        # here too, and then it is not the pointer's doing.
        run(["xdotool", "mousemove", "0", "0"])
        loop.turn(30)
        settled = capture(window, "settled")
        loop.turn(30)
        rested = capture(window, "settled-again")
        drift = difference(settled, rested) if settled and rested else 0.0
        noise = max(noise, drift)
        note(f"drift {drift:.6f}")
        closing = loop.timed(lambda: host.show_custom_ui(0, False))
        note("closed")
        loop.turn(SETTLE)

        gone = visible()
        loop.timed(lambda: host.show_custom_ui(0, True))
        again = editor_window(gone, loop)
        note(f"reopened {again}")
        loop.timed(lambda: host.show_custom_ui(0, False))
        loop.turn(SETTLE)

        started = time.monotonic()
        removed = host.remove_plugin(0)
        removing = time.monotonic() - started
        if not removed:
            print("REMOVE=failed " + host.get_last_error())
            return 1
        note("removed")
    finally:
        started = time.monotonic()
        host.engine_close()
        shutdown = time.monotonic() - started

    summary = (
        f"EDITOR={window} SIZE={found[2]}x{found[3]} COLOURS={colours(baseline)} "
        f"REACTION={reaction:.6f} NOISE={noise:.6f} IDLE_MS={loop.worst * 1000:.0f} "
        f"OPEN_MS={opening * 1000:.0f} CLOSE_MS={closing * 1000:.0f} "
        f"REMOVE_MS={removing * 1000:.0f} SHUTDOWN_MS={shutdown * 1000:.0f} "
        f"BLIND={loop.blind} CLOSED={'yes' if loop.closed else 'no'} "
        f"REOPEN={'yes' if again else 'no'} AIM={aimed}")

    with open(os.path.join(SHOTS, "result.txt"), "w") as handle:
        handle.write(summary + "\n")

    print(summary)
    return 0


if __name__ == "__main__":
    sys.exit(main())
