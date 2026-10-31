"""Holds the recorded fixtures and the repo's text to made-up data: no real tenant, name, id or token.

A recorded Graph or Exchange answer is scrubbed before it is committed (CONTRIBUTING.md, "Tenant data never
enters the repo"): every domain is contoso.com, fabrikam.com, example.com or their .onmicrosoft.com and
.sharepoint.com forms, every GUID is of the form 00000000-0000-0000-0000-0000000000NN, and nothing looks like a JWT.
This script walks tests/fixtures, tests/sidecar, tests/contracts and the snapshot folders of the test projects, and
reads the repo's prose (the docs with the usage guide, which the release zip ships, the task files, which the GitHub
issues mirror, and the community files at the root and under .github; a file git ignores, such as the maintainer's
CLAUDE.local.md, is skipped because it never enters a commit), and fails on anything else, so a real value cannot
slip in with a recording, an accepted snapshot, an example or a Verification line.

A domain is any name of two or more labels that ends in one of TOP_LEVEL_DOMAINS (task 094: `realcompany.hu` is
the common shape of a real tenant's domain). It passes only when it is one of the made-up tenants' (MADE_UP_TENANTS)
or on ALLOWED_HOSTS, which names every other host the repo may carry with a one-line reason; a new link in a doc
adds its host there, with the reason, in the same change.

It also refuses a fixture whose path from the repo root is longer than MAX_PATH_LENGTH characters: git for
Windows cannot add a path over 260 characters without core.longpaths, and a worktree under .claude/worktrees puts
about 70 characters in front of the repo's own path (`m365-aegis call GET <path> --fixture-name` prints the name a
Graph fixture is stored under).

    python -I scripts/check-fixtures.py              # exit 1 on a finding, with the file and the line
    python -I scripts/check-fixtures.py --self-test  # the rules against known cases, exit 1 on a miss
"""

from __future__ import annotations

import re
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
# The fixtures: every text file is held to the rules, and every file to the path length.
FIXTURE_FOLDERS = [
    ROOT / "tests" / "fixtures",
    ROOT / "tests" / "sidecar",
    ROOT / "tests" / "contracts",
    # The accepted snapshots of the Verify tests render the fixtures, so they are held to the same rules.
    *sorted((ROOT / "tests").glob("M365Aegis.*.Tests/Snapshots")),
]
TEXT_SUFFIXES = {".json", ".headers", ".txt", ".md", ".ps1", ".psm1", ".psd1", ".csv"}
# The prose: every Markdown file under these folders, and the Markdown files at the root (task 094). The usage
# guide (docs/usage.md, task 086) is written over the fixtures' names; the task files are the GitHub issues' text.
PROSE_FOLDERS = [ROOT / "docs", ROOT / "tasks", ROOT / ".github"]

# The top-level domains a host is recognized by. A real tenant's domain ends in one of these far more often than
# not; a file name (`me.json`, `run.ps1`) or a code identifier ends in none of them.
TOP_LEVEL_DOMAINS = ("com", "net", "org", "hu", "eu", "de", "at", "ch", "uk", "io", "dev", "info", "biz", "co", "ai")
ANY_DOMAIN = re.compile(r"\b(?:[a-z0-9-]+\.)+(?:" + "|".join(TOP_LEVEL_DOMAINS) + r")\b", re.IGNORECASE)

# The made-up tenants: their .com domain with any subdomain (mail01.contoso.com), their .onmicrosoft.com domain
# with any subdomain, and their SharePoint hosts (contoso.sharepoint.com, contoso-my.sharepoint.com).
MADE_UP_TENANTS = {
    "contoso": "Microsoft's documented example company: the tenant every fixture and example belongs to",
    "fabrikam": "Microsoft's second example company: the foreign addresses (forwarding, guests, other tenants)",
    "example": "reserved for examples by RFC 2606",
}
# A URL escape (`%40` for the at sign of a user principal name in a fixture's file name) is not part of a host.
URL_ESCAPE = re.compile(r"%[0-9a-f]{2}", re.IGNORECASE)
MADE_UP_DOMAIN = re.compile(
    r"(?:[a-z0-9-]+\.)*(?:" + "|".join(MADE_UP_TENANTS) + r")(?:\.onmicrosoft|(?:-my)?\.sharepoint)?\.com",
    re.IGNORECASE,
)

# Every other host the repo may carry, each with the reason. `*.name` is the name and any subdomain of it (an owner
# that hands out no tenant names under it); a bare name is that host alone. `onmicrosoft.com`, `sharepoint.com` and
# `outlook.com` stay bare: a tenant's own name sits under each of them.
ALLOWED_HOSTS = {
    # Microsoft's endpoints and documentation.
    "*.microsoft.com": "Microsoft's own hosts: Graph, the sign-in, Learn, the container registry, the blogs",
    "*.microsoftonline.com": "the Entra sign-in endpoint (login.microsoftonline.com)",
    "*.office365.com": "the Exchange Online endpoint (outlook.office365.com)",
    "onmicrosoft.com": "the name of the tenants' initial domain, written alone in the docs",
    "sharepoint.com": "the name of the tenants' SharePoint hosts, written alone in the docs",
    "outlook.com": "the name of Exchange Online's hosts, written alone in the docs",
    "*.digicert.com": "the timestamp server of the release signature (timestamp.digicert.com)",
    # The package registries and the libraries' homes.
    "*.github.com": "the repository, its issues and releases, the actions, the projects' homes",
    "*.nuget.org": "the NuGet registry and its API",
    "*.powershellgallery.com": "the PowerShell Gallery, where ExchangeOnlineManagement and Pester come from",
    "crates.io": "the Rust registry, named in the research of the stack",
    "pkg.go.dev": "the Go registry, named in the research of the stack",
    "msal.net": "the name of the .NET authentication library (MSAL.NET), not a host",
    "photino.net": "the name of the window library the stack research weighed (Photino.NET), not a host",
    "*.contributor-covenant.org": "the code of conduct's source",
    "img.shields.io": "the README's release and license badges",
    # The research's sources (docs/research): the tools and articles the product was compared with.
    "michev.info": "Vasil Michev's blog, an Exchange Online and Graph reference",
    "admindroid.com": "a Microsoft 365 reporting product, compared in the research",
    "*.manageengine.com": "a Microsoft 365 management product, compared in the research",
    "*.syskit.com": "a Microsoft 365 governance product, compared in the research",
    "*.coreview.com": "a Microsoft 365 management product, compared in the research",
    "sharegate.com": "a Microsoft 365 migration and governance product, compared in the research",
    "*.hornetsecurity.com": "a Microsoft 365 backup and governance product, compared in the research",
    # Code that has the shape of a host.
    "import.meta.env.dev": "Vite's development flag, quoted as code",
    "system.io": "the .NET namespace, in the sidecar tests' PowerShell",
}

GUID = re.compile(r"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", re.IGNORECASE)
MADE_UP_GUID = re.compile(r"^00000000-0000-0000-0000-0000000000[0-9a-f]{2}$", re.IGNORECASE)
# GUIDs that are nothing's identity in a tenant, each with the reason.
ALLOWED_GUIDS = {
    "0f2a7c3e-6b1d-4b2e-9d4a-000000000001": "the fake module's manifest GUID, which PowerShell requires",
    "8bd644d1-64a1-4d4b-ae52-2e0cbf64e373": "Microsoft's message trace application, the same id in every tenant",
    "00000003-0000-0000-c000-000000000000": "Microsoft Graph's application, the same id in every tenant (docs/setup.md)",
    "00000002-0000-0ff1-ce00-000000000000": "Office 365 Exchange Online's application, the same id in every tenant",
}
JWT = re.compile(r"\beyJ[A-Za-z0-9_-]{10,}")
# The longest path from the repo root a fixture may have; FixtureGraphTransport.MaxRepoPathLength is the same.
MAX_PATH_LENGTH = 190


def allowed_host(host: str) -> bool:
    host = host.lower()
    if MADE_UP_DOMAIN.fullmatch(host):
        return True
    for entry in ALLOWED_HOSTS:
        if entry.startswith("*."):
            if host == entry[2:] or host.endswith(entry[1:]):
                return True
        elif host == entry:
            return True
    return False


def line_findings(line: str) -> list[str]:
    out: list[str] = []
    for match in ANY_DOMAIN.finditer(URL_ESCAPE.sub(" ", line)):
        if not allowed_host(match.group(0)):
            out.append(f"domain that is not made up or allowed: {match.group(0)}")
    for match in GUID.finditer(line):
        guid = match.group(0)
        if not MADE_UP_GUID.match(guid) and guid.lower() not in ALLOWED_GUIDS:
            out.append(f"GUID that is not of the made-up form: {guid}")
    if JWT.search(line):
        out.append("looks like a JWT")
    return out


def findings(path: Path, root: Path = ROOT) -> list[str]:
    text = path.read_text(encoding="utf-8", errors="replace")
    relative = path.relative_to(root).as_posix()
    return [
        f"{relative}:{number}: {finding}"
        for number, line in enumerate(text.splitlines(), start=1)
        for finding in line_findings(line)
    ]


def too_long(path: Path) -> str | None:
    relative = path.relative_to(ROOT).as_posix()
    if len(relative) <= MAX_PATH_LENGTH:
        return None
    return (
        f"{relative}: the path is {len(relative)} characters from the repo root, over the limit of {MAX_PATH_LENGTH}"
        " (git for Windows refuses it in a worktree); store a Graph fixture in its short form, which"
        " `m365-aegis call GET <path> --fixture-name` prints"
    )


def prose_files() -> list[Path]:
    paths = sorted(ROOT.glob("*.md"))
    for folder in PROSE_FOLDERS:
        paths += sorted(path for path in folder.rglob("*.md") if path.is_file())
    return without_git_ignored(paths)


def without_git_ignored(paths: list[Path]) -> list[Path]:
    """The paths git does not ignore: a gitignored file (`CLAUDE.local.md`, the maintainer's tenant notes) never
    enters a commit, so it is not the scan's to judge; without git on the PATH every path is kept."""
    if not paths:
        return paths
    relative = [path.relative_to(ROOT).as_posix() for path in paths]
    try:
        # NUL-separated both ways (-z) and bytes, not text: a text pipe on Windows turns "\n" into "\r\n" and git
        # then looks for a path with a trailing carriage return, which is never ignored.
        result = subprocess.run(
            ["git", "-C", str(ROOT), "check-ignore", "--stdin", "-z"],
            input=b"".join(name.encode("utf-8") + b"\0" for name in relative),
            capture_output=True,
            check=False,
        )
    except OSError:
        return paths
    # Exit code 0: some paths are ignored (listed on stdout); 1: none; anything else: git could not tell.
    if result.returncode not in (0, 1):
        return paths
    ignored = {name.decode("utf-8") for name in result.stdout.split(b"\0") if name}
    return [path for path, name in zip(paths, relative, strict=True) if name not in ignored]


# Each case is a line and whether the rules flag it: the self-test fails when one of them is judged otherwise.
SELF_TEST_CASES = [
    ("alice@realcompany.com", True),
    ("userPrincipalName eq 'alice%40realcompany.com'", True),
    ("the tenant realcompany.hu", True),
    ("https://realcompany.sharepoint.com/sites/x", True),
    ("realcompany.onmicrosoft.com", True),
    ("realcompany-hu.mail.protection.outlook.com", True),
    ("graph.microsoft.com.realcompany.com", True),
    ("evilmicrosoft.com", True),
    ("12345678-1234-1234-1234-123456789abc", True),
    ("Bearer eyJ0eXAiOiJKV1QiLCJhbGciOi", True),
    ("alice@contoso.com and bob@fabrikam.com", False),
    ("userPrincipalName eq 'alice%40contoso.com'", False),
    ("mail01.contoso.com, contoso-my.sharepoint.com, refuse.contoso.onmicrosoft.com", False),
    ("https://graph.microsoft.com/v1.0/me and https://github.com/speti4/m365-aegis", False),
    ("00000000-0000-0000-0000-00000000002a", False),
    ("me.json, run.ps1, System.IO and README.md", False),
]


def self_test() -> int:
    misses = [
        f"self-test: {line!r} is {'passed' if expected else 'flagged'} by the rules, expected the opposite"
        for line, expected in SELF_TEST_CASES
        if bool(line_findings(line)) != expected
    ]
    # A recorded answer with a two-label domain in it fails as a file too, with its name and its line.
    with tempfile.TemporaryDirectory() as folder:
        fixture = Path(folder) / "me.json"
        fixture.write_text('{\n  "mail": "alice@realcompany.hu"\n}\n', encoding="utf-8")
        found = findings(fixture, Path(folder))
        if found != ["me.json:2: domain that is not made up or allowed: realcompany.hu"]:
            misses.append(f"self-test: a fixture with a two-label domain gave {found}")
    for miss in misses:
        print(miss)
    print(f"check-fixtures --self-test: {len(SELF_TEST_CASES) + 1} cases, {len(misses)} misses")
    return 1 if misses else 0


def main(argv: list[str]) -> int:
    if argv == ["--self-test"]:
        return self_test()
    if argv:
        print("usage: check-fixtures.py [--self-test]")
        return 2
    problems: list[str] = []
    files = 0
    for folder in FIXTURE_FOLDERS:
        if not folder.is_dir():
            continue
        for path in sorted(folder.rglob("*")):
            if not path.is_file():
                continue
            if (problem := too_long(path)) is not None:
                problems.append(problem)
            if path.suffix.lower() in TEXT_SUFFIXES:
                files += 1
                problems += findings(path)
    for path in prose_files():
        files += 1
        problems += findings(path)
    for problem in problems:
        print(problem)
    print(f"check-fixtures: {files} files, {len(problems)} findings")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
