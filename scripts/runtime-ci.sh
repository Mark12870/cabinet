#!/usr/bin/env bash
# Runs TESTS.md's runtime suite inside a container, for the runtime job in
# .github/workflows/ci.yml. The workflow only starts the container and carries artifacts out;
# every step below runs inside it.
#
# The fixtures are cached in two halves, because they cannot be made per run and they cannot all
# be published. What is free software -- the tools, Carla, the runners a catalogue entry pins, the
# Flatpak runtimes -- lives in a public image on ghcr.io. The catalogue's Freeware and
# Commercial entries, and the DAW, are vendors' binaries: Cabinet may install them on a user's
# machine, but nothing here may republish them, so they ride in the repository's own Actions
# cache, which only this repository's workflows can read, and `clean` takes them out of the
# container before the image is committed.
#
#   scripts/runtime-ci.sh prepare        the tools and the unprivileged user, as root
#   scripts/runtime-ci.sh sync           a writable copy of the read-only checkout
#   scripts/runtime-ci.sh restore tar    put the cached private fixtures back
#   scripts/runtime-ci.sh setup          scripts/setup-runtime-tests.sh on its own display
#   scripts/runtime-ci.sh test           the whole Cabinet.Runtime.Tests matrix
#   scripts/runtime-ci.sh collect dir    the captures, logs and results a failure needs
#   scripts/runtime-ci.sh save tar       pack the private fixtures for the cache
#   scripts/runtime-ci.sh clean          leave only what the image may carry
#
# CABINET_RUNTIME_PROBES=0 leaves the drag-and-drop probes out, fixtures and cases both: they say
# when a yabridge patch can go, which is a question for a refresh rather than for every push.
#
# Wine refuses to run as root and the suite keys its sockets on XDG_RUNTIME_DIR, which has to
# stay short and under /run/user/<uid>, so everything past `prepare` re-execs as that user with
# runuser, which is why the container needs util-linux beyond what the suite itself uses.
set -euo pipefail

APP=io.github.mark12870.cabinet
DAW=fm.reaper.Reaper
OWNER=cabinet
OWNER_ID=1000
ROOT=${CABINET_RUNTIME_ROOT:-/var/cabinet-rt}
SOURCE=/src
WORK=/home/$OWNER/cabinet

die() {
    printf 'runtime-ci: %s\n' "$*" >&2
    exit 1
}

step() { printf '  %s\n' "$*" >&2; }

# Every fixture the catalogue does not licence for redistribution. WorkflowTests keeps this in
# step with the entries scripts/setup-runtime-tests.sh installs; Surge XT is GPL-3.0 and stays
# in the image.
private_paths() {
    local data="home/.var/app/$APP/data"

    printf '%s\n' \
        "$data/prefixes/valhalla" \
        "$data/prefixes/sitala-1" \
        "$data/prefixes/fabfilter" \
        "$data/prefixes/ik-multimedia" \
        "$data/prefixes/sine-player" \
        "$data/native/decent-sampler"
}

as_owner() {
    [ "$(id -u)" = 0 ] || return 0

    exec runuser --user "$OWNER" -- env \
        HOME="/home/$OWNER" \
        XDG_RUNTIME_DIR="/run/user/$OWNER_ID" \
        CABINET_RUNTIME_ROOT="$ROOT" \
        CABINET_RUNTIME_HOST_FLATPAK_REPO="${CABINET_RUNTIME_HOST_FLATPAK_REPO:-$WORK/repo}" \
        CABINET_RUNTIME_CABINET_REF="${CABINET_RUNTIME_CABINET_REF:-$APP/x86_64/stable}" \
        CABINET_RUNTIME_PROBES="${CABINET_RUNTIME_PROBES:-1}" \
        bash "$0" "$@"
}

session_bus() {
    [ -n "${DBUS_SESSION_BUS_ADDRESS:-}" ] && return 0

    DBUS_SESSION_BUS_ADDRESS=$(dbus-daemon --session --fork --print-address)
    export DBUS_SESSION_BUS_ADDRESS
}

# A vendor installer needs a display, and it must be weston rather than Xvfb: a display with no
# refresh rate has no vblank and a plugin presenting through DXGI dies in Wine's unwinder on one.
start_display() {
    local log="$ROOT/tmp/weston-ci.log" waited=0

    mkdir -p "$ROOT/tmp"
    rm -f "$log"
    unset DISPLAY

    weston --backend=headless --width=1920 --height=1080 --refresh-rate=60000 --xwayland \
        --socket="cabinet-ci-$$" --log="$log" >/dev/null 2>&1 &
    compositor=$!

    while [ -z "${DISPLAY:-}" ] && [ "$waited" -lt 240 ]; do
        if [ -f "$log" ]; then
            DISPLAY=$(sed -n 's/.*xserver listening on display \(:[0-9]*\).*/\1/p' "$log" | sed -n 1p)
        fi
        [ -n "${DISPLAY:-}" ] || sleep 0.5
        waited=$((waited + 1))
    done

    [ -n "${DISPLAY:-}" ] || { cat "$log" >&2; die 'weston announced no X display'; }
    export DISPLAY

    xprop -root -spy >/dev/null 2>&1 &
    anchor=$!
}

# TERM, never KILL: a killed weston cannot unlink its own socket, and the next display then
# waits a minute for it to go.
end_display() {
    kill -TERM "${anchor:-0}" 2>/dev/null || true
    kill -TERM "${compositor:-0}" 2>/dev/null || true
    wait "${compositor:-0}" 2>/dev/null || true
}

prepare() {
    mapfile -t packages < "$SOURCE/scripts/runtime-packages.txt"

    step 'dnf install'
    dnf install -y "${packages[@]}" dbus-daemon util-linux

    id -u "$OWNER" >/dev/null 2>&1 || useradd --uid "$OWNER_ID" --create-home "$OWNER"
    install -d -o "$OWNER" -g "$OWNER" -m 700 "/run/user/$OWNER_ID" "$ROOT"
    install -d -o "$OWNER" -g "$OWNER" -m 755 "$WORK"
    install -d -m 1777 /tmp/.X11-unix
    install -d -m 755 /var/lib/flatpak
}

sync_source() {
    as_owner sync

    step 'copy the checkout'
    rm -rf "$WORK"
    mkdir -p "$WORK"
    cp -r "$SOURCE/." "$WORK/"
}

restore() {
    as_owner restore "$@"

    local archive=$1

    if [ ! -f "$archive" ]; then
        step 'no cached fixtures; the setup will install them'
        return 0
    fi

    step 'unpack the cached fixtures'
    tar --extract --file "$archive" --directory "$ROOT"
}

save() {
    as_owner save "$@"

    local archive=$1
    local -a present=()
    local path

    while IFS= read -r path; do
        if [ -e "$ROOT/$path" ]; then
            present+=("$path")
        fi
    done < <(private_paths)

    [ ${#present[@]} -gt 0 ] || die 'the private fixtures are missing; nothing to cache'

    step 'pack the private fixtures'
    tar --create --file "$archive" --directory "$ROOT" "${present[@]}"
    du -sh "$archive" >&2
}

run_setup() {
    as_owner setup

    session_bus
    start_display
    trap end_display EXIT

    step 'setup-runtime-tests.sh'
    cd "$WORK"
    CABINET_RUNTIME_BACKEND=direct scripts/setup-runtime-tests.sh
}

run_test() {
    as_owner test

    session_bus

    local -a scope=()

    if [ "${CABINET_RUNTIME_PROBES:-1}" != 1 ]; then
        scope=(--filter 'FullyQualifiedName!~DragAndDropTests')
    fi

    step 'dotnet test'
    cd "$WORK"
    dotnet test tests/Cabinet.Runtime.Tests --nologo \
        -m:1 -p:BuildInParallel=false -p:RestoreDisableParallel=true \
        --logger 'trx;LogFileName=runtime.trx' \
        --logger 'console;verbosity=normal' "${scope[@]}"
}

collect() {
    as_owner collect "$@"

    local destination=$1
    rm -rf "$destination"
    mkdir -p "$destination"

    if [ -d "$ROOT/tmp/interaction" ]; then
        cp -r "$ROOT/tmp/interaction" "$destination/interaction"
        find "$destination/interaction" -name '*.xwd' -delete
    fi

    for kept in \
        "$ROOT/q" \
        "$ROOT/home/.var/app/$APP/data/logs" \
        "$WORK/tests/Cabinet.Runtime.Tests/TestResults"; do
        [ -e "$kept" ] && cp -r "$kept" "$destination/$(basename "$kept")"
    done

    find "$ROOT/tmp" -maxdepth 1 -name '*.log' -exec cp {} "$destination/" \;
    du -sh "$destination" >&2
}

# The image is committed from the container it ran in, so everything a run leaves behind would
# land in a published layer: its working copy and captures, and every vendor binary it installed.
clean() {
    as_owner clean

    local data="$ROOT/home/.var/app/$APP/data"
    local path marker runner name
    local -a wanted=()

    step 'drop the run'
    rm -rf "$WORK" "$ROOT/tmp" "$ROOT/q" "$data/bridge"

    step 'drop the probe fixtures'
    rm -rf "$data"/prefixes/drag-drop-*

    while IFS= read -r marker; do
        wanted+=("$(<"$marker")")
    done < <(find "$data/prefixes" -maxdepth 2 -name .cabinet-runner 2>/dev/null)

    for runner in "$data"/runners/*/; do
        [ -d "$runner" ] || continue
        name=$(basename "$runner")
        printf '%s\n' "${wanted[@]+"${wanted[@]}"}" | grep -qxF "$name" || rm -rf "$runner"
    done

    step 'take out what may not be republished'
    while IFS= read -r path; do
        rm -rf "${ROOT:?}/$path"
    done < <(private_paths)

    HOME="$ROOT/home" \
        FLATPAK_USER_DIR="$ROOT/home/.local/share/flatpak" \
        flatpak uninstall --user --noninteractive "$DAW" >/dev/null 2>&1 || true
}

case ${1:-} in
    prepare) prepare ;;
    sync) sync_source ;;
    restore) [ $# -eq 2 ] || die 'restore needs the archive to unpack'; restore "$2" ;;
    setup) run_setup ;;
    test) run_test ;;
    collect) [ $# -eq 2 ] || die 'collect needs a destination directory'; collect "$2" ;;
    save) [ $# -eq 2 ] || die 'save needs the archive to write'; save "$2" ;;
    clean) clean ;;
    *) die "usage: $(basename "$0") prepare|sync|restore|setup|test|collect|save|clean" ;;
esac
