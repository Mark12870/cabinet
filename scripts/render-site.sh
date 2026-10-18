#!/usr/bin/env bash
# Renders the Pages front page and the .flatpakrepo from the published repo. The version table is
# read back out of the OSTree history, so it lists exactly what a client can install.
#
#   scripts/render-site.sh --repo repo --out /tmp/site --unsigned
set -euo pipefail

template=$(realpath "$(dirname "$0")/..")/site/index.html.in

repo='' out='' base_url=. homepage=https://github.com/Mark12870/cabinet
gpg_fingerprint='' gpg_key='' unsigned=no
while [ $# -gt 0 ]; do
  case $1 in
    --repo) repo=$2; shift 2 ;;
    --out) out=$2; shift 2 ;;
    --base-url) base_url=$2; shift 2 ;;
    --homepage) homepage=$2; shift 2 ;;
    --gpg-fingerprint) gpg_fingerprint=$2; shift 2 ;;
    --gpg-key) gpg_key=$2; shift 2 ;;
    --unsigned) unsigned=yes; shift ;;
    *) echo "unknown argument: $1" >&2; exit 1 ;;
  esac
done

[ -n "$repo" ] && [ -n "$out" ] || { echo "--repo and --out are required" >&2; exit 1; }
[ -n "$gpg_key" ] || [ "$unsigned" = yes ] ||
  { echo "--gpg-key is required; pass --unsigned for a local preview" >&2; exit 1; }

ostree() { LC_ALL=C command ostree --repo="$repo" "$@"; }
escape() { sed -e 's/&/\&amp;/g' -e 's/</\&lt;/g' -e 's/>/\&gt;/g' -e 's/"/\&quot;/g' -e "s/'/\&#x27;/g" <<< "$1"; }

mapfile -t refs < <(ostree refs | grep '^app/')
[ ${#refs[@]} -eq 1 ] || { echo "expected exactly one app/ ref in $repo, found ${refs[*]}" >&2; exit 1; }
ref=${refs[0]}
app_id=$(cut -d/ -f2 <<< "$ref")
base=${base_url%/}

mapfile -t history < <(ostree log "$ref" | awk '/^commit /{c=$2} /^Date:/ && c {print c, $2; c=""}')
[ ${#history[@]} -gt 0 ] || { echo "no commits found in $repo" >&2; exit 1; }

rows=''
for index in "${!history[@]}"; do
  read -r commit date <<< "${history[$index]}"
  version=$(ostree cat "$commit" "/files/share/metainfo/$app_id.metainfo.xml" 2>/dev/null |
    grep -oE '<release[[:space:]]+version="[^"]+"' | head -1 | cut -d'"' -f2 || true)
  version=${version:-?}
  printf '%-10s %s  %s\n' "$version" "$date" "${commit:0:12}"
  current=''
  if [ "$index" -eq 0 ]; then
    current=' class="is-current"'
    current_version=$version current_date=$date
  fi
  rows+="${rows:+$'\n'}    <tr$current><td>$(escape "$version")</td><td>$date</td><td class=\"commit\"><code>$commit</code></td></tr>"
done

mkdir -p "$out"

{
  printf '%s\n' '[Flatpak Repo]' 'Title=Cabinet' "Url=$base/repo/" "Homepage=$homepage" \
    'Comment=Windows VST plugins on Linux, out of the box' \
    'Description=Wine packaged as a Flatpak, bridging Windows VST2, VST3 and CLAP plugins into a Linux DAW with upstream yabridge, out of the box.'
  if [ -n "$gpg_key" ]; then printf 'GPGKey=%s\n' "$gpg_key"; fi
} > "$out/$app_id.flatpakrepo"

homepage=$(escape "$homepage")
current_version=$(escape "$current_version")
gpg_fingerprint=$(escape "${gpg_fingerprint:-unsigned build}")
flatpakrepo_url=$base/$app_id.flatpakrepo
history_depth=${#history[@]}
generated=$(date -u '+%Y-%m-%d %H:%M UTC')
fields='$app_id $flatpakrepo_url $homepage $current_version $current_date $history_depth $gpg_fingerprint $rows $generated'
export app_id flatpakrepo_url homepage current_version current_date history_depth gpg_fingerprint rows generated
envsubst "$fields" < "$template" > "$out/index.html"

echo "wrote $out/index.html and $app_id.flatpakrepo" >&2
