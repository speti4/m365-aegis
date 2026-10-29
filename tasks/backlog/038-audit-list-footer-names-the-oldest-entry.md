---
status: idea
size: S
origin: done/006-audit-lookups-sign-ins-expiring-secrets-directory-audit.md
created: 2026-10-29
milestone: M1 Foundation
---

# `audit list` says how far the 1000-entry default reached

What: when `audit list` stops at `--limit` (1000 by default) and sets `truncated`, the footer and the envelope
name the oldest entry's time, so the reader knows which part of the window the answer covers and can
continue with `--since`.
Why: the live round of 2026-10-29 answered a busy day with 1000 entries down to 15:27 and the footer did not
say so.
Done when: a truncated answer names the oldest entry's time in the table footer and in the result, with a
fixture test over a two-page answer.
