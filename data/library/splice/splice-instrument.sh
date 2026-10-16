log="$CABINET_WORK/$CABINET_ID-installer.log"

"$WINE" "$CABINET_ARCHIVE" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART >"$log" 2>&1

vst3="$CABINET_PREFIX/drive_c/Program Files/Common Files/VST3/Splice/Splice INSTRUMENT.vst3"
app="$CABINET_PREFIX/drive_c/Program Files/Splice/Splice INSTRUMENT/Splice INSTRUMENT.exe"

if [ ! -d "$vst3" ]; then
    echo "$CABINET_NAME's installer left no Splice INSTRUMENT VST3 plugin in the prefix" >&2
    exit 1
fi

if [ ! -f "$app" ]; then
    echo "$CABINET_NAME's installer left no Splice INSTRUMENT application in the prefix" >&2
    exit 1
fi
