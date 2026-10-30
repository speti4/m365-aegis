import {
  type BridgeError,
  type MailboxAccess,
  type MailboxInfo,
  type MailboxRight,
  MessageType,
  type Plan,
  type PlanChange,
  type PlanUnchanged,
} from "./protocol";

/** An answer of the mock: a reply's type and payload. */
export type MockAnswer = { type: string; data: unknown };

const rightNames: Record<MailboxRight, string> = {
  "full-access": "FullAccess",
  "send-as": "Send As",
  "send-on-behalf": "Send on Behalf",
};

const rights = Object.keys(rightNames) as MailboxRight[];

/** The mock's forwarding, as the fake Exchange Online module has it: bob forwards to an outside address, keeping a copy. */
const mockForwarding: Record<string, { to: string; keepCopy: boolean }> = {
  "bob@contoso.com": { to: "bob@fabrikam.com", keepCopy: true },
};

const error = (code: string, message: string, hint: string): MockAnswer => ({
  type: MessageType.error,
  data: { code, message, hint } satisfies BridgeError,
});

const usage = (message: string, hint: string) => error("usage", message, hint);

const notFound = (what: string, name: string, hint: string) => error("not-found", `${what} '${name}' not found`, hint);

const named = (mailbox: MailboxInfo) => `${mailbox.displayName} (${mailbox.primarySmtpAddress})`;

const forwarding = (to: string, keepCopy: boolean) =>
  `forwards to ${to}, ${keepCopy ? "keeps a copy" : "keeps no copy"}`;

/**
 * The browser mock's writes (task 082): each write request is planned against the mock's made-up holders and
 * forwarding as the core would plan it (only what is not so already, the warnings) and held under an id until an
 * `apply-plan` with `confirm: true` names it; the apply changes nothing in the mock, every change reads applied but a
 * Send on Behalf change on Reception, which fails so the page's partial result shows. Without the confirmation, or
 * for an id the mock does not hold, the apply is refused as the host refuses it.
 */
export class MockWrites {
  private readonly held = new Map<string, Plan>();
  private next = 0;

  constructor(
    private readonly find: (name: string) => MailboxInfo | undefined,
    private readonly holders: readonly MailboxAccess[],
    private readonly now: () => Date = () => new Date(),
  ) {}

  /** The answer to a write request. */
  plan(type: string, payload: Record<string, unknown>): MockAnswer {
    switch (type) {
      case MessageType.mailboxAccessGrant:
      case MessageType.mailboxAccessRevoke:
        return this.access(type === MessageType.mailboxAccessGrant, payload);
      default:
        return this.forwarding(type === MessageType.mailboxForwardingSet, payload);
    }
  }

  /** The answer to `apply-plan`. */
  apply(payload: Record<string, unknown>): MockAnswer {
    const id = typeof payload.planId === "string" ? payload.planId : "";
    const plan = this.held.get(id);
    if (!plan) {
      return usage(
        "no plan with this id waits for a confirmation in this window; nothing was sent",
        "see the plan again on the card: a plan is applied once, and a sign-in or a sign-out drops it",
      );
    }
    if (payload.confirm !== true) {
      return error(
        "refused-without-yes",
        `${plan.changes.length} change(s) planned; nothing was sent`,
        "nothing was changed; confirm the change in the window to apply it",
      );
    }
    this.held.delete(id);
    const started = this.now().toISOString();
    const changes: PlanChange[] = plan.changes.map((change) =>
      change.target.startsWith("Reception") && change.detail.startsWith("Send on Behalf")
        ? {
            ...change,
            result: {
              status: "failed",
              error: {
                code: "cmdlet-failed",
                message: "the mock refuses Send on Behalf on Reception, to show a partial result",
                hint: "Exchange Online could not do this; try again, and tell the administrator if it persists",
              },
            },
          }
        : { ...change, result: { status: "applied" } },
    );
    const failed = changes.filter((change) => change.result?.status === "failed").length;
    const record = `%LOCALAPPDATA%\\m365-aegis\\plans\\${started.replace(/[-:]/g, "")}-${plan.operation}.json`;
    return {
      type: MessageType.planApplied,
      data: {
        plan: { ...plan, changes, mode: "applied", record, started, finished: started },
        error:
          failed === 0
            ? undefined
            : {
                code: "partial",
                message: `${changes.length - failed} of ${changes.length} change(s) applied, ${failed} failed`,
                hint: `the data lists each change's result and the service's message; the plan record is ${record}`,
              },
      },
    };
  }

  private access(grant: boolean, payload: Record<string, unknown>): MockAnswer {
    const users = texts(payload.users);
    const asked = texts(payload.rights);
    const noAutoMapping = grant && payload.noAutoMapping === true;
    if (users.length === 0) {
      return usage(
        "no user was named",
        "give each user's user principal name or address, or a mail-enabled security group's address",
      );
    }
    if (asked.length === 0) {
      return usage("no right was chosen", "choose FullAccess, Send As or Send on Behalf");
    }
    const unknown = asked.find((right) => !rights.includes(right as MailboxRight));
    if (unknown !== undefined) {
      return usage(`unknown right '${unknown}'`, "give FullAccess, SendAs or SendOnBehalf");
    }
    const chosen = asked as MailboxRight[];
    if (noAutoMapping && !chosen.includes("full-access")) {
      return usage(
        "without automapping goes with FullAccess",
        "automapping is how Outlook adds a mailbox its FullAccess holder can open; choose FullAccess, or clear Without automapping",
      );
    }
    const name = text(payload.mailbox);
    const access = this.holders.find((candidate) => candidate.mailbox === this.find(name));
    if (!access) {
      return notFound("mailbox", name, "check the name or the address and try again");
    }
    const changes: PlanChange[] = [];
    const unchanged: PlanUnchanged[] = [];
    for (const user of users) {
      const holder = this.find(user);
      if (!holder) {
        return notFound("recipient", user, "give the holder's address or user principal name; nothing was sent");
      }
      for (const right of chosen) {
        const word = rightNames[right];
        const without = grant && right === "full-access" && noAutoMapping ? ", without automapping" : "";
        const detail = `${word} for ${named(holder)}${without}`;
        const holds = access.holders.some(
          (entry) => entry.right === right && entry.address === holder.primarySmtpAddress,
        );
        if (holds === grant) {
          unchanged.push({
            target: named(access.mailbox),
            detail,
            state: grant ? `holds ${word} already` : `no ${word}`,
          });
        } else {
          const [current, desired] = grant ? [`no ${word}`, `holds ${word}`] : [`holds ${word}`, `no ${word}`];
          changes.push({ action: grant ? "add" : "remove", target: named(access.mailbox), detail, current, desired });
        }
      }
    }
    const verb = grant ? "Grant" : "Revoke";
    return this.ready({
      operation: grant ? MessageType.mailboxAccessGrant : MessageType.mailboxAccessRevoke,
      title: `${verb} mailbox rights on ${named(access.mailbox)}`,
      changes,
      unchanged,
      warnings: [],
    });
  }

  private forwarding(set: boolean, payload: Record<string, unknown>): MockAnswer {
    const to = set ? text(payload.to) : "";
    const keepCopy = set && payload.keepCopy === true;
    if (set && to === "") {
      return usage("the forwarding address is missing", "give the address to forward to, such as bob@fabrikam.com");
    }
    if (set && !/^[^@\s]+@[^@\s]+$/.test(to)) {
      return usage(
        `the forwarding address: '${to}' is not an address`,
        "give the address to forward to, such as bob@fabrikam.com; nothing was sent",
      );
    }
    const name = text(payload.mailbox);
    const mailbox = this.find(name);
    if (!mailbox) {
      return notFound("mailbox", name, "check the name or the address and try again");
    }
    const current = mockForwarding[mailbox.primarySmtpAddress];
    const now = current ? forwarding(current.to, current.keepCopy) : "no forwarding";
    const target = named(mailbox);
    const changes: PlanChange[] = [];
    const unchanged: PlanUnchanged[] = [];
    const warnings: string[] = [];
    if (!set) {
      if (current) {
        changes.push({ action: "clear", target, detail: "the forwarding", current: now, desired: "no forwarding" });
      } else {
        unchanged.push({ target, detail: "the forwarding", state: "no forwarding" });
      }
    } else if (current?.to === to && current.keepCopy === keepCopy) {
      unchanged.push({ target, detail: `forwarding to ${to}`, state: `${now} already` });
    } else {
      changes.push({
        action: "set",
        target,
        detail: `forwarding to ${to}`,
        current: now,
        desired: forwarding(to, keepCopy),
      });
      if (!to.toLowerCase().endsWith("@contoso.com")) {
        warnings.push(
          `${to} is outside the accepted domains: the mail leaves the organization, and the outbound spam filter policy blocks automatic forwarding to an external address unless the policy allows it`,
        );
      }
      if (!keepCopy) {
        warnings.push(`${mailbox.displayName} keeps no copy: the mail reaches ${to} only (--keep-copy keeps one)`);
      }
    }
    const verb = set ? "Set the forwarding of" : "Clear the forwarding of";
    return this.ready({
      operation: set ? MessageType.mailboxForwardingSet : MessageType.mailboxForwardingClear,
      title: `${verb} ${target}`,
      changes,
      unchanged,
      warnings,
    });
  }

  /** The `plan` reply: held under a new id when it has a change to send. */
  private ready(plan: Omit<Plan, "mode">): MockAnswer {
    if (plan.changes.length === 0) {
      return { type: MessageType.plan, data: { plan: { ...plan, mode: "up-to-date" } } };
    }
    this.next += 1;
    const planId = `mock-plan-${this.next}`;
    const ready: Plan = { ...plan, mode: "dry-run" };
    this.held.set(planId, ready);
    return { type: MessageType.plan, data: { planId, plan: ready } };
  }
}

function text(value: unknown): string {
  return typeof value === "string" ? value.trim() : "";
}

function texts(value: unknown): string[] {
  return Array.isArray(value) ? value.map(text).filter((item) => item !== "") : [];
}
