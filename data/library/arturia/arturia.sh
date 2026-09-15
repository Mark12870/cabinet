"$WINE" "$CABINET_ARCHIVE" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART

if [ ! -f "$CABINET_PREFIX/drive_c/Program Files (x86)/Arturia/Arturia Software Center/Arturia Software Center.exe" ]; then
    echo "$CABINET_NAME's installer left no Arturia Software Center.exe in the prefix" >&2
    exit 1
fi
