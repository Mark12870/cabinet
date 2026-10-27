destination="$CABINET_PREFIX/drive_c/Program Files/Minimal/Minimal Hub.exe"
mkdir -p "$(dirname "$destination")"
cp "$CABINET_ARCHIVE" "$destination"

if [ ! -f "$destination" ]; then
    echo "$CABINET_NAME's portable app was not copied into the prefix" >&2
    exit 1
fi
