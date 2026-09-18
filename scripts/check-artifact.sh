#!/usr/bin/env bash
# Checks what flatpak-builder produced, so a release cannot ship an artifact the source checks
# never saw: both front ends as published (NativeAOT, trimmed, on the pinned runtime), the shim,
# yabridge with its loader fallback, the bundled catalogue, and the desktop metadata.
#
#   scripts/check-artifact.sh [build-dir]
set -euo pipefail

root=$(realpath "$(dirname "$0")/..")
app=io.github.mark12870.cabinet
build=$(realpath "${1:-$root/build}")
files=$build/files

fail() { printf 'check-artifact: %s\n' "$*" >&2; exit 1; }

executable() { [ -x "$files/$1" ] || fail "$1 is missing or not executable"; }

same() { cmp -s "$root/$1" "$files/$2" || fail "$2 differs from $1"; }

for program in \
  bin/cabinet lib/cabinet-gui/cabinet-gui \
  lib/yabridge/cabinet-wine lib/yabridge/yabridgectl \
  lib/yabridge/yabridge-host.exe lib/yabridge/yabridge-host.exe.so \
  lib/yabridge/yabridge-host-32.exe lib/yabridge/yabridge-host-32.exe.so; do
  executable "$program"
done

for format in clap vst2 vst3; do
  executable "lib/yabridge/libyabridge-$format.so"
  executable "lib/yabridge/libyabridge-chainloader-$format.so"
done

[ "$(readlink "$files/bin/cabinet-gui")" = ../lib/cabinet-gui/cabinet-gui ] ||
  fail "bin/cabinet-gui does not link to the published GUI"

[ -z "$(find "$files/bin" -name '*.dll')" ] || fail "bin/ carries managed assemblies; cabinet is not NativeAOT"

pinned=$(sed -n 's:.*<RuntimeFrameworkVersion>\(.*\)</RuntimeFrameworkVersion>.*:\1:p' \
  "$root/src/Directory.Build.props")
grep -q "\"runtimepack.Microsoft.NETCore.App.Runtime.linux-x64/$pinned\"" \
  "$files/lib/cabinet-gui/cabinet-gui.deps.json" ||
  fail "the GUI is not published on the pinned runtime $pinned"
grep -q '"System.StartupHookProvider.IsSupported": false' \
  "$files/lib/cabinet-gui/cabinet-gui.runtimeconfig.json" ||
  fail "the GUI is not trimmed"

for host in yabridge-host.exe yabridge-host-32.exe; do
  grep -q 'WINELOADER="$appdir/cabinet-wine"' "$files/lib/yabridge/$host" ||
    fail "$host still falls back to a bare wine"
done

for vendor in "$root"/data/library/*/; do
  name=$(basename "$vendor")
  shipped=$files/share/cabinet/library/$name
  if compgen -G "$vendor*.md" >/dev/null; then
    [ ! -e "$shipped" ] || fail "held-back vendor $name was shipped"
    continue
  fi
  for entry in "$vendor"*.yml; do
    same "data/library/$name/$(basename "$entry")" "share/cabinet/library/$name/$(basename "$entry")"
  done
done

same "$app.metainfo.xml" "share/metainfo/$app.metainfo.xml"
same "$app.desktop" "share/applications/$app.desktop"
same "data/$app.Links.desktop" "share/applications/$app.Links.desktop"
same "data/$app.svg" "share/icons/hicolor/scalable/apps/$app.svg"
[ -f "$files/share/glib-2.0/schemas/gschemas.compiled" ] || fail "the GSettings schema is not compiled"

run() { flatpak build "$build" "$@"; }

[[ $(run cabinet --help) == *$'\nUsage:\n'* ]] || fail "cabinet --help does not run"
[[ $(run /app/lib/yabridge/cabinet-wine --cabinet-self-test) == *' ok' ]] || fail "the shim does not run"

echo "check-artifact: $build is complete"
