#!/usr/bin/env bash
# The url the repository publishes its OSTree repo to. The release gate and the publish's history
# seed each need it, on different runners, so the formula lives here rather than in each workflow.
#
#   scripts/pages-url.sh
set -euo pipefail

owner=${GITHUB_REPOSITORY%%/*}
printf 'https://%s.github.io/%s/repo/\n' "${owner,,}" "${GITHUB_REPOSITORY#*/}"
