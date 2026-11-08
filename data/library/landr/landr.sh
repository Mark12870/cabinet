"$WINE" "$CABINET_ARCHIVE" /S

app=$(find "$CABINET_PREFIX/drive_c/users" -path '*/AppData/Local/Programs/landr-desktop-app/LANDR.exe' -print -quit)

if [ -z "$app" ]; then
    echo "$CABINET_NAME's installer left no LANDR.exe in the prefix" >&2
    exit 1
fi

destination="$CABINET_PREFIX/drive_c/Program Files/LANDR"
mkdir -p "$(dirname "$destination")"
ln -sfnT "$(dirname "$app")" "$destination"
