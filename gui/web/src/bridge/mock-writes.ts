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

/** A mailbox's forwarding in the mock, by the mailbox's address. */
export type MockForwarding = Map<string, { to: string; keepCopy: boolean }>;

/** The mock's forwarding, as the fake Exchange Online module has it: bob forwards to an outside address, keeping a copy. */
export const mockForwarding = (): MockForwarding =>
  new Map([["bob@contoso.com", { to: "bob@fabrikam.com", keepCopy: true }]]);

/** A held plan and, per change, what its apply does to the mock's state. */
interface Held {
  plan: Plan;
  steps: Array<() => void>;
}

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
 * `apply-plan` with `confirm: true` names it. The apply changes the mock's holders and forwarding as the service
 * would (task 091), so the access card's next read shows the grant; every change is applied but a Send on Behalf change
 * on Reception, which fails so the page's partial result shows. Without the confirmation, or for an id the mock does
 * not hold, the apply is refused as the host refuses it.
 */
export class MockWrites {
  private readonly held = new Map<string, Held>();
  private next = 0;

  constructor(
    private readonly find: (name: string) => MailboxInfo | undefined,
    private readonly holders: readonly MailboxAccess[],
    private readonly forwardings: MockForwarding,
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
    const held = this.held.get(id);
    if (!held) {
      return usage(
        "no plan with this id waits for a confirmation in this window; nothing was sent",
        "see the plan again on the card: a plan is applied once, and a sign-in or a sign-out drops it",
      );
    }
    if (payload.confirm !== true) {
      return error(
        "refused-without-yes",
        `${held.plan.changes.length} change(s) planned; nothing was sent`,
        "nothing was changed; confirm the change in the window to apply it",
      );
    }
    this.held.delete(id);
    const { plan, steps } = held;
    const started = this.now().toISOString();
    const changes: PlanChange[] = plan.changes.map((change, index) => {
      if (!(change.target.startsWith("Reception") && change.detail.startsWith("Send on Behalf"))) {
        steps[index]();
        return { ...change, result: { status: "applied" } };
      }
      return {
        ...change,
        result: {
          status: "failed",
          error: {
            code: "cmdlet-failed",
            message: "the mock refuses Send on Behalf on Reception, to show a partial result",
            hint: "Exchange Online could not do this; try again, and tell the administrator if it persists",
          },
        },
      };
    });
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
    const steps: Array<() => void> = [];
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
          steps.push(() => change(access, right, holder, grant));
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
      steps,
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
    const address = mailbox.primarySmtpAddress;
    const current = this.forwardings.get(address);
    const now = current ? forwarding(current.to, current.keepCopy) : "no forwarding";
    const target = named(mailbox);
    const changes: PlanChange[] = [];
    const steps: Array<() => void> = [];
    const unchanged: PlanUnchanged[] = [];
    const warnings: string[] = [];
    if (!set) {
      if (current) {
        changes.push({ action: "clear", target, detail: "the forwarding", current: now, desired: "no forwarding" });
        steps.push(() => this.forwardings.delete(address));
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
      steps.push(() => {
        this.forwardings.set(address, { to, keepCopy });
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
      steps,
    });
  }

  /** The `plan` reply: held under a new id, with what each change does, when it has a change to send. */
  private ready({ steps, ...plan }: Omit<Plan, "mode"> & { steps: Array<() => void> }): MockAnswer {
    if (plan.changes.length === 0) {
      return { type: MessageType.plan, data: { plan: { ...plan, mode: "up-to-date" } } };
    }
    this.next += 1;
    const planId = `mock-plan-${this.next}`;
    const ready: Plan = { ...plan, mode: "dry-run" };
    this.held.set(planId, { plan: ready, steps });
    return { type: MessageType.plan, data: { planId, plan: ready } };
  }
}

/**
 * A grant or a revoke of one right in the mock's holders, as the host reads it back: a Send on Behalf delegate by its
 * name from the Admin API when the mailbox is read there, every other right by its address from the session.
 */
function change(access: MailboxAccess, right: MailboxRight, holder: MailboxInfo, grant: boolean): void {
  const address = holder.primarySmtpAddress;
  const others = access.holders.filter((entry) => !(entry.right === right && entry.address === address));
  const delegate = right === "send-on-behalf" && access.mailboxLayer === "admin-api";
  access.holders = grant
    ? [
        ...others,
        delegate
          ? { right, holder: holder.displayName, address, layer: "admin-api" }
          : { right, holder: address, address, layer: "pwsh" },
      ]
    : others;
}

function text(value: unknown): string {
  return typeof value === "string" ? value.trim() : "";
}

function texts(value: unknown): string[] {
  return Array.isArray(value) ? value.map(text).filter((item) => item !== "") : [];
}
