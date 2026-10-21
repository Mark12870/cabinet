# Automatic catalogue updates

`Catalogue updates` checks upstream releases daily and on manual dispatch. Every shipped entry appears in its
`coverage.json` artifact and GitHub summary. Every pinned download must have a verified discovery rule or a specific
blocker; CI rejects unclassified entries. Rolling and BYO sources are reported but cannot receive automatic version pins.

A newer release gets a PR changing only version, URL and calculated SHA-256, plus a Cabinet release note, when its scenario
covers every declared format. Discovery still checks entries with unsupported formats or missing scenarios, but never
downloads their installers or proposes bumps. Beta catalogue versions and expiring signed download URLs also block proposals.

CI builds the candidate Flatpak and runs its isolated scenario. Every declared format and additional test case must pass
without skips or retries. Merging requires repository checks, build and runtime success, the tested head and an unchanged
base. A moved `main` waits for the next discovery run to refresh the candidate. Failed candidates stay open.

The PR increments Cabinet's patch release and adds a note for the updated plugin. Merging it makes that release eligible
for the existing release gate; publishing still waits for normal CI checks and build.

## GitHub setup

1. In **Settings → Actions → General → Workflow permissions**, allow GitHub Actions to create and approve pull requests.
   The workflows use the built-in `GITHUB_TOKEN`; no App or PAT credentials are needed. They do not bypass branch
   protection.
2. Keep `main` protected with required checks `checks`, `build / build` and `candidate` (confirm displayed names in the
   first run). Automatic merging also requires the candidate runtime; do not require that conditional job for ordinary
   PRs or require `merge-candidate`. The merge is a non-forced fast-forward of the exact tested head and base.
3. Keep the scenario account secrets used by `Plugins` configured. Run `Catalogue updates` manually to verify setup.

Only catalogue scenarios receive those account secrets. Existing GPG signing and plugin-account secrets are unchanged.

## Discovery configuration

`data/library/<vendor>/catalogue-updates.json` files hold rules and blockers, not copied versions; each lives beside its
vendor's entries and paths remain relative to `data/library`. Rules select official GitHub releases, download pages,
published documents, redirects or a suite-specific Debian index. Allowed HTTPS hosts cover metadata, downloads and
redirects. Human metadata validates compact filenames; versions are never guessed. DrumGizmo remains on bookworm.

Inspect coverage offline, check upstream metadata without fetching installers, or generate candidate YAML:

```sh
dotnet run --project tools/Cabinet.Ci -- catalogue-updates coverage /tmp/catalogue-coverage
dotnet run --project tools/Cabinet.Ci -- catalogue-updates check /tmp/catalogue-check
dotnet run --project tools/Cabinet.Ci -- catalogue-updates discover /tmp/catalogue-candidates
```

Reports distinguish discovery, automation eligibility and each check's outcome. Unexpected upstream failures fail the
workflow after saving reports; known blockers do not. Candidate YAML and `candidates.json` never change the catalogue
directly. None of these commands installs or runs software.
