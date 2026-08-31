unpacked="$CABINET_WORK/unpacked"
mkdir -p "$unpacked"
unzip -q -o "$CABINET_ARCHIVE" -d "$unpacked"

if [ -d "$unpacked/VitalBinaries" ]; then
    binaries="$unpacked/VitalBinaries"
    mv "$binaries/Vital.vst3" "$CABINET_DEST/"
    mv "$binaries/Vital.so" "$CABINET_DEST/"
    mv "$binaries/Vital.lv2" "$CABINET_DEST/"
elif [ -d "$unpacked/VitalInstaller/lib" ]; then
    binaries="$unpacked/VitalInstaller/lib"
    mv "$binaries/vst/Vital.so" "$CABINET_DEST/"
    mv "$binaries/vst3/Vital.vst3" "$CABINET_DEST/"
    mv "$binaries/clap/Vital.clap" "$CABINET_DEST/"
else
    echo "$CABINET_NAME's archive has an unknown layout" >&2
    exit 1
fi
