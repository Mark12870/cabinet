"$WINE" "$CABINET_ARCHIVE" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART

case "$CABINET_ID" in
    auto-align-2) plugin=AutoAlign2 ;;
    auto-align-post-2) plugin=AutoAlignPost ;;
    drum-leveler) plugin=DrumLeveler ;;
    surfereq-2) plugin=SurferEQ ;;
    *) plugin=$CABINET_NAME ;;
esac
if [ ! -e "$CABINET_PREFIX/drive_c/Program Files/Common Files/VST3/$plugin.vst3" ]; then
    echo "$CABINET_NAME's installer left no $plugin.vst3 in the prefix" >&2
    exit 1
fi
