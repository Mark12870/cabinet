work="$CABINET_WORK/unpacked"
mkdir -p "$work"
unzip -q "$CABINET_ARCHIVE" -d "$work"

installer=$(find "$work" -mindepth 2 -maxdepth 2 -name '*.exe' | head -n 1)

if [ -z "$installer" ]; then
    echo "$CABINET_NAME's archive holds no installer" >&2
    exit 1
fi

"$WINE" "$installer" /S

if [ ! -f "$CABINET_PREFIX/drive_c/Program Files/IK Multimedia/IK Product Manager/IK Product Manager.exe" ]; then
    echo "$CABINET_NAME's installer left no IK Product Manager.exe in the prefix" >&2
    exit 1
fi
