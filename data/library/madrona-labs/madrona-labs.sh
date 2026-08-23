"$WINE" "$CABINET_ARCHIVE" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART

drive="$CABINET_PREFIX/drive_c"
uninstaller="$drive/ProgramData/Madrona Labs/$CABINET_NAME/InstallerFiles/unins000.exe"

if [ ! -f "$uninstaller" ]; then
    echo "$CABINET_NAME's installer never ran; the prefix has no uninstaller for it" >&2
    exit 1
fi

common="$drive/Program Files/Common Files"

for bundle in "$common/VST3"/*.vst3; do
    module=$(find "$bundle/Contents" -maxdepth 2 -name '*.vst3' 2>/dev/null | head -n 1)
    [ -n "$module" ] || continue

    matching="$common/VST3/$(basename "$module")"
    [ "$bundle" = "$matching" ] || mv "$bundle" "$matching"
done

landed=$(find "$common/VST2" "$common/VST3" -maxdepth 1 -iname "$CABINET_ID*" 2>/dev/null)

if [ -z "$landed" ]; then
    echo "$CABINET_NAME installed, but left nothing where yabridge looks" >&2
    exit 1
fi
