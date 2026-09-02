winetricks --unattended powershell corefonts vcrun2022

"$WINE" winecfg -v win10

"$WINE" reg add 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' \
    /v EnableLUA /t REG_DWORD /d 0 /f
"$WINE" reg add 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' \
    /v ConsentPromptBehaviorAdmin /t REG_DWORD /d 0 /f

"$WINE" "$CABINET_ARCHIVE" /S

app="$CABINET_PREFIX/drive_c/Program Files/Native Instruments/Native Access/Native Access.exe"

if [ ! -f "$app" ]; then
    echo "$CABINET_NAME's installer left no Native Access.exe in the prefix" >&2
    exit 1
fi

downloads="$CABINET_PREFIX/drive_c/users/Public/Documents/Native Instruments/Downloads"
mkdir -p "$downloads"

"$WINE" reg add 'HKCU\Software\Native Instruments\Native Access' \
    /v DownloadLocation /d 'C:\users\Public\Documents\Native Instruments\Downloads' /f

daemon_dir="$CABINET_PREFIX/drive_c/Program Files/Native Instruments/Native Access/resources/daemon/win"
daemon=$(find "$daemon_dir" -maxdepth 1 -type f -name 'NTKDaemon*Setup PC.exe' | sort | tail -n 1)

if [ -z "$daemon" ]; then
    echo "$CABINET_NAME's installer left no NTKDaemon installer in the prefix" >&2
    exit 1
fi

timeout -k 5s 180s "$WINE" "$daemon" /s IAgree=Yes '/l=C:\cabinet-ntk-install.log' \
    </dev/null >/dev/null 2>&1 &
daemon_pid=$!

installed="$CABINET_PREFIX/drive_c/Program Files/Common Files/Native Instruments/NTK/NTKDaemon.exe"
found=1
deadline=$((SECONDS + 180))
while [ "$SECONDS" -lt "$deadline" ]; do
    if [ -s "$installed" ] && timeout -k 2s 5s "$WINE" sc query NTKDaemonService >/dev/null 2>&1; then
        found=0
        break
    fi

    sleep 1
done

kill "$daemon_pid" 2>/dev/null || true

if [ "$found" -ne 0 ]; then
    echo "$CABINET_NAME's daemon installer left no installed NTKDaemonService" >&2
    exit 1
fi

"$WINE" sc stop NTKDaemonService >/dev/null 2>&1 || true
"$(dirname "$WINE")/wineserver" -k >/dev/null 2>&1 || true
