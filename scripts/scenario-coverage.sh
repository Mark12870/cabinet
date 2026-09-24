#!/usr/bin/env bash
set -euo pipefail

root=$(realpath "$(dirname "$0")/..")
cd "$root"

covered=$(sed -n 's/.*const string Id = "\([^"]*\)";.*/\1/p' tests/Cabinet.Runtime.Tests/Scenarios/*Scenario.cs)
shipped=0
missing=()

for entry in data/library/*/*.yml; do
    vendor=$(dirname "$entry")
    if compgen -G "$vendor/*.md" >/dev/null; then
        continue
    fi

    shipped=$((shipped + 1))
    id=$(basename "$entry" .yml)
    if ! grep -qxF "$id" <<<"$covered"; then
        missing+=("$entry")
    fi
done

for entry in "${missing[@]}"; do
    name=$(sed -n 's/^Name: *//p' "$entry")
    if [ -n "${GITHUB_ACTIONS:-}" ]; then
        printf '::warning file=%s,title=No plugin scenario::%s has no end-to-end runtime scenario\n' "$entry" "$name"
    else
        printf '%s  %s\n' "$(basename "$entry" .yml)" "$name"
    fi
done

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
    {
        echo '### Plugin scenario coverage'
        echo
        echo "$((shipped - ${#missing[@]})) of $shipped shipped entries have an end-to-end runtime scenario."
        echo
        for entry in "${missing[@]}"; do
            echo "- \`$(basename "$entry" .yml)\` ($(sed -n 's/^Name: *//p' "$entry"))"
        done
    } >> "$GITHUB_STEP_SUMMARY"
fi
