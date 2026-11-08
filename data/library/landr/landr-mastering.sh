package="$CABINET_PREFIX/drive_c/windows/temp/LANDRMastering.msi"
trap 'rm -f "$package"' EXIT
cp "$CABINET_ARCHIVE" "$package"
"$WINE" msiexec /i 'C:\windows\temp\LANDRMastering.msi' /qn

if [ ! -e "$CABINET_PREFIX/drive_c/Program Files/Common Files/VST3/$CABINET_NAME.vst3" ]; then
    echo "$CABINET_NAME's installer left no $CABINET_NAME.vst3 in the prefix" >&2
    exit 1
fi
