#!/usr/bin/env bash
# The url the repository publishes its OSTree repo to. The checks gate, the build's history seed
# and a publish refresh each need it, on three different runners, so the formula lives here rather
# than three times in the workflow.
#
#   scripts/pages-url.sh
set -euo pipefail

owner=${GITHUB_REPOSITORY%%/*}
printf 'https://%s.github.io/%s/repo/\n' "${owner,,}" "${GITHUB_REPOSITORY#*/}"
