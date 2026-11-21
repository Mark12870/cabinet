work="$CABINET_WORK/unpacked"
mkdir -p "$work"
unzip -q "$CABINET_ARCHIVE" -d "$work"

installer=$(find "$work" -maxdepth 1 -name '*-Setup.exe' -print -quit)

if [ ! -f "$installer" ]; then
    echo "$CABINET_NAME's download holds no installer" >&2
    exit 1
fi

"$WINE" "$installer" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART

vst3="$CABINET_PREFIX/drive_c/Program Files/Common Files/VST3"

if [ -z "$(find "$vst3" -maxdepth 2 -iname "$CABINET_NAME.vst3" 2>/dev/null)" ]; then
    echo "$CABINET_NAME's installer left no VST3 in the prefix" >&2
    exit 1
fi
