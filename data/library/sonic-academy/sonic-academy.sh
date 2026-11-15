case "$CABINET_ARCHIVE" in
    *.[zZ][iI][pP])
        work="$CABINET_WORK/unpacked"
        mkdir -p "$work"
        unzip -q "$CABINET_ARCHIVE" -d "$work" -x '__MACOSX/*'
        installer=$(find "$work" -type f -iname '*.exe' ! -name '._*' ! -iname 'Uninstall*' -print -quit)
        ;;
    *)
        installer="$CABINET_ARCHIVE"
        ;;
esac

if [ ! -f "$installer" ]; then
    echo "$CABINET_NAME's download holds no Windows installer" >&2
    exit 1
fi

"$WINE" "$installer" /S

if [ -z "$(find "$CABINET_PREFIX/drive_c/Program Files/Common Files/VST3" -maxdepth 1 -iname "$CABINET_NAME.vst3" -print -quit 2>/dev/null)" ]; then
    echo "$CABINET_NAME's installer left no $CABINET_NAME.vst3 in the prefix" >&2
    exit 1
fi
