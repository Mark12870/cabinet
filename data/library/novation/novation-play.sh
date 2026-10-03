case "$CABINET_ARCHIVE" in
    *.[zZ][iI][pP]) ;;
    *)
        echo "Choose the complete Windows installer ZIP (Play_1_1_2.zip)." >&2
        exit 1
        ;;
esac

work="$CABINET_WORK/unpacked"
mkdir -p "$work"
unzip -q "$CABINET_ARCHIVE" -d "$work"

installer="$work/Play Installer.exe"

if [ ! -f "$installer" ] || [ ! -f "$work/Play Installer-1.bin" ] || [ ! -f "$work/Play Installer-2.bin" ]; then
    echo "Choose the complete Windows installer ZIP (Play_1_1_2.zip)." >&2
    exit 1
fi

"$WINE" "$installer" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART

if [ ! -f "$CABINET_PREFIX/drive_c/Program Files/Common Files/VST3/Play.vst3" ] \
    || [ -z "$(find "$CABINET_PREFIX/drive_c/Program Files" -type f -name Play.dll -print -quit)" ] \
    || [ -z "$(find "$CABINET_PREFIX/drive_c/ProgramData/Novation/Play/instruments" -type f -name '*.instr' -print -quit)" ]; then
    echo "$CABINET_NAME's installer left no plugins or factory instruments in the prefix" >&2
    exit 1
fi
