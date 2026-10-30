7z e -y -o"$CABINET_WORK" "$CABINET_ARCHIVE" '*-full.nupkg' >/dev/null
7z x -y -o"$CABINET_WORK/package" "$CABINET_WORK"/*-full.nupkg 'lib/net45/*' >/dev/null

destination="$CABINET_PREFIX/drive_c/Program Files/inMusic Software Center"
rm -rf "$destination"
mkdir -p "$(dirname "$destination")"
mv "$CABINET_WORK/package/lib/net45" "$destination"

if [ ! -f "$destination/inMusic Software Center.exe" ]; then
    echo "$CABINET_NAME's package left no inMusic Software Center.exe in the prefix" >&2
    exit 1
fi
