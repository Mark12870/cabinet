case "$CABINET_ID" in
    mantra|tone-king-imperial-mkii-x)
        "$WINE" "$CABINET_ARCHIVE" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART ;;
    *)
        "$WINE" "$CABINET_ARCHIVE" /exenoui /qn /norestart ;;
esac

plugin=$(printf '%s' "$CABINET_NAME" | tr -d ':')
case "$CABINET_ID" in
    fortin-nts-suite) plugin="$plugin (x64)" ;;
esac
if [ ! -e "$CABINET_PREFIX/drive_c/Program Files/Common Files/VST3/$plugin.vst3" ]; then
    echo "$CABINET_NAME's installer left no $plugin.vst3 in the prefix" >&2
    exit 1
fi
