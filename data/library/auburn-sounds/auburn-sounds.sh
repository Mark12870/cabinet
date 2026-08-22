work="$CABINET_WORK/unpacked"
mkdir -p "$work"
unzip -q "$CABINET_ARCHIVE" -d "$work"

linux=$(find "$work" -mindepth 2 -maxdepth 2 -type d -name Linux)

if [ ! -d "$linux" ]; then
    echo "$CABINET_NAME's archive holds no Linux build" >&2
    exit 1
fi

found=0
for bundle in "$linux"/*/*; do
    case "$bundle" in
        *.vst3 | *.clap | *.lv2)
            mv "$bundle" "$CABINET_DEST/"
            echo "  $(basename "$bundle")"
            found=1
            ;;
    esac
done

if [ "$found" = 0 ]; then
    echo "$CABINET_NAME's Linux build holds no .vst3, .clap or .lv2" >&2
    exit 1
fi
