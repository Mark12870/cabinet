#!/usr/bin/env python3
"""Point the manifest at the current yabridge release.

Unlike Beeper, yabridge publishes real GitHub releases, so there is no redirect to
follow and no hash to compute by hand -- except that `type: archive` still needs a
sha256, and GitHub does not publish one. So: read the latest release, and if the tag
moved, download the tarball to hash it.

It does not touch the metainfo. Cabinet's version is its own and yabridge's is a
dependency; carrying a new one is a change worth a release entry, but which release
it is -- patch, minor or major -- is a judgement no script can make.

Exit status is 0 whether or not an update was found; check the `updated` value written
to $GITHUB_OUTPUT (or the final line of stdout) to tell the difference.
"""

from __future__ import annotations

import hashlib
import json
import os
import re
import sys
import urllib.request
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
MANIFEST = REPO / "io.github.mark12870.cabinet.yml"

RELEASES_URL = "https://api.github.com/repos/robbert-vdh/yabridge/releases/latest"

# GitHub rejects the default Python-urllib User-Agent on some paths.
USER_AGENT = "cabinet-flatpak/update-yabridge (+https://github.com/Mark12870/cabinet)"

URL_RE = re.compile(r"(?P<indent>\s*url:\s*)(?P<url>\S*/yabridge-(?P<version>[\d.]+)\.tar\.gz)")
SHA_RE = re.compile(r"(?P<indent>\s*sha256:\s*)(?P<sha>[0-9a-f]{64})")


def fetch(url: str) -> bytes:
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(request, timeout=120) as response:
        return response.read()


def latest_release() -> tuple[str, str]:
    """Return (version, tarball url) for the newest yabridge release."""
    release = json.loads(fetch(RELEASES_URL))
    tag = release["tag_name"]

    for asset in release.get("assets", []):
        if asset["name"] == f"yabridge-{tag}.tar.gz":
            return tag, asset["browser_download_url"]

    raise SystemExit(f"release {tag} has no yabridge-{tag}.tar.gz asset")


def current_version(manifest: str) -> str:
    match = URL_RE.search(manifest)
    if not match:
        raise SystemExit("cannot find the yabridge url in the manifest")
    return match.group("version")


def rewrite(manifest: str, url: str, sha256: str) -> str:
    """Replace the url and the sha256 that follows it, leaving formatting alone."""
    manifest, count = URL_RE.subn(lambda m: m.group("indent") + url, manifest, count=1)
    if count != 1:
        raise SystemExit("could not rewrite the url")

    manifest, count = SHA_RE.subn(lambda m: m.group("indent") + sha256, manifest, count=1)
    if count != 1:
        raise SystemExit("could not rewrite the sha256")

    return manifest


def emit(**values: str) -> None:
    for key, value in values.items():
        print(f"{key}={value}")

    if output := os.environ.get("GITHUB_OUTPUT"):
        with open(output, "a", encoding="utf-8") as handle:
            for key, value in values.items():
                print(f"{key}={value}", file=handle)


def main() -> None:
    version, url = latest_release()
    manifest = MANIFEST.read_text(encoding="utf-8")

    if current_version(manifest) == version:
        emit(updated="false", yabridge=version)
        return

    print(f"yabridge {current_version(manifest)} -> {version}", file=sys.stderr)
    sha256 = hashlib.sha256(fetch(url)).hexdigest()

    MANIFEST.write_text(rewrite(manifest, url, sha256), encoding="utf-8")

    print("now add a <release> to the metainfo; a new yabridge is a Cabinet release",
          file=sys.stderr)
    emit(updated="true", yabridge=version)


if __name__ == "__main__":
    main()
