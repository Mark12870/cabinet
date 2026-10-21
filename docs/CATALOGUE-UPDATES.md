# Automatic catalogue updates

`Catalogue updates` checks upstream releases daily and on manual dispatch. Every shipped entry appears in its
`coverage.json` artifact and GitHub summary. Every pinned download must have a verified discovery rule or a specific
blocker; CI rejects unclassified entries. Rolling and BYO sources are reported but cannot receive automatic version pins.

A newer release gets one batch PR on `catalogue-updates`, changing only candidate entries and one Cabinet release note.
Discovery still checks entries with unsupported formats or missing scenarios, but never downloads their installers or
proposes bumps. Beta catalogue versions and expiring signed download URLs also block proposals.

The bot commit uses `[skip ci]` to avoid a duplicate push or pull-request run. Discovery explicitly dispatches CI from
trusted `main`; that run validates the exact PR, base and head, builds one Flatpak artifact, runs the general runtime suite,
and runs all affected scenarios together in parallel. Every affected entry must pass every declared format and additional
test case without skips or retries. If `main` moved, promotion and publication stop; discovery must refresh and retest.

After the exact tested head is fast-forwarded to `main`, that same CI run publishes the same tested artifact through the
normal release gate. It does not dispatch a second CI run after promotion. Failed candidates stay open.

## GitHub setup

1. In **Settings → Actions → General → Workflow permissions**, allow GitHub Actions to create and approve pull requests.
   The workflows use the built-in `GITHUB_TOKEN`; no App or PAT credentials are needed. They do not bypass branch
   protection.
2. Keep the existing protection on `main`. The workflow does not bypass branch protection; if a rule requires PR-attached
   status checks or otherwise rejects its non-forced fast-forward, promotion fails closed. Do not weaken or bypass those
   rules to make a candidate pass.
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

Reports distinguish discovery, automation eligibility and each check's outcome. Failed upstream checks are reported and
skipped without blocking other candidates. Candidate YAML and `candidates.json` never change the catalogue
directly. None of these commands installs or runs software.
