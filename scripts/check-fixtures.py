"""Holds the recorded fixtures to made-up data: no real tenant, name, id or token under tests/fixtures.

A recorded Graph or Exchange answer is scrubbed before it is committed (CONTRIBUTING.md, "Tenant data never
enters the repo"): every domain is contoso.com, fabrikam.com, example.com or their .onmicrosoft.com forms,
every GUID is of the form 00000000-0000-0000-0000-0000000000NN, and nothing looks like a JWT. This script walks
tests/fixtures and tests/sidecar and fails on anything else, so a real value cannot slip in with a recording.

    python -I scripts/check-fixtures.py            # exit 1 on a finding, with the file and the line
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
FOLDERS = [ROOT / "tests" / "fixtures", ROOT / "tests" / "sidecar", ROOT / "tests" / "contracts"]
TEXT_SUFFIXES = {".json", ".headers", ".txt", ".md", ".ps1", ".psm1", ".psd1", ".csv"}

ALLOWED_DOMAINS = re.compile(
    r"\b(?:[a-z0-9-]+\.)*(?:contoso|fabrikam|example)(?:\.onmicrosoft)?\.com\b", re.IGNORECASE
)
ANY_DOMAIN = re.compile(r"\b[a-z0-9-]+(?:\.[a-z0-9-]+)+\.(?:com|net|org|hu|io|dev|microsoft\.com)\b", re.IGNORECASE)
# Hosts of the services themselves are fine in a URL-shaped fixture path or header.
SERVICE_HOSTS = re.compile(
    r"\b(?:graph\.microsoft\.com|login\.microsoftonline\.com|outlook\.office365\.com|learn\.microsoft\.com|"
    r"github\.com|www\.powershellgallery\.com|ui\.shadcn\.com)\b",
    re.IGNORECASE,
)
GUID = re.compile(r"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", re.IGNORECASE)
MADE_UP_GUID = re.compile(r"^00000000-0000-0000-0000-0000000000[0-9a-f]{2}$", re.IGNORECASE)
# The fake module's own manifest GUID, which PowerShell requires and which is nothing's identity.
MANIFEST_GUIDS = {"0f2a7c3e-6b1d-4b2e-9d4a-000000000001"}
JWT = re.compile(r"\beyJ[A-Za-z0-9_-]{10,}")


def findings(path: Path) -> list[str]:
    out: list[str] = []
    for number, line in enumerate(path.read_text(encoding="utf-8", errors="replace").splitlines(), start=1):
        for match in ANY_DOMAIN.finditer(line):
            host = match.group(0)
            if ALLOWED_DOMAINS.fullmatch(host) or SERVICE_HOSTS.search(host):
                continue
            out.append(f"{path.relative_to(ROOT)}:{number}: domain that is not made up: {host}")
        for match in GUID.finditer(line):
            guid = match.group(0)
            if MADE_UP_GUID.match(guid) or guid.lower() in MANIFEST_GUIDS:
                continue
            out.append(f"{path.relative_to(ROOT)}:{number}: GUID that is not of the made-up form: {guid}")
        if JWT.search(line):
            out.append(f"{path.relative_to(ROOT)}:{number}: looks like a JWT")
    return out


def main() -> int:
    problems: list[str] = []
    files = 0
    for folder in FOLDERS:
        if not folder.is_dir():
            continue
        for path in sorted(folder.rglob("*")):
            if path.is_file() and path.suffix.lower() in TEXT_SUFFIXES:
                files += 1
                problems += findings(path)
    for problem in problems:
        print(problem)
    print(f"check-fixtures: {files} files, {len(problems)} findings")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
