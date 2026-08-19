#!/usr/bin/env bash
# Everything .github/workflows/checks.yml runs, so the hook and the human run one list.
#
#   scripts/checks.sh            verify all of it, tests included
#   scripts/checks.sh --staged   what .githooks/pre-commit runs: reformat the staged code
#                                and stage the fix, skip what nothing is staged for, and
#                                skip dotnet test
#
# Neither toolchain is on a Silverblue host, so this re-execs itself inside
# org.gnome.Sdk//50, which carries the same org.freedesktop.Sdk.Extension branch (25.08).
# It re-execs unconditionally: a dotnet elsewhere on PATH is a different version from the
# one CI uses, and a hook that disagrees with CI is worse than no hook.
# --no-restore is not a micro-optimisation: the implicit restore turns a 6 second format
# into a 19 second one, and a hook that slow is one nobody keeps.
# dotnet's two background servers have to be off, or the run hangs rather than ends: both
# outlive the command that started them, both keep the sandbox they were started in open,
# and a second run reaches the first one's compiler over a /tmp socket it cannot serve.
set -euo pipefail

root=$(git rev-parse --show-toplevel)

if [ -z "${CABINET_CHECKS_IN_SDK:-}" ]; then
  exec flatpak run --share=network --filesystem="$root" --command=sh org.gnome.Sdk//50 -c '
    . /usr/lib/sdk/dotnet10/enable.sh
    export PATH=/usr/lib/sdk/rust-stable/bin:/usr/lib/sdk/llvm20/bin:$PATH
    export MSBUILDDISABLENODEREUSE=1 UseSharedCompilation=false
    CABINET_CHECKS_IN_SDK=1 exec "$0" "$@"' "$(realpath "$0")" "$@"
fi

cd "$root"

step() { printf '  %s\n' "$*" >&2; }

if [ "${1:-}" != --staged ]; then
  step 'dotnet format';   dotnet format --verify-no-changes
  step 'dotnet test';     dotnet test tests/Cabinet.Core.Tests --nologo
  step 'appstreamcli';    appstreamcli validate --no-net io.github.mark12870.cabinet.metainfo.xml
  step 'cargo fmt';       (cd shim && cargo fmt --check)
  step 'cargo clippy';    (cd shim && cargo clippy --all-targets -- -D warnings)
  step 'cargo test';      (cd shim && cargo test)
  exit 0
fi

mapfile -t staged < <(git diff --cached --name-only --diff-filter=ACMR)

declare -A is_staged=() is_dirty=()
for f in "${staged[@]}"; do is_staged[$f]=1; done
while read -r f; do [ -n "$f" ] && is_dirty[$f]=1; done < <(git diff --name-only)

cs=(); rust=no; metainfo=no
for f in "${staged[@]}"; do
  case $f in
    *.cs) cs+=("$f") ;;
    shim/*) rust=yes ;;
    io.github.mark12870.cabinet.metainfo.xml) metainfo=yes ;;
  esac
done

watched=("${cs[@]}")
if [ "$rust" = yes ]; then watched+=(shim/src/main.rs); fi

declare -A before=()
for f in "${watched[@]}"; do
  [ -e "$f" ] && before[$f]=$(git hash-object -- "$f")
done

if [ ${#cs[@]} -gt 0 ]; then
  step 'dotnet format'
  dotnet format --no-restore --include "${cs[@]}"
fi

if [ "$rust" = yes ]; then
  step 'cargo fmt';    (cd shim && cargo fmt)
  step 'cargo clippy'; (cd shim && cargo clippy --all-targets -- -D warnings)
  step 'cargo test';   (cd shim && cargo test)
fi

if [ "$metainfo" = yes ]; then
  step 'appstreamcli'
  appstreamcli validate --no-net io.github.mark12870.cabinet.metainfo.xml
fi

formatted=(); blocked=()
for f in "${watched[@]}"; do
  [ -e "$f" ] || continue
  [ "$(git hash-object -- "$f")" = "${before[$f]:-}" ] && continue
  if [ -n "${is_staged[$f]:-}" ] && [ -z "${is_dirty[$f]:-}" ]; then
    git add -- "$f"
    formatted+=("$f")
  else
    blocked+=("$f")
  fi
done

if [ ${#formatted[@]} -gt 0 ]; then
  printf 'reformatted and staged: %s\n' "${formatted[*]}" >&2
fi

if [ ${#blocked[@]} -gt 0 ]; then
  printf 'reformatted, but NOT staged because the file had unstaged changes too:\n' >&2
  printf '  %s\n' "${blocked[@]}" >&2
  printf 'review the diff and stage it yourself, or commit with --no-verify\n' >&2
  exit 1
fi

exit 0
