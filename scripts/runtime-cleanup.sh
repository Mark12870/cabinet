#!/usr/bin/env bash
set -euo pipefail

ROOT="${CABINET_RUNTIME_ROOT:-${XDG_CACHE_HOME:-$HOME/.cache}/cabinet-rt}"

for environ in /proc/[0-9]*/environ; do
    pid=${environ#/proc/}
    pid=${pid%/environ}
    [ "$pid" != "$$" ] && [ "$pid" != "$PPID" ] && [ -r "$environ" ] || continue
    command=$({ tr '\0' ' ' < "/proc/$pid/cmdline"; } 2>/dev/null | cut -c1-100)

    if { tr '\0' '\n' < "$environ"; } 2>/dev/null |
        grep -q -e "^CABINET_RUNTIME_ROOT=$ROOT\$" -e "^WINEPREFIX=$ROOT/" -e "^HOME=$ROOT/" ||
        grep -q 'Cabinet\.Runtime\.Tests' <<< "$command"; then
        kill "$pid" 2>/dev/null && printf 'ended %s %s\n' "$pid" "$command"
    fi
done
