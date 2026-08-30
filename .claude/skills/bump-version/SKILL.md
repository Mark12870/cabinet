---
name: bump-version
description: Bump Cabinet's version by adding a release to the metainfo. Use when asked to bump, raise or cut Cabinet's version number.
---

# Bumping Cabinet's version

Cabinet's version is the newest `<release>` in `io.github.mark12870.cabinet.metainfo.xml` and
nowhere else. Flatpak reads `Version:` from it and `render-site.py` reads it back out of every
published commit to build the site's table. Nothing goes in a csproj.

**1. Read the newest `<release>`.** It sits at the top of `<releases>`; that version is the one
being bumped from.

**2. Pick the number.** Semver against it, and only one digit ever moves:

| This release | Bump | `0.11.1` becomes |
| --- | --- | --- |
| A feature | the **middle** number, last back to `0` | `0.12.0` |
| A fix | the **last** number | `0.11.2` |

Leave the first number alone; ask before touching it. Cabinet's version is its own — a bundled
yabridge's is not it, so never name a release after yabridge. A new yabridge is a Cabinet
release like any other: the last number, or the middle one when it changes what Cabinet can do.

**3. Insert the new `<release>` above the newest one**, dated today, with a blank line between
it and the one below:

```xml
    <release version="0.12.0" date="2026-08-30">
      <description>
        <p>One line saying what this release is</p>
        <ul>
          <li>What changed, in the user's terms</li>
        </ul>
      </description>
    </release>
```

The `<p>` is the headline and the `<ul>` one `<li>` per user-visible change. Write for whoever
sees the update, not for a reviewer: no file names, no internal types. Escape `<`, `>` and `&`
as `&lt;`, `&gt;` and `&amp;` — a `cabinet set <name>` written raw breaks the XML.

**4. Validate it.**

```sh
appstreamcli validate --no-net io.github.mark12870.cabinet.metainfo.xml
```

`MetainfoTests` checks the same file, so `scripts/checks.sh` covers it too.

**5. Stop there.** Bumping is not releasing: don't commit, tag, build or publish without being
asked.
