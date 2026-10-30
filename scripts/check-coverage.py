"""The coverage gate of the Core and the command line (task 070).

Reads every coverlet JSON report under the given folders or files (the Core tests and the Cli tests each write one,
and both measure the Core), merges them (a line or a branch is covered when any report hit it), prints the files
that miss a line or a branch with the line numbers, and exits 1 when the lines or the branches of an assembly are
under the thresholds (100 and 100 by default). With --summary it also appends a Markdown table to a file (the
job summary of CI).

    python -I scripts/check-coverage.py TestResults/coverage
    python -I scripts/check-coverage.py TestResults/coverage --line 100 --branch 100 --summary "$GITHUB_STEP_SUMMARY"
"""

from __future__ import annotations

import argparse
import json
import sys
from collections import defaultdict
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent


def reports(paths: list[str]) -> list[Path]:
    """The JSON reports under the given folders (coverage*.json) or the given files."""
    found: list[Path] = []
    for name in paths:
        path = Path(name)
        if path.is_dir():
            found.extend(sorted(path.rglob("*coverage*.json")))
        elif path.is_file():
            found.append(path)
    return found


def relative(file: str) -> str:
    """A source path relative to the repo (the reports hold the absolute path of the machine that ran them)."""
    normalized = file.replace("\\", "/")
    for marker in ("/src/", "/tests/"):
        index = normalized.find(marker)
        if index >= 0:
            return normalized[index + 1 :]
    return normalized


def merge(files: list[Path]):
    """{assembly: {file: (lines {(method, line): hits}, branches {(method, line, offset, end, path, ordinal): hits})}}."""
    merged: dict[str, dict[str, tuple[dict, dict]]] = defaultdict(lambda: defaultdict(lambda: ({}, {})))
    for report in files:
        data = json.loads(report.read_text(encoding="utf-8"))
        for assembly, sources in data.items():
            for source, classes in sources.items():
                lines, branches = merged[assembly][relative(source)]
                for methods in classes.values():
                    for method, body in methods.items():
                        for line, hits in body.get("Lines", {}).items():
                            key = (method, int(line))
                            lines[key] = lines.get(key, 0) + hits
                        for branch in body.get("Branches", []):
                            key = (method, branch["Line"], branch["Offset"], branch["EndOffset"], branch["Path"], branch["Ordinal"])
                            branches[key] = branches.get(key, 0) + branch["Hits"]
    return merged


def percent(covered: int, total: int) -> float:
    return 100.0 if total == 0 else covered * 100.0 / total


def ranges(numbers: list[int]) -> str:
    """1,2,3,7 as 1-3,7."""
    spans: list[list[int]] = []
    for number in sorted(set(numbers)):
        if spans and spans[-1][1] + 1 == number:
            spans[-1][1] = number
        else:
            spans.append([number, number])
    return ",".join(str(first) if first == last else f"{first}-{last}" for first, last in spans)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("paths", nargs="+", help="folders with coverlet JSON reports, or the reports")
    parser.add_argument("--line", type=float, default=100.0, help="the line threshold of every assembly (percent)")
    parser.add_argument("--branch", type=float, default=100.0, help="the branch threshold of every assembly (percent)")
    parser.add_argument("--summary", help="a Markdown file to append the table to")
    arguments = parser.parse_args()

    files = reports(arguments.paths)
    if not files:
        print(f"no coverlet JSON report under {', '.join(arguments.paths)}", file=sys.stderr)
        return 1

    merged = merge(files)
    failed = False
    table = ["| assembly | lines | branches |", "| --- | --- | --- |"]
    print(f"{len(files)} report(s): {', '.join(str(file) for file in files)}")
    for assembly in sorted(merged):
        line_total = line_hit = branch_total = branch_hit = 0
        gaps: list[str] = []
        for source in sorted(merged[assembly]):
            lines, branches = merged[assembly][source]
            missed_lines = [line for (_, line), hits in lines.items() if hits == 0]
            missed_branches = [key[1] for key, hits in branches.items() if hits == 0]
            line_total += len(lines)
            line_hit += len(lines) - len(missed_lines)
            branch_total += len(branches)
            branch_hit += len(branches) - len(missed_branches)
            if missed_lines or missed_branches:
                detail = []
                if missed_lines:
                    detail.append(f"lines {ranges(missed_lines)}")
                if missed_branches:
                    detail.append(f"branches on {ranges(missed_branches)}")
                gaps.append(f"  {source}: {'; '.join(detail)}")
        line_percent = percent(line_hit, line_total)
        branch_percent = percent(branch_hit, branch_total)
        below = line_percent < arguments.line or branch_percent < arguments.branch
        failed = failed or below
        print(
            f"{assembly}: lines {line_hit}/{line_total} ({line_percent:.2f}%), "
            f"branches {branch_hit}/{branch_total} ({branch_percent:.2f}%)"
            + ("  BELOW THE THRESHOLD" if below else "")
        )
        for gap in gaps:
            print(gap)
        table.append(f"| {assembly} | {line_hit}/{line_total} ({line_percent:.2f}%) | {branch_hit}/{branch_total} ({branch_percent:.2f}%) |")

    if arguments.summary:
        with open(arguments.summary, "a", encoding="utf-8") as summary:
            summary.write("### Coverage of the Core and the command line\n\n" + "\n".join(table) + "\n\n")
    if failed:
        print(f"the thresholds are {arguments.line:g}% of the lines and {arguments.branch:g}% of the branches", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
