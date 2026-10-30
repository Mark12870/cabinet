"$WINE" "$CABINET_ARCHIVE" /S

if [ ! -f "$CABINET_PREFIX/drive_c/Program Files/Common Files/VST3/Line 6/Helix Stadium Native (x64).vst3" ]; then
    echo "$CABINET_NAME's installer left no Helix Stadium Native (x64).vst3 in the prefix" >&2
    exit 1
fi

if [ ! -f "$CABINET_PREFIX/drive_c/ProgramData/Line 6/Helix Stadium Native/data/ResourcesStadium.bin" ]; then
    echo "$CABINET_NAME installed no ResourcesStadium.bin; its models did not land" >&2
    exit 1
fi
