#!/usr/bin/env bash
# Re-commits from Flathub the extensions the manifest declares for automatic download, so a client
# that installs Cabinet from its own remote finds them there. Prints each ref it changed.
#
#   scripts/mirror-extensions.sh repo
set -euo pipefail

refs=(
  runtime/org.freedesktop.Platform.Compat.i386/x86_64/25.08
  runtime/org.winehq.Wine.gecko/x86_64/stable-25.08
  runtime/org.winehq.Wine.mono/x86_64/stable-25.08
)

repo=$(realpath "$1")
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

export FLATPAK_USER_DIR=$work/installation
flatpak remote-add --user flathub https://dl.flathub.org/repo/flathub.flatpakrepo

attempt=1
until flatpak install --user --noninteractive --no-deploy --no-deps --no-related \
  flathub "${refs[@]}" >&2; do
  [ "$attempt" -lt 3 ] || exit 1
  sleep $((attempt * 30))
  attempt=$((attempt + 1))
done

for ref in "${refs[@]}"; do
  before=$(ostree --repo="$repo" rev-parse "$ref" 2>/dev/null || true)
  flatpak build-commit-from --no-update-summary \
    --src-repo="$FLATPAK_USER_DIR/repo" --src-ref="flathub:$ref" "$repo" "$ref" >&2
  ostree --repo="$repo" prune --refs-only --depth=0 --only-branch="$ref" >&2
  [ "$(ostree --repo="$repo" rev-parse "$ref")" = "$before" ] || echo "$ref"
done
