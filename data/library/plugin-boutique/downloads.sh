downloads="$CABINET_PREFIX/drive_c/users/Public/Downloads/Plugin Boutique"
common="$CABINET_PREFIX/drive_c/Program Files/Common Files"

mkdir -p "$downloads"

for profile in "$CABINET_PREFIX"/drive_c/users/*/; do
    settings="$profile/AppData/Roaming/Plugin Boutique"

    if [ ! -d "$profile" ] || [ "$(basename "$profile")" = Public ] || [ -f "$settings/preferences.json" ]; then
        continue
    fi

    mkdir -p "$settings"
    printf '{\n  "preferences": {\n    "downloadLocation": "C:\\\\users\\\\Public\\\\Downloads\\\\Plugin Boutique"\n  }\n}\n' \
        > "$settings/preferences.json"
done

if grep -qi 'Classes\\\\folder\\\\shell\\\\open\\\\ddeexec\]' "$CABINET_PREFIX/system.reg" 2>/dev/null; then
    "$WINE" reg delete 'HKLM\Software\Classes\Folder\shell\open\ddeexec' /f >/dev/null
    "$WINE" reg add 'HKLM\Software\Classes\Folder\shell\open\command' /ve /d 'C:\windows\system32\rundll32.exe' /f >/dev/null
fi

[ -d "$CABINET_KEPT" ] || exit 0

shelved="$downloads/Not installed"
failed=

busy() {
    "$WINE" tasklist 2>/dev/null | grep -qiF "$(printf '%.25s' "$1")"
}

installed_by_app() {
    busy "$1" || return 1
    echo "  Plugin Boutique App is running $1 itself, so Cabinet waits for it"
    while busy "$1"; do
        sleep 2
    done
}

started_by_app() {
    while [ $(($(date +%s) - $(stat -c %Y "$2"))) -lt 15 ] && ! busy "$1"; do
        sleep 1
    done
    installed_by_app "$1"
}

run() {
    setup=$(basename "$1")
    file=$1

    installed_by_app "$setup" && return 0

    if grep -qa 'Inno Setup' "$1"; then
        set -- "$1" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
    elif grep -qa 'Nullsoft' "$1"; then
        set -- "$1" /S
    elif grep -qa '\.wixburn' "$1"; then
        set -- "$1" /quiet /norestart
    fi

    case "$1" in
        *.[mM][sS][iI]) set -- msiexec /i "$@" /qn ;;
    esac

    timeout -k 10s 30m "$WINE" "$@" </dev/null && return 0
    exited=$?

    installed_by_app "$setup" && return 0

    if [ "$#" -gt 1 ]; then
        echo "  $setup could not install silently (exit $exited), so its own installer opens"
        case "$file" in
            *.[mM][sS][iI]) set -- msiexec /i "$file" ;;
            *) set -- "$file" ;;
        esac
        timeout -k 10s 30m "$WINE" "$@" </dev/null && return 0
        exited=$?
    fi

    echo "  $setup exited with $exited"
    return 1
}

packed() {
    7z l -tPE "$1" 2>/dev/null | grep -q 'RCDATA/CABINET'
}

wide() {
    offset=$(od -An -tu4 -j60 -N4 "$1" | tr -d ' ')
    [ -n "$offset" ] && [ "$(od -An -tx2 -j$((offset + 4)) -N2 "$1" | tr -d ' ')" = 8664 ]
}

place() {
    mkdir -p "$common/$2"
    cp -R "$1" "$common/$2/"
    echo "  placed $(basename "$1") in Common Files\\$2"
}

install_from() (
    installed=1

    for setup in "$1"/*; do
        case "$setup" in
            */unins*) ;;
            *.[mM][sS][iI]) echo "  running $(basename "$setup")"; run "$setup" || exit 1; installed=0 ;;
            *.[eE][xX][eE])
                if packed "$setup"; then
                    7z x -y "-o$setup.unpacked" "$setup" >/dev/null || exit 1
                    install_from "$setup.unpacked" && installed=0
                else
                    echo "  running $(basename "$setup")"
                    run "$setup" || exit 1
                    installed=0
                fi
                ;;
            *.[vV][sS][tT]3) place "$setup" VST3; installed=0 ;;
            *.[cC][lL][aA][pP]) place "$setup" CLAP; installed=0 ;;
            *.[dD][lL][lL])
                if wide "$setup"; then
                    place "$setup" VST2
                    for content in "${setup%.*}".*; do
                        [ "$content" = "$setup" ] || place "$content" VST2
                    done
                    installed=0
                elif grep -qa VSTPluginMain "$setup"; then
                    echo "  $(basename "$setup") is a 32-bit plugin"
                    touch "$work/.32-bit"
                fi
                ;;
            *__MACOSX) ;;
            *) [ -d "$setup" ] && install_from "$setup" && installed=0 ;;
        esac
    done

    exit "$installed"
)

failure() {
    if [ -f "$work/.32-bit" ]; then
        echo "holds only a 32-bit plugin, and this prefix can bridge 64-bit ones only"
    else
        echo "did not install"
    fi
}

shelve() {
    mkdir -p "$shelved"
    mv -f "$downloads/$1" "$shelved/" 2>/dev/null
    echo "$1 $2, so it is in C:\\users\\Public\\Downloads\\Plugin Boutique\\Not installed"
    failed=1
}

for download in "$CABINET_KEPT"/*; do
    [ -f "$download" ] || continue
    name=$(basename "$download")
    work="$CABINET_KEPT/.unpacked"
    rm -rf "$work"
    mkdir -p "$work"

    case "$name" in
        *.[zZ][iI][pP])
            echo "Installing $name"
            if ! 7z x -y "-o$work" "$download" >/dev/null; then
                shelve "$name" "could not be unpacked"
            elif install_from "$work"; then
                rm -f "$downloads/$name"
            else
                shelve "$name" "$(failure)"
            fi
            ;;
        *.[eE][xX][eE] | *.[mM][sS][iI])
            echo "Installing $name"
            cp "$download" "$work/"
            if started_by_app "$name" "$download" || install_from "$work"; then
                rm -f "$downloads/$name"
            else
                shelve "$name" "$(failure)"
            fi
            ;;
    esac

    rm -rf "$work" "$download"
done

[ -z "$failed" ]
