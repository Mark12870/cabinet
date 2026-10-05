"""Report how loud a plugin plays a note once its editor has been used to load a sound.

Some instruments load their sound only through their own editor, so the sound is loaded here the
way a user loads it: clicks and double-clicks in the editor. The note is then played live, through
Carla's running engine rather than an offline render, because a sampler streaming from disk needs
real time to fill its buffers. The output is metered for the same length of time before the note,
while it is held, and after it ends.

With a ninth argument the plugin's state is then saved, a fresh instance is given it the way a DAW
opens a project, and the note is metered again.
"""

import ctypes
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
NOTE = int(sys.argv[6])
STEPS = [step.split() for step in sys.argv[7].split(";") if step.strip()]
TITLE = sys.argv[8]
RESTORE = len(sys.argv) > 9
RELEASE = float(os.environ.get("CABINET_PROBE_RELEASE", "0"))

LISTEN = 3.0

sys.path.insert(0, os.path.join(CARLA, "share", "carla"))
os.environ["YABRIDGE_DEBUG_LEVEL"] = "1"
os.environ["YABRIDGE_DEBUG_FILE"] = LOG

from carla_backend import (  # noqa: E402
    BINARY_NATIVE,
    ENGINE_OPTION_PATH_BINARIES,
    ENGINE_OPTION_PROCESS_MODE,
    ENGINE_OPTION_TRANSPORT_MODE,
    ENGINE_PROCESS_MODE_CONTINUOUS_RACK,
    ENGINE_TRANSPORT_MODE_INTERNAL,
    PLUGIN_OPTION_USE_CHUNKS,
    PLUGIN_VST2,
    PLUGIN_VST3,
    CarlaHostDLL,
)

TYPES = {"vst2": PLUGIN_VST2, "vst3": PLUGIN_VST3}

x11 = ctypes.CDLL("libX11.so.6")
x11.XOpenDisplay.argtypes = [ctypes.c_char_p]
x11.XOpenDisplay.restype = ctypes.c_void_p
x11.XDefaultRootWindow.argtypes = [ctypes.c_void_p]
x11.XDefaultRootWindow.restype = ctypes.c_ulong
x11.XWarpPointer.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_ulong,
                           ctypes.c_int, ctypes.c_int, ctypes.c_uint, ctypes.c_uint,
                           ctypes.c_int, ctypes.c_int]
x11.XSync.argtypes = [ctypes.c_void_p, ctypes.c_int]
display = x11.XOpenDisplay(None)
root = x11.XDefaultRootWindow(display)


def idle(host, seconds):
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        host.engine_idle()
        time.sleep(0.05)


def window():
    found = subprocess.run(
        ["xdotool", "search", "--onlyvisible", "--name", TITLE],
        capture_output=True, text=True, check=False).stdout.split()
    for candidate in found:
        state = subprocess.run(
            ["xprop", "-id", candidate, "WM_STATE"],
            capture_output=True, text=True, check=False).stdout
        if "window state" in state.lower():
            return candidate
    return ""


def capture(name):
    raw = os.path.join(SHOTS, name + ".xwd")
    subprocess.run(["xwd", "-id", window(), "-silent", "-out", raw], check=False)
    subprocess.run(["magick", raw, os.path.join(SHOTS, name + ".png")], check=False)
    os.remove(raw)


def listen(host, held):
    if held:
        host.send_midi_note(0, 0, NOTE, 110)
    peak = 0.0
    end = time.monotonic() + LISTEN
    while time.monotonic() < end:
        host.engine_idle()
        peak = max(peak, host.get_output_peak_value(0, True), host.get_output_peak_value(0, False))
        time.sleep(0.02)
    if held:
        host.send_midi_note(0, 0, NOTE, 0)
    return peak


def main():
    host = CarlaHostDLL(os.path.join(CARLA, "lib", "carla", "libcarla_standalone2.so"), False)
    host.set_engine_option(ENGINE_OPTION_PROCESS_MODE, ENGINE_PROCESS_MODE_CONTINUOUS_RACK, "")
    host.set_engine_option(ENGINE_OPTION_TRANSPORT_MODE, ENGINE_TRANSPORT_MODE_INTERNAL, "")
    host.set_engine_option(ENGINE_OPTION_PATH_BINARIES, 0, os.path.join(CARLA, "lib", "carla"))

    if not host.engine_init("Dummy", "cabinet-editor-play"):
        print("ENGINE=failed " + host.get_last_error())
        return 1

    if not host.add_plugin(BINARY_NATIVE, TYPES[FORMAT], PLUGIN, "", "", 0, None, 0):
        print("PLUGIN=failed " + host.get_last_error())
        return 1

    idle(host, 5)
    host.show_custom_ui(0, True)
    idle(host, 15)

    capture("opened")
    for step in STEPS:
        if step[0] == "wait":
            idle(host, float(step[1]))
            continue
        geometry = subprocess.run(
            ["xdotool", "getwindowgeometry", window()], capture_output=True, text=True, check=True).stdout
        origin = re.search(r"Position:\s*(-?\d+),(-?\d+)", geometry)
        x11.XWarpPointer(display, 0, root, 0, 0, 0, 0,
                         int(origin[1]) + int(step[1]), int(origin[2]) + int(step[2]))
        x11.XSync(display, 0)
        idle(host, 0.5)
        subprocess.run(
            ["xdotool", "click", "--repeat", "2" if step[0] == "double" else "1", "--delay", "120", "1"],
            check=False)
        idle(host, 2)

    capture("loaded")
    before = listen(host, held=False)
    held = listen(host, held=True)
    idle(host, RELEASE)
    after = listen(host, held=False)
    host.show_custom_ui(0, False)
    idle(host, 2)
    print(f"PLAYED BEFORE={before:.6f} HELD={held:.6f} AFTER={after:.6f}")

    if RESTORE:
        state = os.path.join(SHOTS, "state.carxs")
        host.set_option(0, PLUGIN_OPTION_USE_CHUNKS, True)
        host.save_plugin_state(0, state)
        host.remove_all_plugins()
        idle(host, 5)
        if not host.add_plugin(BINARY_NATIVE, TYPES[FORMAT], PLUGIN, "", "", 0, None, 0):
            print("RESTORED=failed " + host.get_last_error())
            return 1
        host.set_option(0, PLUGIN_OPTION_USE_CHUNKS, True)
        if not host.load_plugin_state(0, state):
            print("RESTORED=failed " + host.get_last_error())
            return 1
        idle(host, 20)
        host.show_custom_ui(0, True)
        idle(host, 10)
        capture("restored")
        before = listen(host, held=False)
        held = listen(host, held=True)
        idle(host, RELEASE)
        after = listen(host, held=False)
        host.show_custom_ui(0, False)
        idle(host, 2)
        print(f"RESTORED BEFORE={before:.6f} HELD={held:.6f} AFTER={after:.6f}")

    host.remove_all_plugins()
    host.engine_close()
    return 0


sys.exit(main())
