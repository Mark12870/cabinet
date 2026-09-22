#!/usr/bin/env bash
# Says whether the newest <release> in the metainfo is already published, which is the only thing
# that makes a run a release. The checks job asks so it can gate publishing, and the build asks so
# it can skip seeding a history that a discarded artifact will never parent.
#
#   scripts/release-gate.sh io.github.mark12870.cabinet https://owner.github.io/cabinet/repo/ dir
#
# Prints true or false; says why on stderr.
set -euo pipefail

app=$1
url=$2
work=$3

newest() { grep -oE '<release[[:space:]]+version="[^"]+"' | head -1 | cut -d'"' -f2; }

version="$(newest < "${app}.metainfo.xml")"
[ -n "${version}" ] || {
  echo "::error::no <release> version in ${app}.metainfo.xml" >&2
  exit 1
}

# Branch on the status itself, not on curl's exit: -f collapses every 4xx and 5xx into exit 22,
# so a 403 or a down Pages would read as "nothing published yet" and publish over it.
code="$(curl -sSI -o /dev/null -w '%{http_code}' \
        --retry 3 --retry-all-errors "${url}summary")" || code=000
published=""

case "${code}" in
  200)
    ref="app/${app}/x86_64/stable"
    metainfo="/files/share/metainfo/${app}.metainfo.xml"
    ostree --repo="${work}" init --mode=archive-z2
    ostree --repo="${work}" remote add --if-not-exists --no-gpg-verify published "${url}"
    ostree --repo="${work}" pull --subpath="$(dirname "${metainfo}")" published "${ref}"
    published="$(ostree --repo="${work}" cat "published:${ref}" "${metainfo}" | newest)" ;;
  404|410)
    echo "::notice::nothing published at ${url} yet" >&2 ;;
  *)
    echo "::error::cannot reach ${url} (HTTP ${code})" >&2
    exit 1 ;;
esac

if [ "${version}" = "${published}" ]; then
  echo "::notice::${version} is already published; nothing to release" >&2
  echo false
else
  echo "::notice::releasing ${version}${published:+ over ${published}}" >&2
  echo true
fi
