"$WINE" "$CABINET_ARCHIVE" install --accept-licenses --default-answer --confirm-command

if [ ! -f "$CABINET_PREFIX/drive_c/Program Files/RolandCloudManager/Roland Cloud Manager.exe" ]; then
    echo "$CABINET_NAME's installer left no Roland Cloud Manager.exe in the prefix" >&2
    exit 1
fi
