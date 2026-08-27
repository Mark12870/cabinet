response="${0%/*}/melodyne.iss"
log="$CABINET_WORK/melodyne-setup.log"
response_win=$("$WINE" winepath -w "$response")
log_win=$("$WINE" winepath -w "$log")

"$WINE" "$CABINET_ARCHIVE" /s "/f1$response_win" "/f2$log_win"

if ! grep -q '^ResultCode=0' "$log"; then
    echo "$CABINET_NAME's installer did not complete silently" >&2
    exit 1
fi

if [ ! -f "$CABINET_PREFIX/drive_c/Program Files/Common Files/VST3/Celemony/Melodyne/Melodyne.vst3" ]; then
    echo "$CABINET_NAME's installer left no Melodyne.vst3 in the prefix" >&2
    exit 1
fi
