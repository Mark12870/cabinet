#!/usr/bin/env python3
"""Report each plugin scenario's result against the catalogue entry it names.

Reads the runtime suite's .trx and writes one row per entry and format to the job summary.
A failing format becomes an error annotation on the entry's .yml, so the Actions run page
points at the entry to look at. Exit status is 0: the test step has already failed the job.

    scripts/scenario-report.py runtime/TestResults/runtime.trx
"""

from __future__ import annotations

import os
import re
import sys
import xml.etree.ElementTree as ElementTree
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SCENARIOS = REPO / "tests/Cabinet.Runtime.Tests/Scenarios"
LIBRARY = REPO / "data/library"
NAMESPACE = "Cabinet.Runtime.Tests.Scenarios."
TRX = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"

ID_RE = re.compile(r'const string Id = "([^"]+)";')
TEST_RE = re.compile(r'^' + re.escape(NAMESPACE) + r'(\w+)\.\w+\(format: "([^"]+)"\)$')


def field(entry: Path, name: str) -> str:
    found = re.search(rf"^{name}: *(.+)$", entry.read_text(), re.MULTILINE)
    return found.group(1).strip() if found else ""


def entries() -> dict[str, Path]:
    named = {}
    for scenario in sorted(SCENARIOS.glob("*Scenario.cs")):
        found = ID_RE.search(scenario.read_text())
        if found:
            named[scenario.stem] = next(LIBRARY.glob(f"*/{found.group(1)}.yml"))
    return named


def results(trx: Path) -> dict[tuple[str, str], str]:
    if not trx.is_file():
        return {}

    outcomes = {}
    for result in ElementTree.parse(trx).getroot().iter(f"{TRX}UnitTestResult"):
        found = TEST_RE.match(result.get("testName", ""))
        if found:
            outcomes[(found.group(1), found.group(2))] = result.get("outcome", "Unknown")
    return outcomes


def main() -> int:
    trx = Path(sys.argv[1])
    named = entries()
    outcomes = results(trx)

    rows = []
    for scenario, entry in named.items():
        formats = [value.strip() for value in field(entry, "Formats").split(",") if value.strip()]
        for format in formats:
            outcome = outcomes.get((scenario, format), "Not run")
            rows.append((entry, format, outcome))
            if outcome == "Failed":
                path = entry.relative_to(REPO)
                print(
                    f"::error file={path},title=Plugin scenario failed::"
                    f"{field(entry, 'Name')} {format} failed its end-to-end scenario"
                )

    lines = ["### Plugin scenario results", ""]
    if not outcomes:
        lines += [f"No scenario results in `{trx}`.", ""]
    lines += ["| Entry | Version | Format | Result |", "| --- | --- | --- | --- |"]
    lines += [
        f"| `{entry.stem}` | {field(entry, 'Version') or 'rolling'} | {format} | {outcome} |"
        for entry, format, outcome in rows
    ]
    report = "\n".join(lines) + "\n"

    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as handle:
            handle.write(report)
    else:
        print(report, end="")
    return 0


if __name__ == "__main__":
    sys.exit(main())
