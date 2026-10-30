---
size: M
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M3 Writes with safety
---

# 052 — Group membership and owner writes behind a plan

## Context

`group members add|remove <group> <user…>` and `group owners add|remove <group> <user…>` through Graph
(`POST /groups/{id}/members/$ref`, `DELETE /groups/{id}/members/{id}/$ref`, the owners alike), each a `Plan` of
task 051: the plan lists only what is not already so (a member already present is unchanged), warns when the last
owner would go, and the apply reports per change; recorded POST and DELETE answers with their `.headers` files
under `tests/fixtures`.
Why: the first write of M3 on the Graph side; group membership is the support team's most common change (D-001).
Done when: `group members add --dry-run` from fixtures prints a plan with one change and one unchanged member,
`--yes` applies it from the recorded answers, the last-owner warning shows, and the live `--dry-run` against the
tenant lists the real current state without applying anything.
Part of the M3 writes bundle (051, 052, 053, 054) of the round of 2026-10-30; builds on 051. The live `--dry-run`
runs in the main session's signed-in round; nothing is applied in the tenant this round (decided 2026-10-30).

## Docs that move

- `docs/development.md`: the group writes in the groups domain and in "Writes and plans"
- `README.md`: the commands under the bundle's own heading
- `tests/fixtures/README.md`: the recorded write answers (POST, DELETE, `.headers`), if the naming needs a line

## Not in this change

- a group's creation or deletion, Teams or SharePoint writes
- the CSV batch over these writes (054)

## Steps

1. [x] The plan: the current members or owners (the lookup of task 005), the users to add resolved through `/users`
   or `/directoryObjects` in `$batch`, the diff, the last-owner and the Graph-cannot-change warnings
2. [x] The apply through Graph (`$ref` POST and DELETE), per-change results, the plan record
3. [x] The four commands with `--dry-run` and `--yes`; child-process tests and the table snapshot
4. [x] Recorded write answers in the fixtures, scrubbed; Core tests over the fixture transport
5. [x] Docs

## Log

- 2026-10-30 — promoted by the main session into the M3 writes bundle (051, 052, 053, 054)
- 2026-10-30 — `Groups/GroupWrites.cs`: `PlanAsync(graph, Members|Owners, add, rows)` resolves each group once
  (`GroupLookups.ResolveAsync`), reads its list once (`GroupLookups.ListAsync`, made internal), resolves only the
  users to add not found on the list (a user principal name through `/users/{upn}?$select=…`, an object id through
  `/directoryObjects/{id}`, one `$batch`), and fails with exit code 4 before anything is sent for a user that does
  not exist; a removal of someone not on the list is unchanged without a read; a user named twice is planned once.
  The steps are `POST /groups/{id}/{members|owners}/$ref` with the `directoryObjects` reference and
  `DELETE …/{id}/$ref`, with `GroupMember.ReadWrite.All` (members) and `Group.ReadWrite.All` (owners) as the
  permission a 403 names (checked against the Graph reference "Add owners" on 2026-10-30).
- 2026-10-30 — warnings: the last owner going; a distribution or mail-enabled security group's members, a group
  Graph keeps no owners for, a synced group (the service will refuse the change).
- 2026-10-30 — `group members add|remove <group> <user…>` and `group owners add|remove <group> <user…>` with
  `--from-csv` (task 054), `--dry-run` and `--yes`.
- 2026-10-30 — fixtures: `FixtureGraphTransport.RelativePath(uri, body)` names a `$ref` POST by the id its body's
  `@odata.id` points at (`members/$ref@@odata.id=<id>`); recorded: Dave's add 204, Eve's add 403, Bob as owner 204,
  Carol's and Alice's removals 204, the users dave, bob, eve and nobody (404) through `/users`; request ids
  `…0ec`–`…0ef`, `…083`–`…088`; `scripts/check-fixtures.py` 0 findings.
- 2026-10-30 — tests: `GroupWritesTests` (8: the plan of one change and one unchanged with no write sent, the apply's
  POST body and request id and its record, a missing user fails before anything is sent, a removal with a non-member
  and a duplicate, the last owner's warning and DELETE, an owner to add who is a member, the CSV batch partial, the
  argument checks) and `WriteCommandTests` (dry run, refusal, apply with the record, both flags, the last-owner line
  in the table, the CSV batch exit 6).
- 2026-10-30 — docs: `docs/development.md` (the permissions of the group writes, a line in "The lookups", the writes
  in "Writes and plans", the fixture naming in "Fixtures"), `README.md` "Writes behind a plan",
  `tests/fixtures/README.md` (the write answers).

## Deviations from the plan

- The users to add are resolved in one `$batch` call rather than one request each; a user to remove is not
  resolved at all (someone not on the list is unchanged, whether or not the name exists).
- The fixture transport learned one naming rule (the `$ref` POST's referenced id), documented in
  `tests/fixtures/README.md`.

## Verification

- 2026-10-30 — local, against the fixtures: `group members add Finance dave@contoso.com bob@contoso.com --dry-run`
  exit 0 with one `add` (Dave) and Bob unchanged; `--yes` with Eve as well exit 6 (Dave applied, Eve `failed:
  forbidden`, the record written); `group owners remove Finance alice@contoso.com --yes` exit 0 with the last-owner
  warning; `group owners add Finance bob@contoso.com --yes` exit 0, `meta.backend` `graph`.
- 2026-10-30 — live, `--dry-run` only: `group members add <group> <non-member> <member>` (a Microsoft 365 group of
  3 members and 1 owner) exit 0, 1 change, 1 unchanged ("a member already"), backend `graph`, 3 requests, 797 ms in
  the envelope (899 ms wall); the same without a flag exit 7, 789 ms; `group owners add <group> <non-owner>` exit 0, 1
  change, 3 requests, 778 ms; `group owners remove <sole-owner group> <its owner>` exit 0, 1 change and the warning
  "<group> loses its last owner", 2 requests, 659 ms. Nothing was applied.

## Closing checklist

- [ ] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [ ] `tasks_check` green, README index regenerated (the main session, on `main` after the merge)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` (the main session, on `main` after the merge)
