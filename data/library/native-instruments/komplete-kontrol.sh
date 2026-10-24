drive_c="$CABINET_PREFIX/drive_c"
standalone="$drive_c/Program Files/Native Instruments/Komplete Kontrol/Komplete Kontrol.exe"
vst3="$drive_c/Program Files/Common Files/VST3"

if [ ! -f "$standalone" ]; then
    exit 0
fi

appdata=$(find "$drive_c/users" -mindepth 3 -maxdepth 3 -path '*/AppData/Local' -print -quit)
list="$appdata/Native Instruments/Komplete Kontrol/Plugin_kk3.data"

unlisted() {
    [ -f "$list" ] || return 0
    find "$vst3" -name '*.vst3' -prune ! -name 'Komplete Kontrol.vst3' -print | {
        while read -r plugin; do
            grep -aqF "\\$(basename "$plugin")" "$list" || exit 0
        done
        exit 1
    }
}

if ! unlisted; then
    exit 0
fi

echo "Komplete Kontrol is scanning its plugins, so it can host them in a DAW. This takes a few minutes."
if ! "$WINE" start /min 'C:\Program Files\Native Instruments\Komplete Kontrol\Komplete Kontrol.exe' >/dev/null 2>&1; then
    echo "Komplete Kontrol did not start, so it could not scan its plugins" >&2
    exit 1
fi

started=$SECONDS
while [ "$((SECONDS - started))" -lt 600 ]; do
    if ! unlisted; then
        echo "Komplete Kontrol finished scanning its plugins"
        exit 0
    fi

    if [ "$((SECONDS - started))" -gt 30 ] && ! pgrep -f 'Komplete Kontrol.exe' >/dev/null; then
        break
    fi

    sleep 1
done

echo "Komplete Kontrol did not finish scanning its plugins; Cabinet scans again when Native Access next opens or closes"
