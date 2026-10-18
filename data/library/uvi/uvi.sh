"$WINE" "$CABINET_ARCHIVE" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART

if [ ! -f "$CABINET_PREFIX/drive_c/Program Files (x86)/UVI Portal/UVI Portal.exe" ]; then
    echo "$CABINET_NAME's installer left no UVI Portal.exe in the prefix" >&2
    exit 1
fi

runtime="$CABINET_PREFIX/drive_c/Program Files (x86)/Microsoft/EdgeWebView/Application"
client='HKLM\Software\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
found=1
deadline=$((SECONDS + 300))
while [ "$SECONDS" -lt "$deadline" ]; do
    if find "$runtime" -name msedgewebview2.exe -print -quit 2>/dev/null | grep -q . \
        && timeout -k 2s 10s "$WINE" reg query "$client" /v pv >/dev/null 2>&1; then
        found=0
        break
    fi

    sleep 2
done

if [ "$found" -ne 0 ]; then
    echo "$CABINET_NAME's installer left no WebView2 runtime in the prefix" >&2
    exit 1
fi
