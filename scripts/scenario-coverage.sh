#!/usr/bin/env bash
set -euo pipefail

root=$(realpath "$(dirname "$0")/..")
cd "$root"

declare -A set_aside=(
    [sitala-2]="opens an activation dialog over its editor until a licence key is entered"
    [novation-play]="opens an activation dialog over its editor until a licence key is entered"
)
covered=$(sed -n 's/.*const string Id = "\([^"]*\)";.*/\1/p' tests/Cabinet.Runtime.Tests/Scenarios/*Scenario.cs)
shipped=0
missing=()
aside=()

for entry in data/library/*/*.yml; do
    vendor=$(dirname "$entry")
    if compgen -G "$vendor/*.md" >/dev/null; then
        continue
    fi

    id=$(basename "$entry" .yml)
    if [ -n "${set_aside[$id]:-}" ]; then
        aside+=("$entry")
        continue
    fi

    shipped=$((shipped + 1))
    if ! grep -qxF "$id" <<<"$covered"; then
        missing+=("$entry")
    fi
done

if [ -z "${GITHUB_ACTIONS:-}" ] && [ ${#missing[@]} -gt 0 ]; then
    echo 'Not tested -- these entries have no plugin scenario:'
fi

for entry in "${missing[@]}"; do
    name=$(sed -n 's/^Name: *//p' "$entry")
    if [ -n "${GITHUB_ACTIONS:-}" ]; then
        printf '::warning file=%s,title=Not tested::%s is not tested: it has no plugin scenario yet\n' "$entry" "$name"
    else
        printf '%s  %s\n' "$(basename "$entry" .yml)" "$name"
    fi
done

if [ -z "${GITHUB_ACTIONS:-}" ] && [ ${#aside[@]} -gt 0 ]; then
    echo 'Not tested on purpose:'
    for entry in "${aside[@]}"; do
        printf '%s  %s: %s\n' "$(basename "$entry" .yml)" "$(sed -n 's/^Name: *//p' "$entry")" \
            "${set_aside[$(basename "$entry" .yml)]}"
    done
fi

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
    {
        echo '### Plugin scenario coverage'
        echo
        echo "$((shipped - ${#missing[@]})) of $shipped shipped entries are tested by an end-to-end runtime scenario."
        echo
        echo "**Not tested** -- these ${#missing[@]} entries have no scenario yet:"
        echo
        for entry in "${missing[@]}"; do
            echo "- \`$(basename "$entry" .yml)\` ($(sed -n 's/^Name: *//p' "$entry"))"
        done
        echo
        echo "**Not tested on purpose** -- these ${#aside[@]} entries cannot be tested end to end:"
        echo
        for entry in "${aside[@]}"; do
            echo "- \`$(basename "$entry" .yml)\` ($(sed -n 's/^Name: *//p' "$entry")): ${set_aside[$(basename "$entry" .yml)]}"
        done
    } >> "$GITHUB_STEP_SUMMARY"
fi
