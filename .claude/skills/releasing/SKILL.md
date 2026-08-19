---
name: releasing
description: Cut and publish a Cabinet release — what the version means, where it lives, and what publishing can and cannot undo. Use when bumping the version, adding a metainfo <release>, committing a fix or feature that ships, running update-yabridge.py, or touching signing and the OSTree repo. Read before editing the metainfo, because a published version cannot be corrected afterwards.
---

# Releasing Cabinet

Everything here was learned by publishing something wrong and not being able to take it back.

## Signing

Cabinet signs with its own key, **not** beeper-flatpak's:

```
ed25519  5C6EC25CCC962DA9B07F5944995E2BEBE73034E6  Cabinet Flatpak (mark12870)
```

Separate on purpose. One key across both repos would mean one revocation breaks both, and
beeper's uid reads "Beeper Flatpak", which would be visibly wrong on Cabinet's site.

The key has **no passphrase**, so only `GPG_PRIVATE_KEY` is set on the repo and the
workflow's `GPG_PASSPHRASE` path stays unused. It has no expiry either, and that is not an
oversight: flatpak pins the key when a client adds the remote, so replacing it forces every
installed user to remove and re-add the remote. Treat it as permanent, and keep it backed up
accordingly.

## Versioning

**Cabinet has its own version, and yabridge's is not it.** Semver: major for a breaking
change, minor for a feature, patch for a fix. It lives in exactly one place — the newest
`<release>` in the metainfo, which is where flatpak reads `Version:` from and where
`render-site.py` reads it back out of each published commit to build the table.

Naming a release after the bundled yabridge was the original scheme and it was wrong: seven
different builds published as `5.1.1`, indistinguishable in the table and in `flatpak info`,
and no way to say that the GUI had landed. A new yabridge *is* a Cabinet release — usually a
patch, a minor if it changes what Cabinet can do — so it gets an entry of its own like
anything else.

`scripts/update-yabridge.py` bumps the dependency and nothing else; it rewrites the
manifest's url/sha256 and leaves the metainfo alone, because which release the bump amounts
to is a judgement it cannot make. Add the `<release>` yourself:

```sh
python3 scripts/update-yabridge.py   # prints updated=true/false, yabridge=<tag>
```

**Bumping is manual, on purpose.** Unlike beeper-flatpak there is no daily workflow chasing
upstream: yabridge releases rarely, and an unattended rebuild would publish a commit every
installed client sees as an update.

A push touching the manifest, `src/`, `shim/` or the metainfo triggers `build-publish.yml`;
otherwise run it from the Actions tab via `workflow_dispatch`. **It publishes only when the
metainfo's newest `<release>` differs from the version at the head of the published
history** — *Decide whether to publish* compares the two and skips the rest of the run,
green, when they match. There is no override, and none is needed: a failed publish leaves
the version unpublished, so a retry still goes through, and a re-root deletes the *Seed*
step, so there is nothing left to compare against.

**A published commit's version cannot be corrected.** The string lives in
`files/share/metainfo/` *inside* the commit, and commits are content-addressed and signed —
editing it produces a different checksum, breaking the parent chain and the signature. The
only choices are to leave it or to re-root: publish once with `build-publish.yml`'s
*Seed the repo from the published history* step deleted, so the build exports a parentless
root and everything before it stops being reachable. That is how the seven commits
published as `5.1.1` were dropped, on 2026-08-18, leaving `0.1.0` as the only one. It is a
hand edit on purpose — a permanent switch for it was tried and removed, because a control
that discards published history should not sit one checkbox away in the Actions tab.
