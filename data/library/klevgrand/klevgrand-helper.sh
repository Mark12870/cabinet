bundle="$(dirname "$0")/klevgrand-helper-runtime.zip"
runtime="$CABINET_WORK/bsdtar"
system32="$CABINET_PREFIX/drive_c/windows/system32"

if [ ! -f "$bundle" ]; then
    echo "$CABINET_NAME's archive helper was not shipped" >&2
    exit 1
fi

mkdir -p "$runtime"
unzip -q "$bundle" -d "$runtime"
mkdir -p "$system32"
cp "$runtime/tar.exe" "$runtime"/*.dll "$system32/"

if [ ! -f "$system32/tar.exe" ]; then
    echo "$CABINET_NAME's archive helper could not be installed" >&2
    exit 1
fi

work="$CABINET_WORK/unpacked"
mkdir -p "$work"
unzip -q "$CABINET_ARCHIVE" -d "$work"

installer=$(find "$work" -type f -name '*.exe' -print -quit)
helper="$CABINET_PREFIX/drive_c/Program Files (x86)/Klevgrand/Klevgrand Helper/Klevgrand Helper.exe"

if [ -z "$installer" ]; then
    echo "$CABINET_NAME's archive holds no installer" >&2
    exit 1
fi

if [ ! -f "$helper" ]; then
    "$WINE" "$installer" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
fi

if [ ! -f "$helper" ]; then
    echo "$CABINET_NAME's installer left no Klevgrand Helper.exe in the prefix" >&2
    exit 1
fi
