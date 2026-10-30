/**
 * A plan's warnings in the window's words (task 090). The core writes a plan's warnings for the command line, and the
 * window shows the same `Plan`; a warning that names a flag the colleague at the window never types is reworded here,
 * at the card, for the card's own controls. Every other warning, and one this list does not know, is shown as the core
 * wrote it, so a new warning of the core still reaches the card. The command line's wording stays the core's.
 *
 * The inventory (2026-10-31) of the warnings a `Plan` can carry, the core's `PlanBuilder.Warn` and the resume's own:
 * the mailbox writes' five (a recipient forwarding that the plan clears, an address outside the accepted domains, a
 * forwarding without a copy, a holder that cannot hold a right, a group holder), the group writes' four, `call`'s one,
 * `plan prune`'s one and the resume's one. Two of them name a flag: the forwarding without a copy (`--keep-copy`),
 * which the forwarding card can plan, and `plan prune`'s (`--resume`), which only the command line plans. The Vitest
 * test of this file reads the core's sources and fails when a new warning names a flag.
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
];

/** A plan warning as the card shows it: reworded when it names a flag, else as the core wrote it. */
export function windowWarning(warning: string): string {
  for (const { core, window } of rewordings) {
    const match = core.exec(warning);
    if (match) {
      return window(...match.slice(1));
    }
  }
  return warning;
}
