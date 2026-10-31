/**
 * The core's warnings in the window's words (tasks 090, 102 and 104). The core writes a plan's warnings, and the
 * access view's, for the command line, and the window shows the same `Plan` and the same `AccessReport`; a warning
 * that names a flag or a command, which the colleague at the window never types, is reworded here, at the card, into
 * what the colleague can do in the window. Every other warning, and one this list does not know, is shown as the core
 * wrote it, so a new warning of the core still reaches the card. The command line's wording stays the core's.
 *
 * The inventory (2026-10-31) of the warnings a `Plan` can carry, the core's `PlanBuilder.Warn` and the resume's own:
 * the mailbox writes' five (a recipient forwarding that the plan clears, an address outside the accepted domains, a
 * forwarding without a copy, a holder that cannot hold a right, a group holder), the group writes' four, `call`'s one,
 * `plan prune`'s one and the resume's one. Two of them name a flag: the forwarding without a copy (`--keep-copy`),
 * which the forwarding card can plan, and `plan prune`'s (`--resume`), which only the command line plans; one names a
 * command: the group owner write's last owner (`group owners-missing`), reworded for the group write card that will
 * plan it (task 104). The access view's warnings (task 084) name `snapshot take` for an old snapshot and `snapshot
 * show` for a part a snapshot could not read; the snapshot is the administrator's. The Vitest test of this file reads
 * the core's sources and fails when a new warning names a flag, and checks every bridge sample for a flag or a command.
 */

interface Rewording {
  /** The core's sentence, its variable parts captured in order. */
  core: RegExp;
  /** The window's sentence over the captured parts. */
  window: (...parts: string[]) => string;
}

const rewordings: readonly Rewording[] = [
  {
    // MailboxWrites, a forwarding set without `--keep-copy`: the forwarding card's box is "Keep a copy in the mailbox".
    core: /^(.+) keeps no copy: the mail reaches (.+) only \(--keep-copy keeps one\)$/s,
    window: (mailbox, to) =>
      `${mailbox} keeps no copy: the mail reaches ${to} only. To keep one in the mailbox too, choose "Change the picks" and tick "Keep a copy in the mailbox".`,
  },
  {
    // GroupWrites, an owner removal that leaves no owner: the list of groups without an owner is the administrator's.
    core: /^(.+) loses its last owner: only an administrator can manage it afterwards, and `group owners-missing` lists it$/s,
    window: (group) =>
      `${group} loses its last owner: only an administrator can manage it afterwards. To keep it managed, add another owner first; otherwise the administrator's list of groups without an owner names it.`,
  },
  {
    // SnapshotAccess, a snapshot older than 36 hours: the snapshot is taken on the command line.
    core: /^the snapshot is (.+) hours old, older than (.+): run `m365-aegis snapshot take` for a fresh one$/s,
    window: (age, limit) =>
      `The snapshot is ${age} hours old, older than ${limit} hours, so what it shows may have changed since. Ask the administrator for a fresh one.`,
  },
  {
    // SnapshotAccess, a part of the snapshot read only in part.
    core: /^(.+) is partial in this snapshot: (.+) item\(s\) could not be read \((.+)\); `m365-aegis snapshot show` names them$/s,
    window: (part, count, why) =>
      `${part} is partial in this snapshot: ${count} item(s) could not be read (${why}), so the answer may miss what they hold. The administrator can see which ones in the snapshot's details.`,
  },
  {
    // SnapshotAccess, a part of the snapshot that could not be read at all.
    core: /^(.+) could not be read in this snapshot; `m365-aegis snapshot show` names the failure$/s,
    window: (part) =>
      `${part} could not be read in this snapshot, so the answer has none of it. The administrator can see why in the snapshot's details.`,
  },
];

/** A warning of the core as the card shows it: reworded when it names a flag or a command, else as the core wrote it. */
export function windowWarning(warning: string): string {
  for (const { core, window } of rewordings) {
    const match = core.exec(warning);
    if (match) {
      return window(...match.slice(1));
    }
  }
  return warning;
}
