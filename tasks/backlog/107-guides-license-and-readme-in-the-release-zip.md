---
status: ready
size: S
origin: session 2026-10-31
created: 2026-10-31
milestone: M6 Release and team
---

# The setup and usage guides, the license and a readme in the release zip

What: `scripts/release.ps1` copies `docs/setup.md` as `SETUP.md` next to `GUIDE.md`, the license as `LICENSE.txt`,
and writes a short `README.txt` (what the folder holds, the guides, the GitHub URL, how to verify the download); the
release notes footer names the guides; `docs/development.md` "The release" and `SECURITY.md` list the zip's contents.
Why: the guides live in `docs/` on GitHub first (decided 2026-10-31), and the zip must carry the same files so a
colleague with only the download has the administrator's steps too.
Done when: a local `release.ps1` run without a certificate produces a zip whose file list has the four files, and the
tests and lint checks pass.
