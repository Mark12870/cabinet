#!/usr/bin/env bash
# Mirrors the published OSTree repo with its full history into a local one, so a new commit
# parents the published head. It branches on the HTTP status rather than curl's exit, because
# -f collapses every 4xx and 5xx into exit 22 and a down Pages would read as "nothing published
# yet" and silently re-root the history. Pages answers a burst of object fetches with HTTP 429,
# so a copy of what was last deployed is taken first: one whose summary matches the published
# one is the published repo and needs no pull, and any other copy leaves the pull only what it
# lacks. The pull backs off and resumes, as ostree does not retry a 429; depth -1 stops at the
# pruned end rather than failing. Signatures ride along and clients re-verify them, so the pull
# does not. --mirror writes plain local refs, so the remote is dropped rather than published
# inside repo/config.
#
#   scripts/seed-repo.sh url repo [copy...]
set -euo pipefail

url=$1
repo=$2
shift 2

code="$(curl -sSI -o /dev/null -w '%{http_code}' \
        --retry 3 --retry-all-errors "${url}summary")" || code=000
case "${code}" in
  200) ;;
  404|410)
    echo "::notice::nothing published at ${url} yet; starting a fresh history"
    exit 0 ;;
  *)
    echo "::error::cannot reach ${url} (HTTP ${code})"
    exit 1 ;;
esac

for copy in "$@"; do
  if [ -f "${copy}/config" ]; then
    mv "${copy}" "${repo}"
    break
  fi
done

summary=$(mktemp)
trap 'rm -f "${summary}"' EXIT
if curl -sSf --retry 5 --retry-all-errors --retry-delay 30 -o "${summary}" "${url}summary" &&
   [ -f "${repo}/summary" ] && cmp -s "${summary}" "${repo}/summary"; then
  echo "the copy of the published repo is current"
  exit 0
fi

[ -f "${repo}/config" ] || ostree --repo="${repo}" init --mode=archive-z2
ostree --repo="${repo}" remote add --no-gpg-verify published "${url}"
attempt=1
until ostree --repo="${repo}" pull --mirror --depth=-1 published; do
  [ "${attempt}" -lt 5 ] || exit 1
  echo "::warning::the pull from ${url} failed; retrying in $((attempt * 60)) s"
  sleep $((attempt * 60))
  attempt=$((attempt + 1))
done
ostree --repo="${repo}" remote delete published
