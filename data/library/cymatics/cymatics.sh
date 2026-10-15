"$WINE" reg add 'HKCU\Software\Wine\Fonts\Replacements' /v 'Helvetica, Arial, sans-serif' /d Arial /f

"$WINE" "$CABINET_ARCHIVE" /S

app=$(find "$CABINET_PREFIX/drive_c/users" -path '*/AppData/Local/Programs/*/CymaticsHub.exe' -print -quit)

if [ -z "$app" ]; then
    echo "$CABINET_NAME's installer left no CymaticsHub.exe in the prefix" >&2
    exit 1
fi

destination="$CABINET_PREFIX/drive_c/Program Files/Cymatics Hub"
mkdir -p "$(dirname "$destination")"
ln -sfnT "$(dirname "$app")" "$destination"
