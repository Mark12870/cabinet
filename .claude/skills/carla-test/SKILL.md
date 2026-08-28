---
name: carla-test
description: Test Cabinet-installed Windows plugins through Carla's carla-single runtime.
---

# Test a Cabinet plugin

1. Use a Cabinet yabridge wrapper, not the Windows DLL in the Wine prefix. Set the wrapper and
   format to test:

   ```sh
   FORMAT=vst2
   PLUGIN="$HOME/.vst/yabridge/SyndtSphere.so"
   LABEL="SyndtSphere VST2"
   ```

   Use `FORMAT=vst3` and the matching `.vst3` wrapper for VST3. Do not pass a numeric unique ID.

2. Confirm Cabinet is installed and find its files:

   ```sh
   CABINET_FILES="$(flatpak info --user --show-location io.github.mark12870.cabinet)/files"
   test -x "$CABINET_FILES/lib/yabridge/cabinet-wine"
   test -e "$PLUGIN"
   ```

3. Create a temporary host Flatpak launcher for the Carla toolbox:

   ```sh
   BRIDGE_DIR="$(mktemp -d /tmp/cabinet-carla.XXXXXX)"
   printf '%s\n' '#!/bin/sh' 'exec flatpak-spawn --host flatpak "$@"' > "$BRIDGE_DIR/flatpak"
   chmod +x "$BRIDGE_DIR/flatpak"
   ```

4. Create a unique label, log, and working directory. The label scopes Carla cleanup and the
   working directory prevents Carla state files from entering the repository:

   ```sh
   RUN_ID="carla-$(date +%s)-$$"
   TEST_LABEL="$LABEL [$RUN_ID]"
   LOG="/tmp/$RUN_ID.log"
   WORK_DIR="$(mktemp -d /tmp/$RUN_ID.XXXXXX)"
   YABRIDGE_TEMP_DIR="${YABRIDGE_TEMP_DIR:-${XDG_RUNTIME_DIR:-/run/user/$(id -u)}/yabridge}"
   YABRIDGE_NO_WATCHDOG="${YABRIDGE_NO_WATCHDOG:-1}"
   CABINET_APP=io.github.mark12870.cabinet
   BEFORE_INSTANCES="$(flatpak ps --columns=instance,application |
     while read -r instance application; do
       [ "$application" = "$CABINET_APP" ] && printf '%s ' "$instance"
     done)"
   ```

5. Run `carla-single` in the Carla toolbox. Keep Cabinet's yabridge directory before the
   toolbox's other paths so the chainloader finds the installed host libraries:

   ```sh
   if timeout 30s toolbox run --container carla env \
     "PATH=$BRIDGE_DIR:$CABINET_FILES/lib/yabridge:$PATH" \
     "WINELOADER=$CABINET_FILES/lib/yabridge/cabinet-wine" \
     "YABRIDGE_TEMP_DIR=$YABRIDGE_TEMP_DIR" \
     "YABRIDGE_NO_WATCHDOG=$YABRIDGE_NO_WATCHDOG" \
     sh -c 'cd "$1" && exec /usr/bin/carla-single native "$2" "$3" "$4"' \
     sh "$WORK_DIR" "$FORMAT" "$PLUGIN" "$TEST_LABEL" \
     > "$LOG" 2>&1
   then
     STATUS=0
   else
     STATUS=$?
   fi
   ```

6. Inspect the status and relevant log lines:

   ```sh
   printf 'STATUS=%s\n' "$STATUS"
   grep -E 'Initializing yabridge|plugin type:|Finished initializing|Plugin failed|Could not find|Wine host process has exited|Assertion|Connection reset' "$LOG" || true
   ```

   Treat status `124` as expected when the plugin remains loaded until `timeout`. Require
   `Finished initializing` and no `Plugin failed`, `Could not find`, or unexpected Wine-host exit.
   Record Carla or plugin assertions separately; they do not replace the initialization result.

7. Stop Carla's bridge in the toolbox, then stop only new Cabinet Flatpak instances:

   ```sh
   toolbox run --container carla pkill -TERM -f "$RUN_ID" >/dev/null 2>&1 || true
   sleep 1
   toolbox run --container carla pkill -KILL -f "$RUN_ID" >/dev/null 2>&1 || true
   for instance in $(flatpak ps --columns=instance,application |
     while read -r instance application; do
       [ "$application" = "$CABINET_APP" ] && printf '%s\n' "$instance"
     done); do
     case " $BEFORE_INSTANCES " in
       *" $instance "*) ;;
       *) flatpak kill "$instance" || true ;;
     esac
   done
   rm -rf "$BRIDGE_DIR" "$WORK_DIR"
   ```

8. Repeat steps 1, 5, 6, and 7 for each wrapper format.