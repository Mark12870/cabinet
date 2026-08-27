---
name: release
description: Cut and publish a Cabinet release safely. Use when changing the metainfo `<release>`, updating yabridge, signing or publishing the OSTree repository, or committing a shipping change.
---

# Releasing Cabinet

Everything here was learned by publishing something wrong and not being able to take it back.
Read *What a release cannot undo* before step 3 — that is the step with no reverse.

## Cut a release

**1. Pick the number.** Semver against the last one: major for a breaking change, minor for a
feature, patch for a fix. Cabinet has **its own** version and yabridge's is not it — naming
releases after the bundled one published seven different builds as `5.1.1`, indistinguishable
in the table and in `flatpak info`, with no way to say the GUI had landed. A new yabridge *is*
a Cabinet release like anything else: usually a patch, a minor if it changes what Cabinet can
do.

**2. Bump yabridge, if that is what this release is.**

```sh
python3 scripts/update-yabridge.py   # prints updated=true/false, yabridge=<tag>
```

It rewrites the manifest's url and sha256 and stops there, leaving the metainfo alone on
purpose: which release the bump amounts to is a judgement it cannot make.

**3. Add the `<release>` to the metainfo.** The version lives in exactly one place — the newest
`<release>` there — which is where flatpak reads `Version:` from and where `render-site.py`
reads it back out of each published commit to build the table. Nothing goes in a csproj.
Bumping is manual on purpose: yabridge releases rarely, and an unattended rebuild — which is
beeper-flatpak's daily workflow — would publish a commit every client sees as an update.

**4. Don't commit or push anything without approval.**