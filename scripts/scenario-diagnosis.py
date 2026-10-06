#!/usr/bin/env python3
"""Ask a language model why the plugin scenarios failed, and print its answer as Markdown.

Reads the runtime artifact the Plugins workflow uploads: the .trx of the last attempt, and each
scenario's artefacts. Every scenario that still failed on its last attempt contributes its
error, its probes' own logs, the tail of its install log and its catalogue entry, with Wine's
and the bridge's routine chatter dropped. Every secret the run held, every URL query and every
long token is redacted before anything leaves the runner.

Entries that ended on the same exception share one cause, almost always in Cabinet or its
probes rather than in any vendor, so they are diagnosed once, together.

It is shaped for Groq's free tier, which allows gpt-oss-120b 8,000 tokens a minute: each cause
is its own request, and the evidence and the answer together stay under that limit. A rate
limit is waited out as Groq's retry-after asks. Past REQUESTS causes the rest are only listed.
An answer the API refuses becomes the reason in the printed Markdown, so the report still goes
out. Under every answer sit the failed tests and the error lines as the run recorded them,
which no model has touched.

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
from dataclasses import dataclass, field
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
report = __import__("scenario-report")

ENDPOINT = "https://api.groq.com/openai/v1/chat/completions"
MODEL = "openai/gpt-oss-120b"
SECRETS = ("EMAIL", "PASSWORD", "USERNAME", "DROPBOX", "NATIVE_ACCESS", "WAVES", "GROQ_API_KEY")
EVIDENCE = 12000
PART = 3000
ANSWER = 3000
REQUESTS = 6
TRIES = 3
LONGEST_WAIT = 90
TAIL = 40
EXCERPT = 12
FIELDS = ("Name", "Kind", "Version", "Formats", "Source", "Url", "DemoUrl", "Script", "Runner", "Prefix")

QUERY_RE = re.compile(r"(https?://[^\s?\"']+)\?[^\s\"']*")
TOKEN_RE = re.compile(r"[A-Za-z0-9+_=-]{40,}")
NOISE_RE = re.compile(r"fixme:|DEBUG:|\[host (->|<-) plugin\]|\[carla\] TODO|Unhandled X11 event")
EXCEPTION_RE = re.compile(r"^\s*([\w.]*(?:Error|Exception)\b.*)$", re.MULTILINE)

PROMPT = """You diagnose a failed plugin scenario of Cabinet, a Flatpak that installs Windows audio
plugins into one Wine prefix per vendor and bridges them to Linux DAWs with yabridge; native
Linux entries skip Wine. A scenario installs one catalogue entry from its download into a fresh
home, bridges it, renders audio through Carla, and runs probes: the editor probe opens the
editor, requires more than one colour and clicks and drags in it to see it respond.
Causes, in the order to suspect them: Cabinet or its probes (a Python traceback from a probe, a
failed C# assertion), a moved or changed vendor download or installer, the Wine runner or the
prefix, a flaky network or display. Wine's err:ole, RpcSs, wbemprox and win32k.sys lines appear in
passing runs too and prove nothing on their own.
Say only what went wrong and exactly where: the step that failed, the file and line or the
component, and the lines that show it, quoted. Do not suggest a fix or a next step. Say so when
the evidence is not enough. Answer in at most fifteen lines of Markdown with no heading."""


def redact(text: str) -> str:
    for name in SECRETS:
        value = os.environ.get(name, "")
        if len(value) >= 4:
            text = text.replace(value, f"<{name}>")
    text = QUERY_RE.sub(r"\1?<query>", text)
    return TOKEN_RE.sub("<token>", text)


def quiet(text: str, lines: int = TAIL) -> str:
    kept = [line for line in text.splitlines() if line.strip() and not NOISE_RE.search(line)]
    return "\n".join(kept[-lines:])[-PART:]


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


def probes(artefacts: Path, format: str) -> list[Path]:
    return sorted(log for log in artefacts.glob("*/*/probe.log")
                  if log.parent.name.lower().startswith(format.lower()))


def evidence(entry: Path, formats: dict[str, ElementTree.Element], artefacts: Path) -> str:
    parts = []
    for format, result in formats.items():
        parts.append(f"{format} error:\n{quiet(text(result, 'Output', 'ErrorInfo', 'Message'))}")
        parts.append(f"{format} stack:\n{quiet(text(result, 'Output', 'ErrorInfo', 'StackTrace'), 10)}")
        parts += [f"{log.relative_to(artefacts)}:\n{quiet(log.read_text(errors='replace').split('yabridge:')[0])}"
                  for log in probes(artefacts, format)[:2]]
    install = artefacts / "install.log"
    if install.is_file():
        parts.append(f"install.log:\n{quiet(install.read_text(errors='replace'), 25)}")
    fields = [line for line in entry.read_text().splitlines() if line.split(":")[0] in FIELDS]
    parts.append(f"Entry {entry.name}:\n" + "\n".join(fields))
    return redact("\n\n".join(parts))[:EVIDENCE]


def signature(evidence: str) -> str:
    found = EXCEPTION_RE.findall(evidence)
    return found[-1].strip() if found else ""


def ask(evidence: str) -> str:
    body = json.dumps({
        "model": MODEL,
        "reasoning_effort": "medium",
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


@dataclass
class Cause:
    exception: str
    evidence: str
    labels: list[str] = field(default_factory=list)
    tests: list[str] = field(default_factory=list)

    @property
    def title(self) -> str:
        if len(self.labels) == 1:
            return self.labels[0]
        return f"One cause in {len(self.labels)} entries: {', '.join(self.labels)}"

    @property
    def question(self) -> str:
        if len(self.labels) == 1:
            return self.evidence
        return (f"The same exception ended {len(self.labels)} entries: {', '.join(self.labels)}.\n"
                f"Exception: {self.exception}\nEvidence from the first of them:\n\n{self.evidence}")

    @property
    def recorded(self) -> str:
        lines = [line.rstrip() for line in self.evidence.splitlines()]
        stripped = [line.strip() for line in lines]
        end = len(lines) - stripped[::-1].index(self.exception) if self.exception in stripped else EXCERPT
        quoted = "\n".join(lines[max(0, end - EXCERPT):end]).replace("```", "'''")
        tests = "\n".join(f"- `{test}`" for test in self.tests)
        return f"<details><summary>As recorded</summary>\n\n{tests}\n\n```text\n{quoted}\n```\n</details>"


def causes(artifact: Path, failed: dict[str, dict[str, ElementTree.Element]]) -> list[Cause]:
    named = report.entries()
    grouped: dict[str, Cause] = {}
    for scenario, formats in failed.items():
        entry = named[scenario]
        found = evidence(entry, formats, artifact / "scenarios" / entry.stem / "artefacts")
        label = f"`{entry.stem}` ({', '.join(formats)})"
        exception = signature(found)
        cause = grouped.setdefault(exception or label, Cause(exception, found))
        cause.labels.append(label)
        cause.tests += [result.get("testName", "").removeprefix(report.NAMESPACE) for result in formats.values()]
    return sorted(grouped.values(), key=lambda cause: len(cause.labels), reverse=True)


def main() -> int:
    artifact = Path(sys.argv[1])
    failed = failures(artifact / "TestResults" / "runtime.trx")
    if not failed:
        print("No scenario failed on its last attempt, so the run failed outside the scenarios. Its log says where.")
        return 0

    for index, cause in enumerate(causes(artifact, failed)):
        print(f"### {cause.title}\n")
        if index < REQUESTS:
            print(ask(cause.question))
        else:
            print(f"_Not diagnosed: more than {REQUESTS} separate causes, which points at Cabinet rather than a vendor._")
        print(f"\n{cause.recorded}\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
