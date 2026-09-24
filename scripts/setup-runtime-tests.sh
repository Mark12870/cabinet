#!/usr/bin/env bash
set -euo pipefail

die() {
    printf 'setup-runtime-tests: %s\n' "$*" >&2
    exit 1
}

APP=io.github.mark12870.cabinet
DAW_REF=fm.reaper.Reaper/x86_64/stable/34f34782be7d44a660a05de78ff29176ba36dad656300d50f27dd2f89b46a871
REPOSITORY=$(git rev-parse --show-toplevel)
BACKEND=${CABINET_RUNTIME_BACKEND:-toolbox}
PROBES=${CABINET_RUNTIME_PROBES:-1}
ENTRIES=${CABINET_RUNTIME_ENTRIES:-1}
ROOT=${CABINET_RUNTIME_ROOT:-${XDG_CACHE_HOME:-$HOME/.cache}/cabinet-rt}
BOX=${CABINET_RUNTIME_TOOLBOX:-cabinet-runtime}
IMAGE=${CABINET_RUNTIME_TOOLBOX_IMAGE:-registry.fedoraproject.org/fedora-toolbox:44}
HOST_FLATPAK_REPO=${CABINET_RUNTIME_HOST_FLATPAK_REPO:-$REPOSITORY/repo}
CABINET_REF=${CABINET_RUNTIME_CABINET_REF:-}
HOST_BUS=${DBUS_SESSION_BUS_ADDRESS:-}
COMMIT=97a9e0740baf6df2df942495c02532a624c44682

case "$ROOT/" in
    /*) ;;
    *) die "runtime root must be an absolute path: $ROOT" ;;
esac

[ -n "$HOST_BUS" ] || die "DBUS_SESSION_BUS_ADDRESS is not set"
command -v flatpak >/dev/null || die "flatpak is not installed"

if [ -z "$CABINET_REF" ]; then
    cabinet_commit=$(flatpak info --user --show-commit "$APP" 2>/dev/null) \
        || die "$APP is not installed for the current user"
    CABINET_REF="$APP/x86_64/stable/$cabinet_commit"
fi

case "$BACKEND" in
    toolbox)
        command -v toolbox >/dev/null || die "toolbox is not installed"
        command -v podman >/dev/null || die "podman is not installed"

        if ! podman container exists "$BOX" 2>/dev/null; then
            toolbox create "$BOX" --image "$IMAGE"
        fi

        podman update --memory=8G --memory-swap=8G "$BOX" >/dev/null
        mapfile -t packages < "$REPOSITORY/scripts/runtime-packages.txt"
        toolbox run --container "$BOX" sudo dnf install -y "${packages[@]}"
        runner=(toolbox run --container "$BOX")
        ;;
    direct)
        for command in c++ curl flatpak git magick make python3 sha256sum unzip weston \
            x86_64-w64-mingw32-gcc xdotool xprop xwd Xwayland; do
            command -v "$command" >/dev/null || die "$command is not installed"
        done
        runner=()
        ;;
    *)
        die "unknown runtime backend: $BACKEND"
        ;;
esac

mkdir -p "$ROOT"

"${runner[@]}" env \
    HOME="$ROOT/home" \
    XDG_RUNTIME_DIR="$ROOT/runtime" \
    XDG_DATA_HOME="$ROOT/home/.local/share" \
    XDG_CONFIG_HOME="$ROOT/home/.config" \
    XDG_CACHE_HOME="$ROOT/home/.cache" \
    FLATPAK_USER_DIR="$ROOT/home/.local/share/flatpak" \
    FLATPAK_SYSTEM_DIRS=/var/lib/flatpak \
    DBUS_SESSION_BUS_ADDRESS="$HOST_BUS" \
    CABINET_RUNTIME_ROOT="$ROOT" \
    CABINET_RUNTIME_BACKEND="$BACKEND" \
    CABINET_RUNTIME_PROBES="$PROBES" \
    CABINET_RUNTIME_ENTRIES="$ENTRIES" \
    CABINET_RUNTIME_TOOLBOX="$BOX" \
    bash -s -- "$ROOT" "$HOST_FLATPAK_REPO" "$CABINET_REF" "$DAW_REF" "$APP" "$COMMIT" \
    "$REPOSITORY/scripts/carla-vst3-view-removed.patch" <<'EOF'
set -euo pipefail

root=$1
host_flatpak_repo=$2
cabinet_ref=$3
daw_ref=$4
app=$5
commit=$6
carla_patch=$7
home=$root/home
runtime=$root/runtime
flatpak_user_dir=$home/.local/share/flatpak

case "$home/" in "$root/"*) ;; *) exit 1 ;; esac
case "$flatpak_user_dir/" in "$root/"*) ;; *) exit 1 ;; esac
[ -f "$host_flatpak_repo/config" ] || {
    printf 'setup-runtime-tests: the Cabinet Flatpak repository is not available: %s\n' "$host_flatpak_repo" >&2
    exit 1
}

for command in c++ curl flatpak git make sha256sum unzip; do
    command -v "$command" >/dev/null || {
        printf 'setup-runtime-tests: %s is not installed\n' "$command" >&2
        exit 1
    }
done

mkdir -p "$home/.local/share" "$home/.config" "$home/.cache" "$runtime" "$root/tmp" "$flatpak_user_dir"
: > "$root/.cabinet-runtime-root"
chmod 700 "$root" "$home" "$runtime"

flatpak remote-delete --user --force cabinet-local >/dev/null 2>&1 || true
flatpak remote-add --user --no-gpg-verify cabinet-local "file://$host_flatpak_repo"
flatpak remote-add --user --if-not-exists flathub https://dl.flathub.org/repo/flathub.flatpakrepo
flatpak config --user --set languages en
flatpak install --user --noninteractive --or-update cabinet-local "$cabinet_ref"

flatpak info --user "$app" >/dev/null 2>&1 || {
    printf 'setup-runtime-tests: %s is not installed\n' "$app" >&2
    exit 1
}

cabinet() {
    flatpak run \
        --nofilesystem=home \
        --filesystem="$root":create \
        --env=HOME="$home" \
        --env=XDG_RUNTIME_DIR="$runtime" \
        --env=FLATPAK_USER_DIR="$flatpak_user_dir" \
        --env=CABINET_RUNTIME_ROOT="$root" \
        "$app" "$@"
}

data=$home/.var/app/$app/data
carla_source=$data/carla-tests/source
carla_prefix=$data/carla-tests/prefix

cabinet_files=$flatpak_user_dir/app/$app/current/active/files
[ -f "$cabinet_files/lib/yabridge/libyabridge-chainloader-vst2.so" ] || {
    printf 'setup-runtime-tests: Cabinet yabridge chainloader is missing\n' >&2
    exit 1
}

yabridge="$home/.local/share/yabridge"
if [ -e "$yabridge" ] && [ ! -L "$yabridge" ]; then
    printf 'setup-runtime-tests: yabridge path is not a symbolic link: %s\n' "$yabridge" >&2
    exit 1
fi
rm -f "$yabridge"
ln -s "$cabinet_files/lib/yabridge" "$yabridge"

mkdir -p "$data/native" "$data/prefixes"

# The DAW and the entries below are what the general suite loads. A plugin scenario installs its
# own entry into a home of its own and plays it through Carla, so CABINET_RUNTIME_ENTRIES=0
# leaves all of them out.
if [ "${CABINET_RUNTIME_ENTRIES:-1}" = 1 ]; then
    flatpak install --user --noninteractive --or-update flathub "$daw_ref"
    flatpak info --user "$daw_ref" >/dev/null 2>&1 || {
        printf 'setup-runtime-tests: %s is not installed\n' "$daw_ref" >&2
        exit 1
    }

    daw=${daw_ref%%/*}
    cabinet enrol "$daw" >/dev/null
    flatpak override --user --reset "$daw"
    flatpak override --user "$daw" \
        --device=shm \
        --filesystem=xdg-run/yabridge:create \
        --filesystem="$cabinet_files":ro \
        --filesystem="$data/prefixes":ro \
        --filesystem="$data/native":ro \
        --talk-name=org.freedesktop.Flatpak \
        --env=WINELOADER="$cabinet_files/lib/yabridge/cabinet-wine" \
        --env=YABRIDGE_TEMP_DIR="$runtime/yabridge" \
        --env=YABRIDGE_NO_WATCHDOG=1
fi

if [ -e "$carla_source" ] && [ ! -d "$carla_source/.git" ]; then
    printf 'setup-runtime-tests: source path is not a Git checkout: %s\n' "$carla_source" >&2
    exit 1
fi

if [ ! -d "$carla_source/.git" ]; then
    mkdir -p "$(dirname "$carla_source")"
    git clone --recurse-submodules https://github.com/falkTX/Carla.git "$carla_source"
fi

if git -C "$carla_source" apply --reverse --check "$carla_patch" 2>/dev/null; then
    git -C "$carla_source" apply --reverse "$carla_patch"
fi

if [ -n "$(git -C "$carla_source" status --porcelain --untracked-files=all)" ]; then
    printf 'setup-runtime-tests: source checkout is dirty: %s\n' "$carla_source" >&2
    exit 1
fi

git -C "$carla_source" fetch --prune origin "$commit"
git -C "$carla_source" checkout --detach "$commit"
git -C "$carla_source" submodule update --init --recursive
git -C "$carla_source" apply "$carla_patch"

marker="$carla_prefix/.cabinet-carla-build"
build="$commit frontend $(sha256sum < "$carla_patch" | cut -d' ' -f1)"
if [ ! -x "$carla_prefix/bin/carla" ] || [ ! -f "$marker" ] || [ "$(<"$marker")" != "$build" ]; then
    if [ -f "$marker" ]; then
        make -C "$carla_source" clean
    fi

    mkdir -p "$carla_prefix"
    make -C "$carla_source" -j1
    make -C "$carla_source" PREFIX="$carla_prefix" install
    printf '%s\n' "$build" > "$marker"
fi

[ -x "$carla_prefix/bin/carla" ] || {
    printf 'setup-runtime-tests: Carla frontend was not installed\n' >&2
    exit 1
}

printf 'Carla installed in %s\n' "$carla_prefix"

installed() {
    local id=$1
    local line
    local mark
    local candidate
    local listing

    listing=$(cabinet library)
    while IFS= read -r line; do
        read -r mark candidate _ <<< "$line"
        if [ "$mark" = ok ] && [ "$candidate" = "$id" ]; then
            return 0
        fi
    done <<< "$listing"

    return 1
}

install_entry() {
    local id=$1
    shift

    if installed "$id"; then
        printf 'Already installed: %s\n' "$id"
        return
    fi

    cabinet library install "$id" "$@"
}

newest_release() {
    cabinet runners available | awk -v family="$1" '
        index($0, family " — ") == 1 { listed = 1; next }
        listed && NF == 2 { print $1, $2; exit }'
}

installed_runner() {
    [ -n "$1" ] && [ -x "$data/runners/$1/bin/wine" ] || {
        printf 'setup-runtime-tests: runner %s was not installed\n' "${1:-(none found)}" >&2
        exit 1
    }

    printf '%s\n' "$1"
}

install_newest() {
    local version runner
    read -r version runner <<< "$(newest_release "$1")"

    if [ -n "$runner" ] && [ ! -x "$data/runners/$runner/bin/wine" ]; then
        cabinet runners install "$version" >&2
    fi

    installed_runner "$runner"
}

install_newest_d2d1() {
    local url archive runner
    url=$(curl --fail --silent --show-error --location \
        https://api.github.com/repos/mklnln/wine-d2d1-dcomp/releases/latest |
        python3 -c 'import json, sys; print(next(asset["browser_download_url"] for asset in json.load(sys.stdin)["assets"] if asset["name"].endswith(".tar.zst")))')
    archive=$root/tmp/$(basename "$url")
    runner=$(basename "$url" .tar.zst)
    runner=${runner%-x86_64}

    if [ ! -x "$data/runners/$runner/bin/wine" ]; then
        curl --fail --location --retry 3 --retry-all-errors --output "$archive" "$url"
        cabinet runners add "$archive" >&2
        rm -f "$archive"
    fi

    installed_runner "$runner"
}

drag_drop_prefix() {
    local prefix=$1
    local runner=$2
    local current=

    if [ -f "$data/prefixes/$prefix/.cabinet-runner" ]; then
        current=$(<"$data/prefixes/$prefix/.cabinet-runner")
    fi

    if [ -d "$data/prefixes/$prefix/dosdevices" ] && [ "$current" = "$runner" ]; then
        return
    fi

    if [ -d "$data/prefixes/$prefix" ]; then
        printf 'y\n' | cabinet delete "$prefix" >/dev/null
    fi

    cabinet new "$prefix" ${runner:+"$runner"}
}

# The drag-and-drop probes say when a yabridge patch can go, which is a question for a refresh
# rather than for every run: their four prefixes and the three runner families they pin are five
# gigabytes that nothing else here needs.
if [ "${CABINET_RUNTIME_PROBES:-1}" = 1 ]; then
    newest_d2d1=$(install_newest_d2d1)
    newest_kron4ek=$(install_newest Kron4ek)
    newest_soda=$(install_newest Soda)

    drag_drop_prefix drag-drop-bundled ""
    drag_drop_prefix drag-drop-d2d1 "$newest_d2d1"
    drag_drop_prefix drag-drop-kron4ek "$newest_kron4ek"
    drag_drop_prefix drag-drop-soda "$newest_soda"
fi

if [ "${CABINET_RUNTIME_ENTRIES:-1}" = 1 ]; then
    install_entry sitala-1
    install_entry valhalla-supermassive
    install_entry decent-sampler
    install_entry surge-xt
    install_entry sine-player
    install_entry fabfilter-total-bundle
    install_entry ik-product-manager

    # Decent Sampler opens a modal welcome screen over its interface until it has shown it once,
    # and a sweep across that screen measures a window that takes no input. The plugin writes this
    # flag itself on first open; the fixtures own it instead, so a root that has never run it is
    # not a special case.
    welcome=$home/.config/DecentSampler/DecentSampler.xml
    if [ ! -f "$welcome" ]; then
        mkdir -p "$(dirname "$welcome")"
        cat > "$welcome" <<'XML'
<?xml version="1.0" encoding="UTF-8"?>

<PROPERTIES>
  <VALUE name="welcomeScreenAlreadyShown" val="1"/>
</PROPERTIES>
XML
    fi

    cabinet sync

    manager="$data/prefixes/ik-multimedia/drive_c/Program Files/IK Multimedia/IK Product Manager/IK Product Manager.exe"
    [ -f "$manager" ] || {
        printf 'setup-runtime-tests: IK Product Manager was not installed\n' >&2
        exit 1
    }
fi

printf 'backend=%s\n' "${CABINET_RUNTIME_BACKEND:-direct}" > "$root/config"
printf 'toolbox=%s\n' "${CABINET_RUNTIME_TOOLBOX:-}" >> "$root/config"
printf 'Carla fixtures are installed. No product was installed through IK Product Manager.\n'
EOF
