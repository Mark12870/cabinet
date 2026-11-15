"$WINE" "$CABINET_ARCHIVE" /S

if [ ! -f "$CABINET_PREFIX/drive_c/Program Files/MasteringTheMix/Installer Manager.exe" ]; then
    echo "$CABINET_NAME's installer left no Installer Manager.exe in the prefix" >&2
    exit 1
fi
