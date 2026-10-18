#!/usr/bin/env bash
# Emails the plugin scenarios' AI diagnosis as plain text with an HTML alternative, rendered by
# GitHub's own Markdown API. Implicit TLS on port 465, STARTTLS on any other.
#
#   scripts/email-diagnosis.sh diagnosis.md
set -euo pipefail

diagnosis=$1

missing=()
for name in SMTP_HOST SMTP_USERNAME SMTP_PASSWORD SMTP_TO GITHUB_TOKEN GITHUB_RUN_ID; do
  [ -n "${!name:-}" ] || missing+=("$name")
done
if [ ${#missing[@]} -gt 0 ]; then
  echo "::error::Configure these diagnosis email settings: $(IFS=,; echo "${missing[*]}" | sed 's/,/, /g')"
  exit 1
fi

failed() {
  echo "::error::Diagnosis email failed ($1). The AI summary is on the Actions run page."
  exit 1
}

styled() {
  sed -E \
    -e 's#<h2( [^>]*)?>#<h2 style="font-size:24px;margin:0 0 20px;">#g' \
    -e 's#<h3( [^>]*)?>#<h3 style="font-size:18px;margin:28px 0 12px;">#g' \
    -e 's#<p( [^>]*)?>#<p style="margin:12px 0;">#g' \
    -e 's#<ul( [^>]*)?>#<ul style="padding-left:24px;">#g' \
    -e 's#<li( [^>]*)?>#<li style="margin:8px 0;">#g' \
    -e 's#<blockquote( [^>]*)?>#<blockquote style="margin:16px 0;padding:0 16px;border-left:4px solid \#dddddd;">#g' \
    -e 's#<pre( [^>]*)?>#<pre style="padding:16px;background:\#f5f5f5;border-radius:8px;white-space:pre-wrap;overflow-wrap:anywhere;">#g' \
    -e 's#<code( [^>]*)?>#<code style="font-family:monospace;font-size:13px;">#g' \
    -e 's#<table( [^>]*)?>#<table style="border-collapse:collapse;width:100%;">#g' \
    -e 's#<th( [^>]*)?>#<th style="padding:8px;text-align:left;border:1px solid \#dddddd;">#g' \
    -e 's#<td( [^>]*)?>#<td style="padding:8px;border:1px solid \#dddddd;">#g' \
    -e 's#<a #<a style="color:\#227d66;" #g'
}

readable=$(sed -e 's#<details><summary>As recorded</summary>#\#\#\# As recorded#' -e 's#</details>##' "$diagnosis")
rendered=$(jq -n --arg text "$readable" '{text: $text, mode: "gfm"}' |
  curl -fsS --max-time 30 -H "Authorization: Bearer $GITHUB_TOKEN" -H 'Accept: application/vnd.github+json' \
    --data @- https://api.github.com/markdown) || failed "Markdown rendering"

boundary="cabinet-diagnosis-$GITHUB_RUN_ID"
subject=$(printf 'Cabinet Plugins AI diagnosis — run %s' "$GITHUB_RUN_ID" | base64 -w0)
message=$(mktemp)
trap 'rm -f "$message"' EXIT
{
  printf 'From: %s\nTo: %s\nSubject: =?UTF-8?B?%s?=\n' "$SMTP_USERNAME" "$SMTP_TO" "$subject"
  printf 'MIME-Version: 1.0\nContent-Type: multipart/alternative; boundary="%s"\n\n' "$boundary"
  printf -- '--%s\nContent-Type: text/plain; charset=utf-8\nContent-Transfer-Encoding: base64\n\n' "$boundary"
  base64 < "$diagnosis"
  printf '\n--%s\nContent-Type: text/html; charset=utf-8\nContent-Transfer-Encoding: base64\n\n' "$boundary"
  {
    printf '%s' '<!doctype html><html><body style="margin:0;">'
    printf '%s' '<div style="max-width:720px;margin:auto;padding:24px;font-family:Arial,sans-serif;font-size:16px;line-height:1.6;">'
    styled <<< "$rendered"
    printf '%s' '</div></body></html>'
  } | base64
  printf '\n--%s--\n' "$boundary"
} > "$message"

port=${SMTP_PORT:-465}
if [ "$port" = 465 ]; then url="smtps://$SMTP_HOST:$port"; else url="smtp://$SMTP_HOST:$port"; fi

printf 'user = "%s"\n' "$(printf '%s:%s' "$SMTP_USERNAME" "$SMTP_PASSWORD" | sed 's/[\\"]/\\&/g')" |
  curl -fsS --max-time 30 --ssl-reqd --crlf --config - --url "$url" \
    --mail-from "$SMTP_USERNAME" --mail-rcpt "$SMTP_TO" --upload-file "$message" || failed "SMTP"
