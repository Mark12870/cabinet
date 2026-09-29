"""Report how loud a plugin plays a note once its editor has been used to load a sound.

Some instruments load their sound only through their own editor, and their saved state does not
bring it back, so the sound is loaded here the way a user loads it: clicks and double-clicks in
the editor. The note is then played live, through Carla's running engine rather than an offline
render, because a sampler streaming from disk needs real time to fill its buffers. The output is
metered for the same length of time before the note, while it is held, and after it ends.
"""

import os
import subprocess
import sys
import time

CARLA = sys.argv[1]
PLUGIN = sys.argv[2]
LOG = sys.argv[4]
SHOTS = sys.argv[5]
NOTE = int(sys.argv[6])
STEPS = [step.split() for step in sys.argv[7].split(";") if step.strip()]
TITLE = sys.argv[8]

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
    PLUGIN_VST3,
    CarlaHostDLL,
)


def idle(host, seconds):
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        host.engine_idle()
        time.sleep(0.05)


def window():
    found = subprocess.run(
        ["xdotool", "search", "--onlyvisible", "--name", TITLE],
        capture_output=True, text=True, check=False).stdout.split()
    return found[0] if found else ""


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

    if not host.add_plugin(BINARY_NATIVE, PLUGIN_VST3, PLUGIN, "", "", 0, None, 0):
        print("PLUGIN=failed " + host.get_last_error())
        return 1

    idle(host, 5)
    host.show_custom_ui(0, True)
    idle(host, 15)

    for step in STEPS:
        if step[0] == "wait":
            idle(host, float(step[1]))
            continue
        subprocess.run(
            ["xdotool", "mousemove", "--window", window(), step[1], step[2],
             "click", "--repeat", "2" if step[0] == "double" else "1", "--delay", "120", "1"],
            check=False)
        idle(host, 2)

    capture("loaded")
    before = listen(host, held=False)
    held = listen(host, held=True)
    after = listen(host, held=False)
    host.show_custom_ui(0, False)
    idle(host, 2)
    host.remove_all_plugins()
    host.engine_close()
    print(f"PLAYED BEFORE={before:.6f} HELD={held:.6f} AFTER={after:.6f}")
    return 0


sys.exit(main())
