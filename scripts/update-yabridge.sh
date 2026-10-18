#!/usr/bin/env bash
# Points the manifest at a pinned yabridge commit. GitHub publishes no checksum for a commit
# archive, so this downloads it and hashes it. The metainfo is left alone: which release a new
# yabridge makes is a judgement, not a script's.
#
#   scripts/update-yabridge.sh COMMIT
#
# Exits 0 whether or not anything changed; `updated=` on stdout (and in $GITHUB_OUTPUT) says which.
set -euo pipefail

manifest=$(realpath "$(dirname "$0")/..")/flatpak/io.github.mark12870.cabinet.yml
archive=https://github.com/robbert-vdh/yabridge/archive

commit=${1:-}
[[ $# -eq 1 && $commit =~ ^[0-9a-f]{40}$ ]] || { echo "usage: $(basename "$0") COMMIT" >&2; exit 1; }

emit() {
  printf 'updated=%s\nyabridge=%s\n' "$1" "$commit"
  if [ -n "${GITHUB_OUTPUT:-}" ]; then
    printf 'updated=%s\nyabridge=%s\n' "$1" "$commit" >> "$GITHUB_OUTPUT"
  fi
}

line=$(grep -nE "^ *url: ${archive}/[0-9a-f]{40}\.tar\.gz$" "$manifest" | head -1) ||
  { echo "cannot find the yabridge url in the manifest" >&2; exit 1; }
number=${line%%:*}
current=$(sed -E 's#.*/([0-9a-f]{40})\.tar\.gz$#\1#' <<< "$line")

if [ "$current" = "$commit" ]; then
  emit false
  exit 0
fi

sed -n "$((number + 1))p" "$manifest" | grep -qE '^ *sha256: [0-9a-f]{64}$' ||
  { echo "the yabridge url is not followed by its sha256" >&2; exit 1; }

echo "yabridge $current -> $commit" >&2
sha256=$(curl -fsSL --max-time 120 "$archive/$commit.tar.gz" | sha256sum | cut -d' ' -f1)

sed -i -E "${number}s#[0-9a-f]{40}\.tar\.gz#${commit}.tar.gz#; $((number + 1))s#[0-9a-f]{64}#${sha256}#" "$manifest"

echo "now add a <release> to the metainfo; a new yabridge is a Cabinet release" >&2
emit true
