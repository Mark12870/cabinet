"""Report where a bridged plugin's editor is, and where Wine has been told it is.

Carla loads the plugin, opens its editor and idles. yabridge's editor trace names the
wrapper window it creates, the Wine window it reparents into that wrapper, and the position
it sends Wine in a synthetic ConfigureNotify. X is then asked where each window actually is.

WRAPPER and WINE must share an origin: a Wine window that drifts away from it draws off the
frame the DAW shows, which is a blank editor. TOLD must equal WINE: Wine turns screen
coordinates into client ones with the position it was given, so a window that is not where
Wine believes it is delivers every click at an offset.
"""

import os
import re
import subprocess
import sys
import time

CARLA = sys.argv[1]
PLUGIN = sys.argv[2]
FORMAT = sys.argv[3]
LOG = sys.argv[4]

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
    PLUGIN_VST2,
    PLUGIN_VST3,
    CarlaHostDLL,
)

TYPES = {"vst2": PLUGIN_VST2, "vst3": PLUGIN_VST3}


def geometry(window):
    said = subprocess.run(
        ["xdotool", "getwindowgeometry", str(window)],
        capture_output=True, text=True, check=False).stdout
    found = re.search(r"Position:\s*(-?\d+),(-?\d+)", said)
    return (int(found.group(1)), int(found.group(2))) if found else None


def windows():
    with open(LOG, "r", errors="replace") as handle:
        trace = handle.read()

    def last(name):
        found = re.findall(rf"DEBUG: {name}: (\d+)", trace)
        return int(found[-1]) if found else None

    told = re.findall(r"DEBUG: Translated coords: \d+ : \d+x\d+\+(-?\d+)\+(-?\d+)", trace)

    return (last("wrapper_window"), last("wine_window"),
            (int(told[-1][0]), int(told[-1][1])) if told else None)


def main():
    library = os.path.join(CARLA, "lib", "carla", "libcarla_standalone2.so")
    host = CarlaHostDLL(library, False)
    host.set_engine_option(ENGINE_OPTION_PROCESS_MODE,
                           ENGINE_PROCESS_MODE_CONTINUOUS_RACK, "")
    host.set_engine_option(ENGINE_OPTION_TRANSPORT_MODE,
                           ENGINE_TRANSPORT_MODE_INTERNAL, "")
    host.set_engine_option(ENGINE_OPTION_PATH_BINARIES,
                           0, os.path.join(CARLA, "lib", "carla"))

    if not host.engine_init("Dummy", "cabinet-editor-geometry"):
        print("ENGINE=failed " + host.get_last_error())
        return 1

    try:
        if not host.add_plugin(BINARY_NATIVE, TYPES[FORMAT], PLUGIN, "",
                               "", 0, None, 0):
            print("PLUGIN=failed " + host.get_last_error())
            return 1

        for _ in range(60):
            host.engine_idle()
            time.sleep(0.05)

        host.show_custom_ui(0, True)

        for _ in range(200):
            host.engine_idle()
            time.sleep(0.05)

        wrapper, wine, told = windows()
        if wrapper is None or wine is None:
            print("EDITOR=not-traced")
            return 1

        print(f"WRAPPER={geometry(wrapper)} WINE={geometry(wine)} TOLD={told}")
        host.show_custom_ui(0, False)
    finally:
        host.engine_close()

    return 0


if __name__ == "__main__":
    sys.exit(main())
