"$WINE" "$CABINET_ARCHIVE" /S

if [ ! -d "$CABINET_PREFIX/drive_c/Program Files/Common Files/VST3/Serum2.vst3" ]; then
    echo "$CABINET_NAME's installer left no Serum2.vst3 in the prefix" >&2
    exit 1
fi

if [ -z "$(find "$CABINET_PREFIX/drive_c/users" -type d -name Tables)" ]; then
    echo "$CABINET_NAME installed no wavetables; its content folder was not writable" >&2
    exit 1
fi
