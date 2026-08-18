#!/usr/bin/env python3
"""Render the Pages front page and the .flatpakrepo from the published repo.

The version table is read back out of the OSTree history rather than tracked
separately, so it lists exactly what a client can install.

Simpler than beeper-flatpak's equivalent, which also had to check whether upstream
still served each commit's `extra-data`: Cabinet bundles yabridge and Wine inside the
commit, so every version still in the repo is installable by definition.

Usage locally, against a repo built by flatpak-builder:

    python3 scripts/render-site.py --repo repo --out /tmp/site --unsigned
"""

from __future__ import annotations

import argparse
import html
import os
import re
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path
from string import Template

REPO = Path(__file__).resolve().parent.parent
TEMPLATE = REPO / "site" / "index.html.in"

# The newest <release> in the metainfo shipped inside a commit is Cabinet's own
# version at the time that commit was built. It is not yabridge's -- that is a
# dependency, and update-yabridge.py deliberately leaves the metainfo alone.
RELEASE_RE = re.compile(r'<release\s+version="([^"]+)"')


def ostree(repo: Path, *args: str) -> str:
    # LC_ALL=C so `log` timestamps stay parseable under any runner locale.
    result = subprocess.run(
        ["ostree", f"--repo={repo}", *args],
        check=True, capture_output=True, text=True, env={**os.environ, "LC_ALL": "C"},
    )
    return result.stdout


def app_ref(repo: Path) -> str:
    refs = [r for r in ostree(repo, "refs").split() if r.startswith("app/")]
    if len(refs) != 1:
        raise SystemExit(f"expected exactly one app/ ref in {repo}, found {refs}")
    return refs[0]


def history(repo: Path, ref: str) -> list[tuple[str, datetime]]:
    """Return (commit, timestamp) newest first, for every commit still present."""
    commits, commit = [], None
    for line in ostree(repo, "log", ref).splitlines():
        if line.startswith("commit "):
            commit = line.split()[1]
        elif line.startswith("Date:") and commit:
            when = datetime.strptime(line.split(":", 1)[1].strip(), "%Y-%m-%d %H:%M:%S %z")
            commits.append((commit, when))
            commit = None
    return commits


def cabinet_version(repo: Path, commit: str, app_id: str) -> str:
    path = f"/files/share/metainfo/{app_id}.metainfo.xml"
    try:
        match = RELEASE_RE.search(ostree(repo, "cat", commit, path))
    except subprocess.CalledProcessError:
        return "?"
    return match.group(1) if match else "?"


def render_rows(versions: list[tuple[str, datetime, str]]) -> str:
    rows = []
    for index, (version, when, commit) in enumerate(versions):
        current = ' class="is-current"' if index == 0 else ""
        rows.append(
            f"    <tr{current}>"
            f"<td>{html.escape(version)}</td>"
            f'<td>{when.strftime("%Y-%m-%d")}</td>'
            f'<td class="commit"><code>{commit}</code></td></tr>'
        )
    return "\n".join(rows)


def write_flatpakrepo(path: Path, base: str, homepage: str, key: str) -> None:
    lines = [
        "[Flatpak Repo]",
        "Title=Cabinet",
        f"Url={base}/repo/",
        f"Homepage={homepage}",
        "Comment=Windows VST plugins in per-plugin Wine prefixes",
        "Description=Wine packaged as a Flatpak, bridging Windows VST2, VST3 and CLAP"
        " plugins into a Linux DAW with upstream yabridge, one Wine prefix per plugin.",
    ]
    if key:
        lines.append(f"GPGKey={key}")
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, required=True, help="OSTree repo to describe")
    parser.add_argument("--out", type=Path, required=True, help="directory to write into")
    parser.add_argument("--base-url", default=".", help="public URL of --out, no trailing slash")
    parser.add_argument("--homepage", default="https://github.com/Mark12870/cabinet")
    parser.add_argument("--gpg-fingerprint", default="")
    parser.add_argument("--gpg-key", default="", help="base64 of the exported public key")
    parser.add_argument("--unsigned", action="store_true", help="local previews only")
    args = parser.parse_args()

    # A .flatpakrepo with no GPGKey adds the remote with gpg-verify off, which is a
    # silent downgrade for everyone who installs from it. Make it a deliberate choice.
    if not args.gpg_key and not args.unsigned:
        raise SystemExit("--gpg-key is required; pass --unsigned for a local preview")

    ref = app_ref(args.repo)
    app_id = ref.split("/")[1]
    base = args.base_url.rstrip("/")

    versions = [
        (cabinet_version(args.repo, commit, app_id), when, commit)
        for commit, when in history(args.repo, ref)
    ]
    if not versions:
        raise SystemExit(f"no commits found in {args.repo}")

    for version, when, commit in versions:
        print(f"{version:<10} {when:%Y-%m-%d}  {commit[:12]}")

    current_version, current_when, _ = versions[0]

    args.out.mkdir(parents=True, exist_ok=True)
    page = Template(TEMPLATE.read_text(encoding="utf-8")).substitute(
        app_id=app_id,
        flatpakrepo_url=f"{base}/{app_id}.flatpakrepo",
        homepage=html.escape(args.homepage),
        current_version=html.escape(current_version),
        current_date=f"{current_when:%Y-%m-%d}",
        history_depth=len(versions),
        gpg_fingerprint=html.escape(args.gpg_fingerprint) or "unsigned build",
        rows=render_rows(versions),
        generated=f"{datetime.now(timezone.utc):%Y-%m-%d %H:%M UTC}",
    )
    (args.out / "index.html").write_text(page, encoding="utf-8")
    write_flatpakrepo(args.out / f"{app_id}.flatpakrepo", base, args.homepage, args.gpg_key)
    print(f"wrote {args.out}/index.html and {app_id}.flatpakrepo", file=sys.stderr)


if __name__ == "__main__":
    main()
