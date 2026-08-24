"$WINE" "$CABINET_ARCHIVE" /S

for disabled in "*wbemprox" "*wbemcomn"; do
    "$WINE" reg add "HKCU\\Software\\Wine\\DllOverrides" \
        /v "$disabled" /t REG_SZ /d native /f
done

if [ ! -f "$CABINET_PREFIX/drive_c/Program Files/Common Files/VST3/Line 6/Helix Native (x64).vst3" ]; then
    echo "$CABINET_NAME's installer left no Helix Native (x64).vst3 in the prefix" >&2
    exit 1
fi

if [ ! -f "$CABINET_PREFIX/drive_c/ProgramData/Line 6/HelixCore/HelixCoreWin_x64.dll" ]; then
    echo "$CABINET_NAME installed no HelixCore; its models did not land" >&2
    exit 1
fi
