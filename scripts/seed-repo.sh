#!/usr/bin/env bash
# Mirrors the published OSTree repo with its full history into a local one, so a new commit
# parents the published head. It branches on the HTTP status rather than curl's exit, because
# -f collapses every 4xx and 5xx into exit 22 and a down Pages would read as "nothing published
# yet" and silently re-root the history. Pages answers a burst of object fetches with HTTP 429,
# which ostree does not retry, so the pull backs off and resumes; depth -1 stops at the pruned
# end rather than failing. Signatures ride along and clients re-verify them, so the pull does
# not. --mirror writes plain local refs, so the remote is dropped rather than published inside
# repo/config.
#
#   scripts/seed-repo.sh url repo
set -euo pipefail

url=$1
repo=$2

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

ostree --repo="${repo}" init --mode=archive-z2
ostree --repo="${repo}" remote add --no-gpg-verify published "${url}"
attempt=1
until ostree --repo="${repo}" pull --mirror --depth=-1 published; do
  [ "${attempt}" -lt 5 ] || exit 1
  sleep $((attempt * 60))
  attempt=$((attempt + 1))
done
ostree --repo="${repo}" remote delete published
