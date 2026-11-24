work="$CABINET_WORK/unpacked"
vst3="$CABINET_PREFIX/drive_c/Program Files/Common Files/VST3"
vst2="$CABINET_PREFIX/drive_c/Program Files/VstPlugins"
mkdir -p "$work" "$vst3" "$vst2"
unzip -q "$CABINET_ARCHIVE" -d "$work"

if [ ! -f "$work/$CABINET_NAME (64).vst3" ] || [ ! -f "$work/$CABINET_NAME (64).dll" ]; then
    echo "$CABINET_NAME's ZIP holds no 64-bit $CABINET_NAME (64).vst3 and $CABINET_NAME (64).dll" >&2
    exit 1
fi

cp "$work/$CABINET_NAME (64).vst3" "$vst3/$CABINET_NAME.vst3"
cp "$work/$CABINET_NAME (64).dll" "$vst2/$CABINET_NAME.dll"
