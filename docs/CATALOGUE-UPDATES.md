# Automatic catalogue updates

`Catalogue updates` checks upstream releases daily and on manual dispatch. Its cron is 23:23 UTC; the full plugin suite
runs at 00:23 UTC, one hour later. `coverage.json` and the summary cover every entry; CI rejects pinned downloads without
a verified rule or blocker. Unsupported formats, missing scenarios, beta versions and expiring URLs block proposals.
Rolling and BYO sources are reported but cannot receive version pins; discovery never fetches installers.

```text
Ordinary CI ── shared checks/build/general runtime ── release gate ── shared publish
Bot PR [skip ci] ── candidate dispatch on main ── validate exact PR/head/base
                                                     ├─ shared checks/build/general runtime
                                                     ├─ selected scenarios
                                                     └─ fast-forward → publish same artifact
```

The candidate workflow validates the immutable PR, head and base, then permits only catalogue YAML files and Cabinet's
release notes. Every affected entry must pass every declared format and additional test case without skips or retries.
If main moves, promotion and publication stop; discovery must refresh and retest. There is no second post-promotion build;
failed candidates stay open.

## GitHub setup

1. In **Settings → Actions → General → Workflow permissions**, allow GitHub Actions to create and approve pull requests.
   The workflows use the built-in `GITHUB_TOKEN`; no App or PAT credentials are needed.
2. Keep the existing protection on `main`. Promotion does not bypass it; if a rule rejects the non-forced fast-forward,
   the candidate fails closed. Do not weaken or bypass those rules.
3. Keep the scenario account secrets used by `Plugins` configured. Run `Catalogue updates` manually to verify setup.

Only catalogue scenarios receive those account secrets. Existing GPG signing and plugin-account secrets are unchanged.

## Discovery configuration

`data/library/<vendor>/catalogue-updates.json` files hold rules and blockers beside their vendor's entries. Rules select
official releases, download pages, documents, redirects or a suite-specific Debian index. Allowed HTTPS hosts cover
metadata, downloads and redirects. Human metadata validates compact filenames; versions are never guessed.

Inspect coverage, check upstream metadata, or generate candidates without fetching installers:

```sh
dotnet run --project tools/Cabinet.Ci -- catalogue-updates coverage /tmp/catalogue-coverage
dotnet run --project tools/Cabinet.Ci -- catalogue-updates check /tmp/catalogue-check
dotnet run --project tools/Cabinet.Ci -- catalogue-updates discover /tmp/catalogue-candidates
```

Reports distinguish discovery, automation eligibility and each check's outcome. Failed upstream checks are reported and
skipped without blocking other candidates. Candidate YAML and `candidates.json` never change the catalogue directly.
