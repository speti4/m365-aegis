import type { MailboxInfo, Plan } from "../bridge/protocol";

/**
 * A change a write card applied (task 091): the plan's operation, the mailbox as the card asked for it, and the
 * addresses the plan's changes name (the core names a change's target "Finance (finance@contoso.com)"). A write card
 * tells the page once its apply has finished, whether every change, some or none was made, and when the apply broke
 * off on the way, so a card that shows the mailbox can read it again.
 */
export interface AppliedWrite {
  operation: string;
  mailbox: string;
  addresses: string[];
}

/** The page's one channel from the write cards to the cards that read what they change. */
export class AppliedWrites {
  private handlers = new Set<(write: AppliedWrite) => void>();

  /** Tells every card that listens; the write cards call it after an apply. */
  readonly publish = (write: AppliedWrite): void => {
    for (const handler of [...this.handlers]) {
      handler(write);
    }
  };

  /** Listens to the applies until the returned function is called. */
  subscribe(handler: (write: AppliedWrite) => void): () => void {
    this.handlers.add(handler);
    return () => {
      this.handlers.delete(handler);
    };
  }
}

/** The addresses in the plan's change targets, as the core writes them: "Finance (finance@contoso.com)". */
export function targetAddresses(plan: Plan): string[] {
  const addresses = plan.changes
    .map((change) => /\(([^()\s]+@[^()\s]+)\)$/.exec(change.target)?.[1])
    .filter((address): address is string => address !== undefined);
  return [...new Set(addresses)];
}

/** Whether the apply touched the mailbox: the name the card asked for or an address of its changes is the mailbox's. */
export function touches(write: AppliedWrite, mailbox: MailboxInfo): boolean {
  const names = [mailbox.displayName, mailbox.primarySmtpAddress, mailbox.userPrincipalName, mailbox.objectId]
    .filter((name): name is string => name !== undefined)
    .map((name) => name.toLowerCase());
  return [write.mailbox, ...write.addresses].some((name) => names.includes(name.trim().toLowerCase()));
}
