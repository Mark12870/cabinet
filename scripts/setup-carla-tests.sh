#!/usr/bin/env bash
set -euo pipefail

die() {
    printf 'setup-carla-tests: %s\n' "$*" >&2
    exit 1
}

APP=io.github.mark12870.cabinet
DAW_REF=fm.reaper.Reaper/x86_64/stable/34f34782be7d44a660a05de78ff29176ba36dad656300d50f27dd2f89b46a871
REPOSITORY=$(git rev-parse --show-toplevel)
BACKEND=${CABINET_RUNTIME_BACKEND:-toolbox}
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
        toolbox run --container "$BOX" sudo dnf install -y \
            alsa-lib-devel \
            curl \
            file-devel \
            flatpak \
            fontconfig \
            gcc-c++ \
            git \
            libX11-devel \
            libXcursor-devel \
            libXext-devel \
            libXrandr-devel \
            liblo-devel \
            libsamplerate-devel \
            libsndfile-devel \
            make \
            mesa-dri-drivers \
            openbox \
            pkgconf-pkg-config \
            pulseaudio-libs-devel \
            unzip \
            xorg-x11-server-Xvfb \
            xorg-x11-xauth
        runner=(toolbox run --container "$BOX")
        ;;
    direct)
        for command in curl flatpak git make sha256sum unzip; do
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
    CABINET_RUNTIME_TOOLBOX="$BOX" \
    bash -s -- "$ROOT" "$HOST_FLATPAK_REPO" "$CABINET_REF" "$DAW_REF" "$APP" "$COMMIT" <<'EOF'
set -euo pipefail

root=$1
host_flatpak_repo=$2
cabinet_ref=$3
daw_ref=$4
app=$5
commit=$6
home=$root/home
runtime=$root/runtime
flatpak_user_dir=$home/.local/share/flatpak

case "$home/" in "$root/"*) ;; *) exit 1 ;; esac
case "$flatpak_user_dir/" in "$root/"*) ;; *) exit 1 ;; esac
[ -f "$host_flatpak_repo/config" ] || {
    printf 'setup-carla-tests: the Cabinet Flatpak repository is not available: %s\n' "$host_flatpak_repo" >&2
    exit 1
}

for command in curl flatpak git make sha256sum unzip; do
    command -v "$command" >/dev/null || {
        printf 'setup-carla-tests: %s is not installed\n' "$command" >&2
        exit 1
    }
done

mkdir -p "$home/.local/share" "$home/.config" "$home/.cache" "$runtime" "$root/tmp" "$flatpak_user_dir"
chmod 700 "$root" "$home" "$runtime"

flatpak remote-delete --user --force cabinet-local >/dev/null 2>&1 || true
flatpak remote-add --user --no-gpg-verify cabinet-local "file://$host_flatpak_repo"
flatpak remote-add --user --if-not-exists flathub https://dl.flathub.org/repo/flathub.flatpakrepo
flatpak install --user --noninteractive --or-update cabinet-local "$cabinet_ref"
flatpak install --user --noninteractive --or-update flathub "$daw_ref"

flatpak info --user "$app" >/dev/null 2>&1 || {
    printf 'setup-carla-tests: %s is not installed\n' "$app" >&2
    exit 1
}
flatpak info --user "$daw_ref" >/dev/null 2>&1 || {
    printf 'setup-carla-tests: %s is not installed\n' "$daw_ref" >&2
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
surge_prefix=carla-surge-windows
surge_url=https://github.com/surge-synthesizer/releases-xt/releases/download/1.3.4/surge-xt-win64-1.3.4-pluginsonly.zip
surge_sha256=564e162c560af07ad4ed47fe1bfcd827cf97a575de30d06c48249aad2e7c35e6

cabinet_files=$flatpak_user_dir/app/$app/current/active/files
[ -f "$cabinet_files/lib/yabridge/libyabridge-chainloader-vst2.so" ] || {
    printf 'setup-carla-tests: Cabinet yabridge chainloader is missing\n' >&2
    exit 1
}

yabridge="$home/.local/share/yabridge"
if [ -e "$yabridge" ] && [ ! -L "$yabridge" ]; then
    printf 'setup-carla-tests: yabridge path is not a symbolic link: %s\n' "$yabridge" >&2
    exit 1
fi
rm -f "$yabridge"
ln -s "$cabinet_files/lib/yabridge" "$yabridge"

daw=${daw_ref%%/*}
mkdir -p "$home/.var/app/$daw/data" "$data/native" "$data/prefixes"
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

if [ -e "$carla_source" ] && [ ! -d "$carla_source/.git" ]; then
    printf 'setup-carla-tests: source path is not a Git checkout: %s\n' "$carla_source" >&2
    exit 1
fi

if [ ! -d "$carla_source/.git" ]; then
    mkdir -p "$(dirname "$carla_source")"
    git clone --recurse-submodules https://github.com/falkTX/Carla.git "$carla_source"
fi

if [ -n "$(git -C "$carla_source" status --porcelain --untracked-files=all)" ]; then
    printf 'setup-carla-tests: source checkout is dirty: %s\n' "$carla_source" >&2
    exit 1
fi

git -C "$carla_source" fetch --prune origin "$commit"
git -C "$carla_source" checkout --detach "$commit"
git -C "$carla_source" submodule update --init --recursive

marker="$carla_prefix/.cabinet-carla-commit"
if [ ! -x "$carla_prefix/bin/carla-single" ] || [ ! -f "$marker" ] || [ "$(<"$marker")" != "$commit" ]; then
    if [ -f "$marker" ]; then
        make -C "$carla_source" clean
    fi

    mkdir -p "$carla_prefix"
    make -C "$carla_source" HAVE_FRONTEND=false -j1
    make -C "$carla_source" HAVE_FRONTEND=false PREFIX="$carla_prefix" install
    printf '%s\n' "$commit" > "$marker"
fi

usage=$("$carla_prefix/bin/carla-single" 2>&1)
case "$usage" in
    *"  - clap"*) ;;
    *)
        printf 'setup-carla-tests: carla-single has no CLAP support\n' >&2
        exit 1
        ;;
esac

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

install_entry sitala-1
install_entry valhalla-supermassive
install_entry decent-sampler
install_entry surge-xt
install_entry ik-product-manager

surge_root=$data/prefixes/$surge_prefix
if [ ! -d "$surge_root/dosdevices" ]; then
    cabinet new "$surge_prefix"
fi

archive=$(mktemp "$root/tmp/cabinet-surge-windows.XXXXXX.zip")
trap 'rm -f "$archive"' EXIT
curl --fail --location --retry 3 --retry-all-errors --output "$archive" "$surge_url"
printf '%s  %s\n' "$surge_sha256" "$archive" |
    sha256sum --check --status || {
        printf 'setup-carla-tests: Surge XT Windows archive checksum does not match\n' >&2
        exit 1
    }

common="$surge_root/drive_c/Program Files/Common Files"
mkdir -p "$common/VST3" "$common/CLAP"
unzip -q -o "$archive" 'Surge XT.vst3/*' -d "$common/VST3"
unzip -q -o -j "$archive" 'Surge XT.clap' -d "$common/CLAP"

[ -f "$common/VST3/Surge XT.vst3/Contents/x86_64-win/Surge XT.vst3" ] || {
    printf 'setup-carla-tests: Surge XT Windows VST3 was not installed in the test prefix\n' >&2
    exit 1
}
[ -f "$common/CLAP/Surge XT.clap" ] || {
    printf 'setup-carla-tests: Surge XT Windows CLAP was not installed in the test prefix\n' >&2
    exit 1
}

cabinet sync

manager="$data/prefixes/ik-multimedia/drive_c/Program Files/IK Multimedia/IK Product Manager/IK Product Manager.exe"
[ -f "$manager" ] || {
    printf 'setup-carla-tests: IK Product Manager was not installed\n' >&2
    exit 1
}

printf 'backend=%s\n' "${CABINET_RUNTIME_BACKEND:-direct}" > "$root/config"
printf 'toolbox=%s\n' "${CABINET_RUNTIME_TOOLBOX:-}" >> "$root/config"
printf 'Carla fixtures are installed. No product was installed through IK Product Manager.\n'
EOF
