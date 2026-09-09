destination="$CABINET_PREFIX/drive_c/Program Files/Splice"
mkdir -p "$destination"
unzip -q -o "$CABINET_ARCHIVE" -d "$destination"

if [ ! -f "$destination/Splice.exe" ]; then
    echo "$CABINET_NAME's archive left no Splice.exe in the prefix" >&2
    exit 1
fi

if [ ! -f "$destination/updatecheck.exe" ]; then
    echo "$CABINET_NAME's archive left no updatecheck.exe in the prefix" >&2
    exit 1
fi
