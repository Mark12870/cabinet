unpacked="$CABINET_WORK/unpacked"
mkdir -p "$unpacked"

case "$CABINET_ARCHIVE" in
    *.7z) 7z x -y -bd -o"$unpacked" "$CABINET_ARCHIVE" >/dev/null ;;
    *) tar -xf "$CABINET_ARCHIVE" -C "$unpacked" ;;
esac

binaries=$(find "$unpacked" -mindepth 2 -maxdepth 2 -type d -name x86-64bit | head -n 1)

if [ ! -d "$binaries" ]; then
    echo "$CABINET_NAME's archive holds no x86-64 Linux build" >&2
    exit 1
fi

found=0
for bundle in "$binaries"/*.vst3 "$binaries"/*.lv2; do
    [ -d "$bundle" ] || continue
    mv "$bundle" "$CABINET_DEST/"
    echo "  $(basename "$bundle")"
    found=1
done

if [ "$found" = 0 ]; then
    echo "$CABINET_NAME's Linux build holds no .vst3 or .lv2" >&2
    exit 1
fi
