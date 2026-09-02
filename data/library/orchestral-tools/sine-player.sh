"$WINE" "$CABINET_ARCHIVE" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART

"$WINE" reg add 'HKCU\Software\Wine\AppDefaults\msedgewebview2.exe' /v Version /d win7 /f
"$WINE" reg add 'HKCU\Software\Wine' /v Version /d win7 /f

drive="$CABINET_PREFIX/drive_c/Program Files"
vst3="$drive/Common Files/VST3/SINE Player.vst3/Contents/x86_64-win/SINE Player.vst3"
vst2="$drive/VstPlugins/SINE Player.dll"
app="$drive/SINE Player/SINE Player.exe"

if [ ! -f "$vst3" ]; then
    echo "$CABINET_NAME's installer left no SINE Player VST3 plugin in the prefix" >&2
    exit 1
fi

if [ ! -f "$vst2" ]; then
    echo "$CABINET_NAME's installer left no SINE Player VST2 plugin in the prefix" >&2
    exit 1
fi

if [ ! -f "$app" ]; then
    echo "$CABINET_NAME's installer left no SINE Player application in the prefix" >&2
    exit 1
fi
