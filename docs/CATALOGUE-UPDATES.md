# Automatic catalogue updates

`Catalogue updates` checks upstream releases daily and on manual dispatch. Every shipped entry appears in its
`coverage.json` artifact and GitHub summary. Every pinned download must have a verified discovery rule or a specific
blocker; CI rejects unclassified entries. Rolling and BYO sources are reported but cannot receive automatic version pins.

A newer release gets a PR changing only version, URL and calculated SHA-256 when its scenario covers every declared
format. Discovery still checks entries with unsupported formats or missing scenarios, but never downloads their
installers or proposes bumps. Beta catalogue versions and expiring signed download URLs also block automatic proposals.

CI builds the candidate Flatpak and runs its isolated scenario. Every declared format and additional test case must pass
without skips or retries. Merging requires repository checks, build and runtime success, the tested head and an unchanged
base. A moved `main` waits for the next discovery run to refresh the candidate. Failed candidates stay open.

Catalogue updates ship with the next Cabinet release; they do not bump Cabinet's release version.

## GitHub setup

1. Install a GitHub App on this repository with **Contents: read and write** and **Pull requests: read and write**.
   Do not grant it branch-protection bypass. Allow squash merging in the repository settings.
2. Protect `main` with **Require branches to be up to date before merging** and required checks `checks`,
   `build / build` and `candidate` (confirm displayed names in the first run). Automatic merging additionally requires
   the candidate runtime; do not require that conditional job for ordinary PRs. Never require `merge-candidate`:
   it must merge before its own job finishes. The App must obey these protections,
   which close the race between checking the tested base and GitHub accepting the merge.
3. Set repository variable `CATALOGUE_UPDATE_APP_ID` to its App ID and `CATALOGUE_UPDATE_BOT` to its bot login
   (`<app-slug>[bot]`). Store its private key in secret `CATALOGUE_UPDATE_APP_PRIVATE_KEY`.
4. Keep the scenario account secrets used by `Plugins` configured. Run `Catalogue updates` manually to verify setup.

Discovery and coverage reporting run without App credentials; only PR creation and merging require them. The App token
never reaches installers or scenarios. App-created PRs and merges trigger normal CI; required approvals still apply.

## Discovery configuration

`data/catalogue-updates.json` holds rules and explicit metadata blockers, not copied catalogue versions. Rules select
official GitHub releases, download pages, published documents, redirects or a suite-specific Debian index. Allowed HTTPS
hosts cover metadata, downloads and redirects. Human release metadata validates compact filenames; versions are never
guessed. DrumGizmo remains on bookworm rather than following another Debian suite.

Inspect coverage offline, check upstream metadata without fetching installers, or generate candidate YAML:

```sh
dotnet run --project tools/Cabinet.Ci -- catalogue-updates coverage /tmp/catalogue-coverage
dotnet run --project tools/Cabinet.Ci -- catalogue-updates check /tmp/catalogue-check
dotnet run --project tools/Cabinet.Ci -- catalogue-updates discover /tmp/catalogue-candidates
```

Reports distinguish discovery, automation eligibility and each check's outcome. Unexpected upstream failures fail the
workflow after saving reports; known blockers do not. Candidate YAML and `candidates.json` never change the catalogue
directly. None of these commands installs or runs software.
