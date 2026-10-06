#!/usr/bin/env python3
"""Ask a language model why the plugin scenarios failed, and print its answer as Markdown.

Reads the runtime artifact the Plugins workflow uploads: the .trx of the last attempt, and each
scenario's artefacts. Every scenario that still failed on its last attempt contributes its
error, the tail of its output and of its logs, and its catalogue entry. Every secret the run
held, every URL query and every long token is redacted before anything leaves the runner.

It is shaped for Groq's free tier, which allows gpt-oss-120b 8,000 tokens a minute: each failed
entry is its own request, its formats sharing one set of logs, and the evidence and the answer
together stay under that limit. A rate limit is waited out as Groq's retry-after asks. Past
ENTRIES failed entries the cause is almost surely Cabinet's own, so the rest are only listed.
An answer the API refuses becomes the reason in the printed Markdown, so the report still goes
out.

    GROQ_API_KEY=... scripts/scenario-diagnosis.py runtime
"""

from __future__ import annotations

import json
import os
import re
import sys
import time
import urllib.error
import urllib.request
import xml.etree.ElementTree as ElementTree
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
report = __import__("scenario-report")

ENDPOINT = "https://api.groq.com/openai/v1/chat/completions"
MODEL = "openai/gpt-oss-120b"
SECRETS = ("EMAIL", "PASSWORD", "USERNAME", "DROPBOX", "NATIVE_ACCESS", "WAVES", "GROQ_API_KEY")
EVIDENCE = 12000
ANSWER = 3000
ENTRIES = 6
TRIES = 3
LONGEST_WAIT = 90
TAIL = 40
LOGS = ("install.log", "compile.log")

QUERY_RE = re.compile(r"(https?://[^\s?\"']+)\?[^\s\"']*")
TOKEN_RE = re.compile(r"[A-Za-z0-9+/_=.-]{40,}")

PROMPT = """You diagnose a failed plugin scenario of Cabinet, a Flatpak that installs Windows audio
plugins into one Wine prefix per vendor and bridges them to Linux DAWs with yabridge. A scenario
installs one catalogue entry from its pinned download into a fresh home, bridges it, renders
audio through Carla and opens the editor, which must show more than one colour and respond to
the pointer. Give the most likely cause (a moved or changed vendor download, a changed
installer, Wine or the runner, Cabinet itself, or a flaky network or display), quote the lines
that show it, and name the next step. Say so when the evidence is not enough. Answer in at most
fifteen lines of Markdown with no heading."""


def redact(text: str) -> str:
    for name in SECRETS:
        value = os.environ.get(name, "")
        if len(value) >= 4:
            text = text.replace(value, f"<{name}>")
    text = QUERY_RE.sub(r"\1?<query>", text)
    return TOKEN_RE.sub("<token>", text)


def tail(text: str, lines: int = TAIL) -> str:
    return "\n".join(text.splitlines()[-lines:])


def failures(trx: Path) -> dict[str, dict[str, ElementTree.Element]]:
    if not trx.is_file():
        return {}

    failed: dict[str, dict[str, ElementTree.Element]] = {}
    for result in ElementTree.parse(trx).getroot().iter(f"{report.TRX}UnitTestResult"):
        found = report.TEST_RE.match(result.get("testName", ""))
        if found and result.get("outcome") == "Failed":
            failed.setdefault(found.group(1), {})[found.group(2)] = result
    return failed


def text(result: ElementTree.Element, *path: str) -> str:
    node = result
    for tag in path:
        node = node.find(f"{report.TRX}{tag}")
        if node is None:
            return ""
    return node.text or ""


def evidence(entry: Path, formats: dict[str, ElementTree.Element], artefacts: Path) -> str:
    parts = [f"Entry {entry.name}:\n{entry.read_text()}"]
    for format, result in formats.items():
        parts += [
            f"{format} error:\n{text(result, 'Output', 'ErrorInfo', 'Message')}",
            f"{format} stack:\n{tail(text(result, 'Output', 'ErrorInfo', 'StackTrace'), 10)}",
        ]
    output = text(next(iter(formats.values())), "Output", "StdOut")
    parts.append(f"Test output:\n{tail(output)}")
    parts += [f"{log}:\n{tail(path.read_text(errors='replace'))}"
              for log in LOGS if (path := artefacts / log).is_file()]
    return redact("\n\n".join(parts))[-EVIDENCE:]


def ask(evidence: str) -> str:
    body = json.dumps({
        "model": MODEL,
        "reasoning_effort": "low",
        "max_completion_tokens": ANSWER,
        "messages": [
            {"role": "system", "content": PROMPT},
            {"role": "user", "content": evidence},
        ],
    }).encode()
    headers = {
        "Authorization": f"Bearer {os.environ['GROQ_API_KEY']}",
        "Content-Type": "application/json",
        "User-Agent": "cabinet-scenario-diagnosis",
    }

    for attempt in range(1, TRIES + 1):
        try:
            request = urllib.request.Request(ENDPOINT, data=body, headers=headers)
            with urllib.request.urlopen(request, timeout=180) as response:
                return json.load(response)["choices"][0]["message"]["content"].strip()
        except urllib.error.HTTPError as error:
            said = redact(error.read().decode(errors="replace"))
            if error.code != 429 or attempt == TRIES:
                return f"_The diagnosis could not be fetched: Groq answered {error.code} {said}_"
            time.sleep(min(float(error.headers.get("retry-after", 60)), LONGEST_WAIT))
        except (urllib.error.URLError, TimeoutError) as error:
            return f"_The diagnosis could not be fetched: {error}_"
    return ""


def main() -> int:
    artifact = Path(sys.argv[1])
    failed = failures(artifact / "TestResults" / "runtime.trx")
    if not failed:
        print("No scenario failed on its last attempt, so the run failed outside the scenarios. Its log says where.")
        return 0

    named = report.entries()
    for index, (scenario, formats) in enumerate(failed.items()):
        entry = named[scenario]
        print(f"### `{entry.stem}` ({', '.join(formats)})\n")
        if index < ENTRIES:
            print(ask(evidence(entry, formats, artifact / "scenarios" / entry.stem / "artefacts")))
        else:
            print(f"_Not diagnosed: more than {ENTRIES} entries failed, which points at Cabinet rather than a vendor._")
        print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
