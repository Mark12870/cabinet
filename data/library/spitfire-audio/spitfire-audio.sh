"$WINE" "$CABINET_ARCHIVE" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART

if [ ! -f "$CABINET_PREFIX/drive_c/Program Files/Spitfire Audio/Spitfire Audio.exe" ]; then
    echo "$CABINET_NAME's installer left no Spitfire Audio.exe in the prefix" >&2
    exit 1
fi
