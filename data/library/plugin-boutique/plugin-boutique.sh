"$WINE" "$CABINET_ARCHIVE" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART

if [ ! -f "$CABINET_PREFIX/drive_c/Program Files/Plugin Boutique/Plugin Boutique/Plugin Boutique.exe" ]; then
    echo "$CABINET_NAME's installer left no Plugin Boutique.exe in the prefix" >&2
    exit 1
fi
