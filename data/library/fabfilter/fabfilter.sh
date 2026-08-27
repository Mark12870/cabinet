"$WINE" "$CABINET_ARCHIVE" /Unattended

drive="$CABINET_PREFIX/drive_c"
vst3=$(find "$drive/Program Files/Common Files/VST3" -maxdepth 1 -type f -iname 'FabFilter*.vst3' -print -quit 2>/dev/null)
clap=$(find "$drive/Program Files/Common Files/CLAP" -maxdepth 1 -type f -iname 'FabFilter*.clap' -print -quit 2>/dev/null)

if [ -z "$vst3" ]; then
    echo "$CABINET_NAME's installer left no FabFilter VST3 plugin in the prefix" >&2
    exit 1
fi

if [ -z "$clap" ]; then
    echo "$CABINET_NAME's installer left no FabFilter CLAP plugin in the prefix" >&2
    exit 1
fi
