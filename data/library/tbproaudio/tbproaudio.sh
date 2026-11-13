work="$CABINET_WORK/unpacked"
mkdir -p "$work"
unzip -q "$CABINET_ARCHIVE" -d "$work"

installer=$(find "$work" -maxdepth 1 -name '*_Installer.exe')

if [ ! -f "$installer" ]; then
    echo "$CABINET_NAME's download holds no installer" >&2
    exit 1
fi

"$WINE" "$installer" /SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /COMPONENTS=vst2_64,vst3_64,clap_64,manual

plugin=$(basename "$installer" _Installer.exe)

if [ ! -e "$CABINET_PREFIX/drive_c/Program Files/Common Files/VST3/TBProAudio/$plugin.vst3" ]; then
    echo "$CABINET_NAME's installer left no $plugin.vst3 in the prefix" >&2
    exit 1
fi
