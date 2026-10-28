"""Fails when a NuGet package of the solution, transitive ones included, has a known vulnerability.

`dotnet package list --vulnerable` prints the advisories but exits 0 either way, so CI needs this wrapper: it runs
the command with --format json over the solution and exits 1 when any project lists a vulnerable package. The
restore's NuGetAudit (Directory.Build.props) catches the same advisories at build time; this is the explicit,
scheduled check (ci.yml `deps`, advisories.yml) that also prints what it found.

    python -I scripts/check-vulnerable.py           # exit 1 on a finding
"""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SOLUTION = ROOT / "M365Aegis.slnx"


def main() -> int:
    command = [
        "dotnet", "package", "list", "--project", str(SOLUTION),
        "--vulnerable", "--include-transitive", "--format", "json",
    ]
    completed = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", cwd=ROOT, check=False)
    if completed.returncode != 0:
        print(completed.stdout)
        print(completed.stderr, file=sys.stderr)
        print(f"check-vulnerable: dotnet exited {completed.returncode}")
        return 1
    # The output may carry a non-JSON preamble (restore messages); the report starts at the first brace.
    text = completed.stdout
    start = text.find("{")
    report = json.loads(text[start:]) if start >= 0 else {}
    findings: list[str] = []
    for project in report.get("projects", []):
        for framework in project.get("frameworks", []):
            for kind in ("topLevelPackages", "transitivePackages"):
                for package in framework.get(kind, []):
                    for advisory in package.get("vulnerabilities", []):
                        findings.append(
                            f"{Path(project.get('path', '?')).name}: {package.get('id')} {package.get('resolvedVersion')} "
                            f"{advisory.get('severity')} {advisory.get('advisoryurl')}"
                        )
    for finding in findings:
        print(finding)
    print(f"check-vulnerable: {len(findings)} vulnerable packages")
    return 1 if findings else 0


if __name__ == "__main__":
    sys.exit(main())
