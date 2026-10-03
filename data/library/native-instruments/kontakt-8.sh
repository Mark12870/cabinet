if [ "${1:-}" = --installing ]; then
    export PATH="/app/bin:/usr/bin:/bin"
    drive_c="${2%/}"
    temp="$drive_c/windows/temp"
    result="$temp/cabinet-msi.result"
    trap '[ -s "$result" ] || echo failed > "$result"' EXIT

    command=$(cat "$temp/cabinet-msi.command")
    source=$(printf '%s\n' "$command" | sed -n 's/.*SRCDIR=\("[^"]*"\|[^ ]*\).*/\1/p')
    prefix=$(basename "${drive_c%/drive_c}")

    cabinet msi "$prefix" "$3" "$command" "SourceDir=$source" >"$temp/cabinet-msi.log" 2>&1 &&
        echo ok > "$result"
    exit 0
fi

drive_c="$CABINET_PREFIX/drive_c"
vst3="$drive_c/Program Files/Common Files/VST3/Kontakt 8.vst3"
install_dir="$drive_c/Program Files/Native Instruments/Kontakt 8"
content_dir="$drive_c/Program Files/Common Files/Native Instruments/Kontakt 8"
system="$drive_c/windows/syswow64"
result="$drive_c/windows/temp/cabinet-msi.result"
stand_in="$(dirname "$0")/msi.dll"
wine_msi="$(dirname "$WINE")/../lib/wine/i386-windows/msi.dll"

quietly() {
    output=$("$@" 2>&1) || { printf '%s\n' "$output" >&2; return 1; }
}

if [ ! -f "$wine_msi" ]; then
    echo "The runner has no 32-bit msi.dll for Kontakt 8's installer to fall back on" >&2
    exit 1
fi

if ! cmp -s "$stand_in" "$system/msi.dll" || ! cmp -s "$wine_msi" "$system/msi_wine.dll"; then
    install -m644 "$wine_msi" "$system/msi_wine.dll"
    install -m644 "$stand_in" "$system/msi.dll"
    quietly "$WINE" reg add 'HKCU\Software\Wine\AppDefaults\Kontakt 8 Setup PC.exe\DllOverrides' \
        /v msi /d native /f
fi

if [ "$(cat "$result" 2>/dev/null)" = failed ]; then
    rm -f "$result"
    echo "Native Access could not install Kontakt 8:" >&2
    cat "$drive_c/windows/temp/cabinet-msi.log" >&2
    echo "Install or update it again in Native Access once that is resolved." >&2
fi

if [ -f "$install_dir/Kontakt 8.exe" ]; then
    exit 0
fi

archive=$(find "$CABINET_KEPT" -maxdepth 1 -type f -iname '*kontakt*8*.zip' -printf '%T@ %p\n' 2>/dev/null | \
    sort -nr | awk '{ sub(/^[^ ]+ /, ""); print; exit }')

if [ -n "$archive" ]; then
    echo "Installing Kontakt 8 from $(basename "$archive")"

    work="$drive_c/cabinet-kontakt"
    rm -rf "$work"
    mkdir -p "$work"
    7z x -y "-o$work" "$archive" >/dev/null 2>&1 || true
    setup=$(find "$work" -type f -iname '*Kontakt 8*Setup PC.exe' | sort | tail -n 1)

    if [ -z "$setup" ]; then
        echo "The kept Kontakt 8 download held no installer; install Kontakt 8 again in Native Access" >&2
        rm -rf "$work"
        rm -f "$archive" "$archive".*
        exit 1
    fi

    rm -f "$result"
    timeout -k 10s 30m "$WINE" "$setup" /s </dev/null >/dev/null 2>&1 || true
    rm -rf "$work"
    rm -f "$archive" "$archive".*

    if [ "$(cat "$result" 2>/dev/null)" != ok ] || [ ! -f "$install_dir/Kontakt 8.exe" ]; then
        echo "Kontakt 8's installer did not finish; install Kontakt 8 again in Native Access" >&2
        exit 1
    fi

    echo "Installed Kontakt 8"
    exit 0
fi

if [ -s "$vst3" ] && [ ! -d "$install_dir" ]; then
    mkdir -p "$install_dir" "$content_dir"
    key='HKLM\SOFTWARE\Native Instruments\Kontakt 8'
    quietly "$WINE" reg add "$key" /v InstallDir /d 'C:\Program Files\Native Instruments\Kontakt 8' /f
    quietly "$WINE" reg add "$key" /v ContentDir /d 'C:\Program Files\Common Files\Native Instruments\Kontakt 8' /f
    quietly "$WINE" reg add "$key" /v ContentVersion /d 4.0 /f
    quietly "$WINE" reg add "$key" /v InstallVST364Dir /d 'C:\Program Files\Common Files\VST3' /f
    echo "Registered Kontakt 8's install folders, so Native Access can update or repair it"
fi
