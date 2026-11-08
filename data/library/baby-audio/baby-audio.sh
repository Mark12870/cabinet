work="$CABINET_WORK/unpacked"
mkdir -p "$work"
unzip -q "$CABINET_ARCHIVE" -d "$work" -x '__MACOSX/*'

installer=$(find "$work" -maxdepth 1 -name '*.exe')

if [ ! -f "$installer" ]; then
    echo "$CABINET_NAME's download holds no installer" >&2
    exit 1
fi

"$WINE" "$installer" /SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART '/COMPONENTS=x64bit,x64bit\vst2,x64bit\vst3'

if [ ! -e "$CABINET_PREFIX/drive_c/Program Files/Common Files/VST3/$CABINET_NAME.vst3" ]; then
    echo "$CABINET_NAME's installer left no $CABINET_NAME.vst3 in the prefix" >&2
    exit 1
fi
