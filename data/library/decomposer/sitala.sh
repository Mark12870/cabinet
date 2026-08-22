"$WINE" msiexec /i "$CABINET_ARCHIVE" /qn

program="$CABINET_PREFIX/drive_c/Program Files"
vst2="$program/Steinberg/VstPlugins/Sitala.dll"

if [ ! -f "$vst2" ]; then
    echo "$CABINET_NAME's installer left no Sitala.dll in the prefix" >&2
    exit 1
fi

if [ ! -d "$program/Sitala/Factory Kits" ]; then
    echo "$CABINET_NAME installed no factory kits" >&2
    exit 1
fi

rm -f "$CABINET_PREFIX/drive_c/Program Files (x86)/Steinberg/VstPlugins/Sitala.dll"

if [ -e "$program/Common Files/VST3/Sitala.vst3" ]; then
    rm -f "$vst2"
fi
