#!/usr/bin/env python3
"""Point the manifest at a pinned yabridge commit.

The source is a GitHub commit archive. `type: archive` still needs a sha256, and GitHub
does not publish one, so download the archive and hash it.

It does not touch the metainfo. Cabinet's version is its own and yabridge's is a
dependency; carrying a new one is a change worth a release entry, but which release
it is -- patch, minor or major -- is a judgement no script can make.

Exit status is 0 whether or not an update was found; check the `updated` value written
to $GITHUB_OUTPUT (or the final line of stdout) to tell the difference.
"""

from __future__ import annotations

import hashlib
import os
import re
import sys
import urllib.request
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
MANIFEST = REPO / "io.github.mark12870.cabinet.yml"

# GitHub rejects the default Python-urllib User-Agent on some paths.
USER_AGENT = "cabinet-flatpak/update-yabridge (+https://github.com/Mark12870/cabinet)"

URL_RE = re.compile(
    r"(?P<indent>\s*url:\s*)https://github\.com/robbert-vdh/yabridge/"
    r"archive/(?P<commit>[0-9a-f]{40})\.tar\.gz"
)
SHA_RE = re.compile(r"(?P<indent>\s*sha256:\s*)(?P<sha>[0-9a-f]{64})")


def fetch(url: str) -> bytes:
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(request, timeout=120) as response:
        return response.read()


def current_commit(manifest: str) -> str:
    match = URL_RE.search(manifest)
    if not match:
        raise SystemExit("cannot find the yabridge url in the manifest")
    return match.group("commit")


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
    if len(sys.argv) != 2 or not re.fullmatch(r"[0-9a-f]{40}", sys.argv[1]):
        raise SystemExit(f"usage: {Path(sys.argv[0]).name} COMMIT")

    commit = sys.argv[1]
    url = f"https://github.com/robbert-vdh/yabridge/archive/{commit}.tar.gz"
    manifest = MANIFEST.read_text(encoding="utf-8")
    current = current_commit(manifest)

    if current == commit:
        emit(updated="false", yabridge=commit)
        return

    print(f"yabridge {current} -> {commit}", file=sys.stderr)
    sha256 = hashlib.sha256(fetch(url)).hexdigest()

    MANIFEST.write_text(rewrite(manifest, url, sha256), encoding="utf-8")

    print("now add a <release> to the metainfo; a new yabridge is a Cabinet release",
          file=sys.stderr)
    emit(updated="true", yabridge=commit)


if __name__ == "__main__":
    main()
