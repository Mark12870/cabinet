"$WINE" "$CABINET_ARCHIVE" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART

if [ ! -f "$CABINET_PREFIX/drive_c/Program Files (x86)/UVI Portal/UVI Portal.exe" ]; then
    echo "$CABINET_NAME's installer left no UVI Portal.exe in the prefix" >&2
    exit 1
fi
