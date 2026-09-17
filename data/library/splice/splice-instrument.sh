log="$CABINET_WORK/$CABINET_ID-installer.log"
flags='--no-sandbox --disable-gpu-sandbox --disable-gpu --disable-gpu-compositing --in-process-gpu'

"$WINE" "$CABINET_ARCHIVE" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART >"$log" 2>&1
"$WINE" reg add 'HKCU\Software\Wine\AppDefaults\msedgewebview2.exe' /v Version /d win7 /f >>"$log" 2>&1
"$WINE" reg add 'HKLM\System\CurrentControlSet\Services\edgeupdate' /v Start /t REG_DWORD /d 4 /f >>"$log" 2>&1
for host in yabridge-host.exe 'Splice INSTRUMENT.exe'; do
    "$WINE" reg add 'HKLM\Software\Policies\Microsoft\Edge\WebView2\AdditionalBrowserArguments' /v "$host" \
        /d "$flags" /f >>"$log" 2>&1
done

vst3="$CABINET_PREFIX/drive_c/Program Files/Common Files/VST3/Splice/Splice INSTRUMENT.vst3"
app="$CABINET_PREFIX/drive_c/Program Files/Splice/Splice INSTRUMENT/Splice INSTRUMENT.exe"

if [ ! -d "$vst3" ]; then
    echo "$CABINET_NAME's installer left no Splice INSTRUMENT VST3 plugin in the prefix" >&2
    exit 1
fi

if [ ! -f "$app" ]; then
    echo "$CABINET_NAME's installer left no Splice INSTRUMENT application in the prefix" >&2
    exit 1
fi
