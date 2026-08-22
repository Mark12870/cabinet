work="$CABINET_WORK/unpacked"
mkdir -p "$work"
unzip -q "$CABINET_ARCHIVE" -d "$work"

installer=$(find "$work" -maxdepth 1 -name '*.exe')

if [ ! -f "$installer" ]; then
    echo "$CABINET_NAME's download holds no installer" >&2
    exit 1
fi

product=$(basename "$installer")
product=${product%%Win_*}

"$WINE" "$installer" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART

if [ ! -e "$CABINET_PREFIX/drive_c/Program Files/Common Files/VST3/$product.vst3" ]; then
    echo "$CABINET_NAME's installer left no $product.vst3 in the prefix" >&2
    exit 1
fi
