package="$CABINET_PREFIX/drive_c/windows/temp/PA-InstallationManager.msi"
manager="$CABINET_PREFIX/drive_c/Program Files/Plugin Alliance/Installation Manager/PA-InstallationManager.exe"
trap 'rm -f "$package"' EXIT

unzip -p "$CABINET_ARCHIVE" '*.msi' > "$package"
"$WINE" msiexec /i 'C:\windows\temp\PA-InstallationManager.msi' /qn

if [ ! -f "$manager" ]; then
    echo "$CABINET_NAME's installer left no PA-InstallationManager.exe in the prefix" >&2
    exit 1
fi
