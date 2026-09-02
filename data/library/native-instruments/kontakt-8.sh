destination="$CABINET_PREFIX/drive_c/Program Files/Common Files/VST3/Kontakt 8.vst3"

if [ ! -d "$CABINET_KEPT" ] || [ -s "$destination" ]; then
    exit 0
fi

archive=$(find "$CABINET_KEPT" -maxdepth 1 -type f -iname '*kontakt*8*.zip' -printf '%T@ %p\n' | \
    sort -nr | awk '{ sub(/^[^ ]+ /, ""); print; exit }')

if [ -z "$archive" ]; then
    exit 0
fi

echo "Recovering Kontakt 8 from $(basename "$archive")"

work="$CABINET_PREFIX/drive_c/cabinet-kontakt"
rm -rf "$work"
mkdir -p "$work"
7z x -y "-o$work" "$archive" >/dev/null 2>&1 || true

source=$(find "$work" -type f -name 'Kontakt 8.vst3' | sort | tail -n 1)

if [ -z "$source" ]; then
    setup=$(find "$work" -type f -iname '*Kontakt 8*Setup PC.exe' | sort | tail -n 1)

    if [ -z "$setup" ]; then
        echo "The kept Kontakt 8 download held no installer" >&2
        rm -rf "$work"
        exit 1
    fi

    7z x -y "-o$work/payload" "$setup" >/dev/null 2>&1 || true
    source=$(find "$work/payload" -type f -name 'Kontakt 8.vst3' | sort | tail -n 1)
fi

if [ -z "$source" ] || [ ! -s "$source" ]; then
    echo "Kontakt 8's installer held no Kontakt 8.vst3 to recover" >&2
    rm -rf "$work"
    exit 1
fi

version=$(LC_ALL=C grep -aoP -m 1 \
    'P\x00r\x00o\x00d\x00u\x00c\x00t\x00V\x00e\x00r\x00s\x00i\x00o\x00n\x00\x00\x00\K(?:[0-9]\x00)+(?:\.\x00(?:[0-9]\x00)+)+' \
    "$source" | tr -d '\000')

if [ -z "$version" ]; then
    echo "Could not determine the Kontakt version from the recovered VST3" >&2
    rm -rf "$work"
    exit 1
fi

mkdir -p "$(dirname "$destination")"
cp "$source" "$destination"

"$WINE" reg add 'HKLM\SOFTWARE\Native Instruments\Kontakt 8' \
    /v InstallVST64Dir /d 'C:\Program Files\Common Files\VST3' /f
"$WINE" reg add 'HKLM\SOFTWARE\Native Instruments\Kontakt 8' \
    /v Version /d "$version" /f

products="$CABINET_PREFIX/drive_c/users/Public/Documents/Native Instruments/installed_products"
mkdir -p "$products"
printf '%s\n' "{\"InstallVST64Dir\":\"C:\\\\Program Files\\\\Common Files\\\\VST3\",\"Version\":\"$version\",\"VST3Path\":\"C:\\\\Program Files\\\\Common Files\\\\VST3\\\\Kontakt 8.vst3\"}" \
    > "$products/Kontakt 8.json"

rm -rf "$work"
rm -f "$archive" "$archive".*
"$(dirname "$WINE")/wineserver" -k >/dev/null 2>&1 || true

echo "Recovered Kontakt 8 $version"
