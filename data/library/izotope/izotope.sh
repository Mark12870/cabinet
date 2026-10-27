"$WINE" "$CABINET_ARCHIVE" --mode unattended

if [ ! -f "$CABINET_PREFIX/drive_c/Program Files/iZotope/Product Portal/x64/iZotope Product Portal.exe" ]; then
    echo "$CABINET_NAME's installer left no iZotope Product Portal.exe in the prefix" >&2
    exit 1
fi
