#!/usr/bin/env bash
# Runs docs/TESTS.md's runtime suite inside a container, for the runtime job in
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
#   scripts/runtime-ci.sh key            the fixture cache key's hash, on the runner itself
#   scripts/runtime-ci.sh prepare        the tools and the unprivileged user, as root
#   scripts/runtime-ci.sh sync           a writable copy of the checkout, without the repo
#   scripts/runtime-ci.sh restore tar    put the cached private fixtures back
#   scripts/runtime-ci.sh setup          scripts/setup-runtime-tests.sh on its own display
#   scripts/runtime-ci.sh test           the whole Cabinet.Runtime.Tests matrix
#   scripts/runtime-ci.sh collect dir    the captures, logs and results a failure needs
#   scripts/runtime-ci.sh save tar       pack the private fixtures for the cache
#   scripts/runtime-ci.sh clean          leave only what the image may carry
#
# CABINET_RUNTIME_PROBES=0 leaves the drag-and-drop probes out, fixtures and cases both: they say
# when a yabridge patch can go, which is a question for a refresh rather than for every push.
# CABINET_RUNTIME_SUITE=general, the default, is everything but the plugin scenarios; a push runs
# it. CABINET_RUNTIME_SUITE=scenarios is only the plugin scenarios, which run daily on a schedule;
# each installs its own entry, three at a time, so its setup leaves out the DAW and the entries,
# and it has no use for the private fixtures.
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

# A native plugin's bundles live in the DAW scan paths, and data/native/<id> holds only links to
# them, so its fixture is both.
private_fixtures() {
    local path link target

    while IFS= read -r path; do
        printf '%s\n' "$path"
        [[ $path == */native/* && -d $ROOT/$path ]] || continue

        while IFS= read -r link; do
            target=$(readlink "$link")
            case $target in
                "$ROOT"/home/.*/?*) realpath --no-symlinks --relative-to="$ROOT" "$target" ;;
                *) die "$link points outside the scan paths, at $target" ;;
            esac
        done < <(find "$ROOT/$path" -type l -lname '/*')
    done < <(private_paths)
}

as_owner() {
    [ "$(id -u)" = 0 ] || return 0

    exec runuser --user "$OWNER" -- env \
        HOME="/home/$OWNER" \
        XDG_RUNTIME_DIR="/run/user/$OWNER_ID" \
        LANG=C.UTF-8 \
        CABINET_RUNTIME_ROOT="$ROOT" \
        CABINET_RUNTIME_HOST_FLATPAK_REPO="${CABINET_RUNTIME_HOST_FLATPAK_REPO:-$SOURCE/repo}" \
        CABINET_RUNTIME_CABINET_REF="${CABINET_RUNTIME_CABINET_REF:-$APP/x86_64/stable}" \
        CABINET_RUNTIME_PROBES="${CABINET_RUNTIME_PROBES:-1}" \
        CABINET_RUNTIME_SUITE="${CABINET_RUNTIME_SUITE:-general}" \
        CABINET_RUNTIME_FILTER_CLASS="${CABINET_RUNTIME_FILTER_CLASS:-}" \
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
    local hostbins tool
    mapfile -t packages < "$SOURCE/scripts/runtime-packages.txt"

    if ! rpm -q "${packages[@]}" dbus-daemon util-linux >/dev/null; then
        step 'dnf install'
        dnf install -y "${packages[@]}" dbus-daemon util-linux
    fi

    # Carla builds its frontend only when all of its own tests pass, installs a no-gui build
    # without a word when one does not, and the suite then fails twenty minutes later for want of
    # bin/carla. These are those tests, asked the way Carla asks them -- including the `which` it
    # resolves its tools with, which a minimal base does not ship and a toolbox image does.
    command -v which >/dev/null || die 'Carla resolves its tools with which, and it is missing'
    which pyuic5 >/dev/null 2>&1 || die 'Carla needs pyuic5; python3-qt5-base is missing'
    pkg-config --exists Qt5Core Qt5Gui Qt5Widgets ||
        die "Carla's frontend needs the Qt5 development files; qt5-qtbase-devel is missing"

    hostbins=$(pkg-config --variable=host_bins Qt5Core)
    for tool in moc rcc uic; do
        [ -x "$hostbins/$tool" ] ||
            die "Carla's frontend needs $tool; qt5-qtbase-devel is missing"
    done

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
    # Without the repo: it is half a gigabyte of OSTree objects that only flatpak reads, and it is
    # mounted at $SOURCE already.
    tar --create --directory "$SOURCE" --exclude ./repo . | tar --extract --directory "$WORK"
}

restore() {
    as_owner restore "$@"

    local archive=$1

    if [ ! -f "$archive" ]; then
        step 'no cached fixtures; the setup will install them'
        return 0
    fi

    step 'unpack the cached fixtures'
    tar --list --file "$archive" | sed -e 's|/$||' -e "s|^|${ROOT:?}/|" | xargs -d '\n' rm -rf --
    tar --extract --file "$archive" --directory "$ROOT"
}

save() {
    as_owner save "$@"

    local archive=$1 fixtures
    local -a present=()
    local path

    fixtures=$(private_fixtures) || die 'cannot tell which fixtures are private'
    while IFS= read -r path; do
        if [ -e "$ROOT/$path" ]; then
            present+=("$path")
        fi
    done <<< "$fixtures"

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

    local entries=1
    if [ "${CABINET_RUNTIME_SUITE:-general}" = scenarios ]; then
        entries=0
    fi

    step 'setup-runtime-tests.sh'
    cd "$WORK"
    CABINET_RUNTIME_BACKEND=direct CABINET_RUNTIME_ENTRIES="$entries" scripts/setup-runtime-tests.sh
}

credentials() {
    [ "$(id -u)" = 0 ] && [ -n "${EMAIL:-}" ] || return 0

    install -o "$OWNER" -g "$OWNER" -m 600 /dev/null "$WORK/credentials.env"
    printf 'EMAIL=%s\nPASSWORD=%s\nDROPBOX=%s\nUSERNAME=%s\nNATIVE_ACCESS=%s\n' \
        "$EMAIL" "$PASSWORD" "${DROPBOX:-}" "${USERNAME:-}" "${NATIVE_ACCESS:-}" > "$WORK/credentials.env"
}

run_test() {
    credentials
    as_owner test

    session_bus

    local filter=() parallel=()

    case "${CABINET_RUNTIME_SUITE:-general}" in
        general) filter=(--filter-not-namespace Cabinet.Runtime.Tests.Scenarios) ;;
        scenarios) filter=(--filter-namespace Cabinet.Runtime.Tests.Scenarios)
            parallel=(--parallel collections)
            if [ -n "${CABINET_RUNTIME_FILTER_CLASS:-}" ]; then
                filter+=(--filter-class "$CABINET_RUNTIME_FILTER_CLASS")
                parallel=(--parallel none)
            fi ;;
        *) die "no runtime suite named ${CABINET_RUNTIME_SUITE}" ;;
    esac

    if [ "${CABINET_RUNTIME_PROBES:-1}" != 1 ]; then
        filter+=(--filter-not-class '*DragAndDropTests')
    fi

    step 'dotnet build'
    cd "$WORK"
    dotnet build tests/Cabinet.Runtime.Tests --nologo \
        -m:1 -p:BuildInParallel=false -p:RestoreDisableParallel=true

    # A vendor's server that stalls once fails a test with nothing wrong in it, so a failed test
    # runs once more in a fresh process, fixture and all. Past a fifth of the suite failing, the
    # cause is Cabinet's and nothing is rerun. The top runtime.trx is the last attempt alone;
    # each attempt's own is under Retries/, which scenario-report.py reads.
    step 'dotnet test'
    dotnet test --project tests/Cabinet.Runtime.Tests --no-build --no-progress \
        --results-directory "$WORK/tests/Cabinet.Runtime.Tests/TestResults" \
        --report-trx --report-trx-filename runtime.trx --output detailed \
        --retry-failed-tests 1 --retry-failed-tests-max-percentage 20 \
        "${filter[@]}" "${parallel[@]}"
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

    if [ -d "$ROOT/tmp/scenarios" ]; then
        mkdir -p "$destination/scenarios"
        for scenario in "$ROOT/tmp/scenarios"/*; do
            [ -d "$scenario/artefacts" ] || continue
            name=$(basename "$scenario")
            mkdir -p "$destination/scenarios/$name"
            cp -r "$scenario/artefacts" "$destination/scenarios/$name/artefacts"
        done
        find "$destination/scenarios" -name '*.xwd' -delete
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
    local -a wanted=() fixtures=()

    step 'drop the run'
    rm -rf "$WORK" "$ROOT/tmp" "$ROOT/q" "$data/bridge" "$ROOT"/home/.{vst3,vst,clap}/cabinet/windows

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
    fixtures=$(private_fixtures) || die 'cannot tell which fixtures are private'
    mapfile -t fixtures <<< "$fixtures"
    for path in "${fixtures[@]}"; do
        rm -rf "${ROOT:?}/$path"
    done

    HOME="$ROOT/home" \
        FLATPAK_USER_DIR="$ROOT/home/.local/share/flatpak" \
        flatpak uninstall --user --noninteractive "$DAW" >/dev/null 2>&1 || true
}

# Only the vendors whose entries the setup installs: an entry only a plugin scenario uses is
# installed by that scenario, and changing it rebuilds nothing.
fixture_key() {
    cd "$(dirname "$0")/.."

    local ids vendors id entry
    ids=$(sed -n 's/^ *install_entry \([a-z0-9-]*\)$/\1/p' scripts/setup-runtime-tests.sh)
    [ -n "$ids" ] || die 'the setup installs no entries'

    vendors=$(for id in $ids; do
        entry=$(find data/library -name "$id.yml")
        [ -n "$entry" ] || die "no catalogue entry for $id"
        dirname "$entry"
    done | sort -u)

    { find $vendors scripts/setup-runtime-tests.sh -type f -print0 | sort -z | xargs -0 sha256sum
      declare -f private_paths private_fixtures; } | sha256sum | cut -d' ' -f1
}

case ${1:-} in
    key) fixture_key ;;
    prepare) prepare ;;
    sync) sync_source ;;
    restore) [ $# -eq 2 ] || die 'restore needs the archive to unpack'; restore "$2" ;;
    setup) run_setup ;;
    test) run_test ;;
    collect) [ $# -eq 2 ] || die 'collect needs a destination directory'; collect "$2" ;;
    save) [ $# -eq 2 ] || die 'save needs the archive to write'; save "$2" ;;
    clean) clean ;;
    *) die "usage: $(basename "$0") key|prepare|sync|restore|setup|test|collect|save|clean" ;;
esac
