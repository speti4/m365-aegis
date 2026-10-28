"""The licenses of the front end's packages against licenses/allowed.json, from the lock file.

npm's lock file (lockfileVersion 3) records each package's `license`, so no extra tool is needed: this script
reads gui/web/package-lock.json, checks every runtime package (the dev-only ones never reach the shipped page)
against licenses/allowed.json and prints or appends the notices for the shipped ones.

    python -I scripts/npm-licenses.py                       # exit 1 on a license outside the allowlist
    python -I scripts/npm-licenses.py --notices out.md      # append a "## Front end" section with the packages
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
LOCK = ROOT / "gui" / "web" / "package-lock.json"
ALLOWED = ROOT / "licenses" / "allowed.json"


def shipped_packages(lock: dict) -> dict[str, dict]:
    """The packages that are not dev-only, by name, from the lock file's `packages` map."""
    out: dict[str, dict] = {}
    for key, entry in lock.get("packages", {}).items():
        if not key or entry.get("dev"):
            continue
        name = key.rsplit("node_modules/", 1)[-1]
        out[name] = entry
    return out


def license_ok(expression: str, allowed: set[str]) -> bool:
    """Every identifier of an SPDX expression is on the allowlist (`(MIT OR Apache-2.0)` passes when one is)."""
    identifiers = [token for token in re.split(r"[\s()]+", expression) if token and token not in {"OR", "AND", "WITH"}]
    if not identifiers:
        return False
    if " OR " in expression.upper():
        return any(identifier in allowed for identifier in identifiers)
    return all(identifier in allowed for identifier in identifiers)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--notices", type=Path, help="append the front end's notices section to this file")
    args = parser.parse_args()

    allowed = set(json.loads(ALLOWED.read_text(encoding="utf-8")))
    lock = json.loads(LOCK.read_text(encoding="utf-8"))
    packages = shipped_packages(lock)
    problems = []
    rows = []
    for name, entry in sorted(packages.items()):
        expression = entry.get("license") or "UNKNOWN"
        version = entry.get("version", "?")
        rows.append((name, version, expression))
        if not license_ok(expression, allowed):
            problems.append(f"{name}@{version}: license '{expression}' is not on licenses/allowed.json")
    for problem in problems:
        print(problem)
    print(f"npm-licenses: {len(packages)} shipped packages, {len(problems)} outside the allowlist")

    if args.notices is not None and not problems:
        lines = ["", "## Front end (gui/web)", "", "| Package | Version | License |", "|---|---|---|"]
        lines += [f"| {name} | {version} | {expression} |" for name, version, expression in rows]
        with args.notices.open("a", encoding="utf-8") as handle:
            handle.write("\n".join(lines) + "\n")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
