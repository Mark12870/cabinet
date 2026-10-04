"$WINE" "$CABINET_ARCHIVE" /S

app="$CABINET_PREFIX/drive_c/Program Files/Waves Central"

if [ ! -f "$app/Waves Central.exe" ]; then
    echo "$CABINET_NAME's installer left no Waves Central.exe in the prefix" >&2
    exit 1
fi

modules="$CABINET_PREFIX/drive_c/ProgramData/Waves Audio/Modules"
mkdir -p "$modules"
cp -a "$app/resources/res/external/bin/WavesLicenseEngine.bundle" "$modules/"
