#!/usr/bin/env bash
set -euo pipefail

APP=io.github.mark12870.cabinet
BOX=${CABINET_CARLA_TOOLBOX:-carla}
IMAGE=${CABINET_CARLA_TOOLBOX_IMAGE:-registry.fedoraproject.org/fedora-toolbox:44}
DATA_ROOT=${CABINET_CARLA_DATA:-$HOME/.var/app/$APP/data/carla-tests}
SOURCE=${CABINET_CARLA_SOURCE:-$DATA_ROOT/source}
PREFIX=${CABINET_CARLA_PREFIX:-$DATA_ROOT/prefix}
COMMIT=97a9e0740baf6df2df942495c02532a624c44682
SURGE_PREFIX=carla-surge-windows
SURGE_URL=https://github.com/surge-synthesizer/releases-xt/releases/download/1.3.4/surge-xt-win64-1.3.4-pluginsonly.zip
SURGE_SHA256=564e162c560af07ad4ed47fe1bfcd827cf97a575de30d06c48249aad2e7c35e6

die() {
    printf 'setup-carla-tests: %s\n' "$*" >&2
    exit 1
}

command -v flatpak >/dev/null || die "flatpak is not installed"
command -v curl >/dev/null || die "curl is not installed"
command -v sha256sum >/dev/null || die "sha256sum is not installed"
command -v toolbox >/dev/null || die "toolbox is not installed"
command -v podman >/dev/null || die "podman is not installed"
command -v unzip >/dev/null || die "unzip is not installed"
flatpak info --user "$APP" >/dev/null 2>&1 || die "$APP is not installed for the current user"

if ! podman container exists "$BOX" 2>/dev/null; then
    toolbox create "$BOX" --image "$IMAGE"
fi

toolbox run --container "$BOX" sudo dnf install -y \
    alsa-lib-devel \
    file-devel \
    gcc-c++ \
    git \
    libX11-devel \
    libXcursor-devel \
    libXext-devel \
    libXrandr-devel \
    liblo-devel \
    libsamplerate-devel \
    libsndfile-devel \
    mesa-dri-drivers \
    make \
    pkgconf-pkg-config \
    pulseaudio-libs-devel \
    xorg-x11-server-Xvfb \
    xorg-x11-xauth \
    openbox

toolbox run --container "$BOX" bash -s -- "$SOURCE" "$PREFIX" "$COMMIT" <<'EOF'
set -euo pipefail

source_dir=$1
prefix=$2
commit=$3

if [ -e "$source_dir" ] && [ ! -d "$source_dir/.git" ]; then
    printf 'setup-carla-tests: source path is not a Git checkout: %s\n' "$source_dir" >&2
    exit 1
fi

if [ ! -d "$source_dir/.git" ]; then
    mkdir -p "$(dirname "$source_dir")"
    git clone --recurse-submodules https://github.com/falkTX/Carla.git "$source_dir"
fi

if [ -n "$(git -C "$source_dir" status --porcelain --untracked-files=all)" ]; then
    printf 'setup-carla-tests: source checkout is dirty: %s\n' "$source_dir" >&2
    exit 1
fi

git -C "$source_dir" fetch --prune origin "$commit"
git -C "$source_dir" checkout --detach "$commit"
git -C "$source_dir" submodule update --init --recursive

marker="$prefix/.cabinet-carla-commit"
if [ ! -x "$prefix/bin/carla-single" ] || [ ! -f "$marker" ] || [ "$(<"$marker")" != "$commit" ]; then
    if [ -f "$marker" ]; then
        make -C "$source_dir" clean
    fi

    make -C "$source_dir" HAVE_FRONTEND=false -j"$(nproc)"
    make -C "$source_dir" HAVE_FRONTEND=false PREFIX="$prefix" install
    mkdir -p "$prefix"
    printf '%s\n' "$commit" > "$marker"
fi

usage=$("$prefix/bin/carla-single" 2>&1)
case "$usage" in
    *"  - clap"*) ;;
    *)
        printf 'setup-carla-tests: carla-single has no CLAP support\n' >&2
        exit 1
        ;;
esac

printf 'Carla installed in %s\n' "$prefix"
EOF

installed() {
    local id=$1
    local line
    local mark
    local candidate
    local listing

    listing=$(flatpak run "$APP" library)
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

    flatpak run "$APP" library install "$id" "$@"
}

install_entry sitala-1
install_entry valhalla-supermassive
install_entry decent-sampler
install_entry surge-xt
install_entry ik-product-manager

DATA=$HOME/.var/app/$APP/data
SURGE_ROOT=$DATA/prefixes/$SURGE_PREFIX

if [ ! -d "$SURGE_ROOT/dosdevices" ]; then
    flatpak run "$APP" new "$SURGE_PREFIX"
fi

archive=$(mktemp /tmp/cabinet-surge-windows.XXXXXX.zip)
trap 'rm -f "$archive"' EXIT
curl --fail --location --retry 3 --retry-all-errors --output "$archive" "$SURGE_URL"
printf '%s  %s\n' "$SURGE_SHA256" "$archive" | sha256sum --check --status || die "Surge XT Windows archive checksum does not match"

common="$SURGE_ROOT/drive_c/Program Files/Common Files"
mkdir -p "$common/VST3" "$common/CLAP"
unzip -q -o "$archive" 'Surge XT.vst3/*' -d "$common/VST3"
unzip -q -o -j "$archive" 'Surge XT.clap' -d "$common/CLAP"

[ -f "$common/VST3/Surge XT.vst3/Contents/x86_64-win/Surge XT.vst3" ] \
    || die "Surge XT Windows VST3 was not installed in the test prefix"
[ -f "$common/CLAP/Surge XT.clap" ] \
    || die "Surge XT Windows CLAP was not installed in the test prefix"

flatpak run "$APP" sync

manager="$DATA/prefixes/ik-multimedia/drive_c/Program Files/IK Multimedia/IK Product Manager/IK Product Manager.exe"
[ -f "$manager" ] || die "IK Product Manager was not installed"

printf 'Carla fixtures are installed. No product was installed through IK Product Manager.\n'
