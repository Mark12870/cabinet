log="$CABINET_WORK/setup.log"
pace='License Support Win64.exe'

timeout -k 10s 1200s "$WINE" "$CABINET_ARCHIVE" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART "/LOG=Z:$(printf '%s' "$log" | tr / '\\')" &
setup=$!

stopped=
while [ -z "$stopped" ] && kill -0 "$setup" 2>/dev/null; do
    if grep -q " Filename: .*$pace" "$log" 2>/dev/null &&
        timeout -k 2s 10s "$WINE" taskkill /f /im "$pace" >/dev/null 2>&1; then
        stopped=yes
    fi
    sleep 1
done

if ! wait "$setup"; then
    echo "$CABINET_NAME's installer failed or did not finish within 20 minutes" >&2
    exit 1
fi

if [ ! -e "$CABINET_PREFIX/drive_c/Program Files/Common Files/VST3/Soundtoys/EchoBoy.vst3" ]; then
    echo "$CABINET_NAME's installer left no EchoBoy.vst3 in the prefix" >&2
    exit 1
fi
