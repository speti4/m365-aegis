// @vitest-environment happy-dom
import { describe, expect, it, vi } from "vitest";
import { BridgeClient } from "../bridge/client";
import { MockTransport } from "../bridge/mock-transport";
import { type BridgeMessage, type MailboxInfo, MessageType, type Plan, type PlanChange } from "../bridge/protocol";
import { button, click, isDisabled, liveStatus, render, settle, submit, type } from "../test/dom";
import { type Answer, ScriptedTransport } from "../test/scripted-transport";
import {
  type AppliedWrite,
  AppliedWrites,
  detailAddresses,
  namesUser,
  targetAddresses,
  touches,
} from "./applied-writes";
import { MailboxAccessCard } from "./mailbox-access-card";
import { MailboxAccessWriteCard, MailboxForwardingCard } from "./mailbox-write-cards";
import { confirmActions } from "./plan-wording";

const finance: MailboxInfo = {
  displayName: "Finance",
  primarySmtpAddress: "finance@contoso.com",
  type: "SharedMailbox",
  objectId: "00000000-0000-0000-0000-000000000015",
};

const grant = (mailbox: string, addresses: string[] = [], users: string[] = []): AppliedWrite => ({
  operation: MessageType.mailboxAccessGrant,
  mailbox,
  addresses,
  users,
});

describe("the applies the write cards tell", () => {
  it("names the addresses of a plan's targets once each", () => {
    const change = (target: string): PlanChange => ({ action: "add", target, detail: "d", current: "a", desired: "b" });
    const plan: Plan = {
      operation: MessageType.mailboxAccessGrant,
      title: "t",
      changes: [
        change("Finance (finance@contoso.com)"),
        change("Finance (finance@contoso.com)"),
        change("Reception"),
        change("Bob (bob@contoso.com) (shared)"),
      ],
      unchanged: [],
      warnings: [],
      mode: "dry-run",
    };
    expect(targetAddresses(plan)).toEqual(["finance@contoso.com"]);
  });

  it("matches a mailbox by the name asked or an address of the changes, whatever the case", () => {
    expect(touches(grant(" FINANCE "), finance)).toBe(true);
    expect(touches(grant("00000000-0000-0000-0000-000000000015"), finance)).toBe(true);
    expect(touches(grant("fin", ["finance@contoso.com"]), finance)).toBe(true);
    expect(touches(grant("reception", ["reception@contoso.com"]), finance)).toBe(false);
    expect(touches(grant("finance@contoso.com"), { ...finance, userPrincipalName: "finance@contoso.com" })).toBe(true);
  });

  it("names the addresses in the changes' details once each, as the users the write named", () => {
    const change = (detail: string): PlanChange => ({
      action: "add",
      target: "Finance",
      detail,
      current: "a",
      desired: "b",
    });
    const plan: Plan = {
      operation: MessageType.mailboxAccessGrant,
      title: "t",
      changes: [
        change("FullAccess for Bob Example (bob@contoso.com), without automapping"),
        change("Send As for Bob Example (bob@contoso.com)"),
        change("Send As for Finance Staff (finance-staff@contoso.com) and Carol (carol@contoso.com)"),
        change("forwarding to alice@fabrikam.com"),
      ],
      unchanged: [],
      warnings: [],
      mode: "dry-run",
    };
    expect(detailAddresses(plan)).toEqual(["bob@contoso.com", "finance-staff@contoso.com", "carol@contoso.com"]);
  });

  it("matches the user a card shows by a user the write named, whatever the case", () => {
    const bob = { user: "bob@contoso.com", displayName: "Bob Example", grants: [], mailboxesScanned: 6, failed: [] };
    expect(namesUser(grant("finance", [], [" BOB@contoso.com "]), bob)).toBe(true);
    expect(namesUser(grant("finance", [], ["bob example"]), bob)).toBe(true);
    expect(namesUser(grant("finance", [], ["alice@contoso.com"]), bob)).toBe(false);
    expect(namesUser(grant("finance", [], ["Bob Example"]), { ...bob, displayName: undefined })).toBe(false);
  });

  it("tells every listener until it stops listening", () => {
    const applied = new AppliedWrites();
    const heard: string[] = [];
    const stop = applied.subscribe((write) => heard.push(write.mailbox));
    applied.publish(grant("finance"));
    stop();
    applied.publish(grant("reception"));
    expect(heard).toEqual(["finance"]);
  });
});

/** The page's three mailbox cards over one client and one channel, as `App` puts them together. */
async function page(transport: MockTransport | ScriptedTransport) {
  const client = new BridgeClient(transport);
  const applied = new AppliedWrites();
  const rendered = await render(
    <>
      <MailboxAccessCard client={client} applied={applied} />
      <MailboxAccessWriteCard client={client} signedIn onApplied={applied.publish} />
      <MailboxForwardingCard client={client} signedIn onApplied={applied.publish} />
    </>,
  );
  const [access, rights, forwarding] = [...rendered.container.querySelectorAll('[data-slot="card"]')] as HTMLElement[];
  return { ...rendered, access, rights, forwarding };
}

/** The card's field under the visible label. */
const input = (card: HTMLElement, label: string) => {
  const found = [...card.querySelectorAll("label")].find((candidate) => candidate.textContent === label);
  return document.getElementById(found?.htmlFor ?? "") as HTMLInputElement;
};

/** An option of a card's choice (a radio), by its text. */
const radio = (card: HTMLElement, name: string) =>
  [...card.querySelectorAll('[role="radio"]')].find((item) => item.textContent === name) as HTMLElement;

/** The confirmation's button, whatever the operation names it. */
const confirmButton = (card: HTMLElement) =>
  [...card.querySelectorAll("button")].find((item) =>
    Object.values(confirmActions).some((action) => action.label === item.textContent),
  ) as HTMLButtonElement;

async function lookUp(card: HTMLElement, mailbox: string) {
  await type(input(card, "Mailbox"), mailbox);
  await submit(card.querySelector("form") as HTMLFormElement);
  await settle();
}

/** Plans the card's picks and confirms the plan. */
async function apply(card: HTMLElement, values: Record<string, string>) {
  for (const [label, value] of Object.entries(values)) {
    await type(input(card, label), value);
  }
  await click(button(card, "See the plan"));
  await settle();
  await click(button(card, "Continue to confirm"));
  await click(
    [...card.querySelectorAll("label")]
      .find((label) => label.textContent?.includes("I've reviewed"))
      ?.querySelector("input") as HTMLInputElement,
  );
  await click(confirmButton(card));
}

/** A mock transport whose requests the test can read. */
function recorded() {
  const transport = new MockTransport();
  const sent: BridgeMessage[] = [];
  const send = transport.send.bind(transport);
  vi.spyOn(transport, "send").mockImplementation((message) => {
    sent.push(message);
    send(message);
  });
  const types = () => sent.map((message) => message.type);
  return { transport, sent, types };
}

const holderRows = (card: HTMLElement) =>
  [...card.querySelectorAll("tbody tr")].map((row) => [...row.querySelectorAll("td")].map((cell) => cell.textContent));

/** The layer each holder was read through, as the answer's "Details" line lists them (task 126). */
const layers = (card: HTMLElement) =>
  [...card.querySelectorAll('[data-testid="layers"] li')].map((item) => item.textContent);

describe("the mailbox access card after an apply", () => {
  it("reads the mailbox it shows again once a grant is applied to it, and shows the new grant", async () => {
    const { transport, sent, types } = recorded();
    const { access, rights } = await page(transport);
    await lookUp(access, "finance@contoso.com");
    expect(holderRows(access)).toHaveLength(3);
    expect(access.querySelector('[data-testid="refreshed"]')).toBeNull();

    await apply(rights, { Mailbox: "Finance", Users: "bob@contoso.com" });
    await settle();

    expect(types()).toEqual([
      MessageType.mailboxAccess,
      MessageType.mailboxAccessGrant,
      MessageType.applyPlan,
      MessageType.mailboxAccess,
    ]);
    expect(sent[3].data).toEqual({ mailbox: "finance@contoso.com" });
    expect(holderRows(access)).toContainEqual(["FullAccess", "bob@contoso.com", "bob@contoso.com"]);
    expect(layers(access)).toContain("FullAccess, bob@contoso.com: read through PowerShell");
    expect(access.querySelector('[data-testid="refreshed"]')?.textContent).toBe(
      "Read again after a change on the Mailbox rights card.",
    );
    expect(access.textContent).not.toContain("reading its access again");

    // A lookup of its own clears the mark.
    await submit(access.querySelector("form") as HTMLFormElement);
    await settle();
    expect(access.querySelector('[data-testid="refreshed"]')).toBeNull();
  });

  it("tells the rights card it read the mailbox again, whose Show it scrolls the access card into view (task 124)", async () => {
    const scrolled = vi.spyOn(HTMLElement.prototype, "scrollIntoView").mockImplementation(() => {});
    const { access, rights } = await page(new MockTransport());
    await lookUp(access, "finance@contoso.com");

    await apply(rights, { Mailbox: "Finance", Users: "bob@contoso.com" });
    await settle();
    expect(rights.querySelector('[data-testid="re-read"]')?.textContent).toBe(
      "The Mailbox access card read Finance again after this change.Show it",
    );

    await click(button(rights, "Show it"));
    expect(scrolled).toHaveBeenCalledOnce();
    expect(scrolled.mock.contexts[0]).toBe(access);
    expect(scrolled).toHaveBeenCalledWith({ behavior: "smooth", block: "start" });
    expect(document.activeElement).toBe(access);

    // "Start a new change" starts the card over; the next apply, to a mailbox the access card does not show, has no
    // such line.
    await click(button(rights, "Start a new change"));
    await click(radio(rights, "Revoke"));
    await apply(rights, { Mailbox: "reception@contoso.com", Users: "bob@contoso.com" });
    await settle();
    expect(rights.textContent).toContain("Every change was made.");
    expect(rights.querySelector('[data-testid="re-read"]')).toBeNull();
    scrolled.mockRestore();
  });

  it("reads it again after a partial apply too, and after a revoke, with the delegate the Admin API names", async () => {
    const { transport, types } = recorded();
    const { access, rights } = await page(transport);
    await lookUp(access, "reception");

    await click(button(rights, "FullAccess"));
    await click(button(rights, "Send As"));
    await click(button(rights, "Send on Behalf"));
    await apply(rights, { Mailbox: "reception@contoso.com", Users: "alice@contoso.com" });
    await settle();

    expect(rights.textContent).toContain("Partly done");
    expect(types().filter((sentType) => sentType === MessageType.mailboxAccess)).toHaveLength(2);
    expect(holderRows(access)).toContainEqual(["Send As", "alice@contoso.com", "alice@contoso.com"]);

    // "Start a new change" keeps the mailbox only (task 126): the revoke names the user and the right again.
    await click(button(rights, "Start a new change"));
    expect(input(rights, "Mailbox").value).toBe("reception@contoso.com");
    expect(input(rights, "Users").value).toBe("");
    await click(radio(rights, "Revoke"));
    await click(button(rights, "FullAccess"));
    await click(button(rights, "Send As"));
    await apply(rights, { Users: "alice@contoso.com" });
    await settle();
    expect(types().filter((sentType) => sentType === MessageType.mailboxAccess)).toHaveLength(3);
    expect(holderRows(access)).not.toContainEqual(["Send As", "alice@contoso.com", "alice@contoso.com"]);
  });

  it("shows a Send on Behalf grant by name where the Admin API reads the mailbox", async () => {
    const { access, rights } = await page(new MockTransport());
    await lookUp(access, "finance");

    await click(button(rights, "FullAccess"));
    await click(button(rights, "Send on Behalf"));
    await apply(rights, { Mailbox: "finance", Users: "alice@contoso.com" });
    await settle();

    expect(holderRows(access)).toContainEqual(["Send on Behalf", "Alice Example", "alice@contoso.com"]);
    expect(layers(access)).toContain("Send on Behalf, Alice Example: read through Admin API");
  });

  it("sends nothing after an apply to another mailbox, a forwarding, or while it shows no mailbox", async () => {
    const { transport, types } = recorded();
    const { access, rights, forwarding } = await page(transport);

    // No mailbox on the card yet.
    await apply(rights, { Mailbox: "alice@contoso.com", Users: "bob@contoso.com" });
    await settle();
    expect(types()).toEqual([MessageType.mailboxAccessGrant, MessageType.applyPlan]);

    await lookUp(access, "finance");
    await click(button(rights, "Start a new change"));
    await apply(rights, { Mailbox: "bob@contoso.com", Users: "alice@contoso.com" });
    await settle();
    expect(rights.textContent).toContain("Every change was made.");
    await apply(forwarding, { Mailbox: "finance", "Forward to": "bob@contoso.com" });
    await settle();

    expect(types().filter((sentType) => sentType === MessageType.mailboxAccess)).toHaveLength(1);
    expect(forwarding.textContent).toContain("Every change was made.");
    expect(access.querySelector('[data-testid="refreshed"]')).toBeNull();
  });

  it("sends nothing while it shows a user's grants", async () => {
    const { transport, types } = recorded();
    const { access, rights } = await page(transport);
    await click(radio(access, "A user"));
    await type(input(access, "User"), "bob@contoso.com");
    await submit(access.querySelector("form") as HTMLFormElement);
    await settle();

    await apply(rights, { Mailbox: "finance", Users: "bob@contoso.com" });
    await settle();

    expect(types().filter((sentType) => sentType === MessageType.mailboxAccess)).toHaveLength(1);
  });
});

describe("the write card's apply, as the access card hears it", () => {
  const plan: Plan = {
    operation: MessageType.mailboxAccessGrant,
    title: "Grant mailbox rights on Finance (finance@contoso.com)",
    changes: [
      {
        action: "add",
        target: "Finance (finance@contoso.com)",
        detail: "FullAccess for Bob Example (bob@contoso.com)",
        current: "no FullAccess",
        desired: "holds FullAccess",
      },
    ],
    unchanged: [],
    warnings: [],
    mode: "dry-run",
  };

  /** A host that plans `plan`, answers the apply with `applyAnswer` and the lookups with Finance's holders. */
  const host = (applyAnswer: ReturnType<Answer>) =>
    new ScriptedTransport((message) => {
      switch (message.type) {
        case MessageType.applyPlan:
          return applyAnswer;
        case MessageType.mailboxAccess:
          return { type: MessageType.mailboxHolders, data: { mailbox: finance, holders: [] } };
        default:
          return { type: MessageType.plan, data: { planId: "p1", plan } };
      }
    });

  async function lookUpThenApply(transport: ScriptedTransport) {
    const { access, rights } = await page(transport);
    await lookUp(access, "finance");
    await apply(rights, { Mailbox: "fin", Users: "bob" });
    await settle();
    return {
      access,
      rights,
      lookups: transport.types.filter((sentType) => sentType === MessageType.mailboxAccess).length,
    };
  }

  it("is told after an apply where no change was made, and after one that broke off on the way", async () => {
    const failed = {
      type: MessageType.planApplied,
      data: {
        plan: { ...plan, mode: "applied", changes: [{ ...plan.changes[0], result: { status: "failed" } }] },
        error: { code: "forbidden", message: "the role is missing" },
      },
    };
    expect((await lookUpThenApply(host(failed))).lookups).toBe(2);
    expect(
      (await lookUpThenApply(host({ type: MessageType.error, data: { code: "internal", message: "x" } }))).lookups,
    ).toBe(2);
  });

  it("is not told after an apply the host refused, or an answer that is no apply's", async () => {
    const refused = { type: MessageType.error, data: { code: "refused-without-yes", message: "not confirmed" } };
    expect((await lookUpThenApply(host(refused))).lookups).toBe(1);
    expect(
      (await lookUpThenApply(host({ type: MessageType.pong, data: { product: "m365-aegis", version: "1" } }))).lookups,
    ).toBe(1);
  });

  it("says it reads the mailbox again while the read is under way", async () => {
    const transport = new ScriptedTransport((message) => {
      switch (message.type) {
        case MessageType.mailboxAccess:
          return transport.types.filter((sentType) => sentType === MessageType.mailboxAccess).length === 1
            ? { type: MessageType.mailboxHolders, data: { mailbox: finance, holders: [] } }
            : null;
        case MessageType.applyPlan:
          return { type: MessageType.planApplied, data: { plan: { ...plan, mode: "applied" } } };
        default:
          return { type: MessageType.plan, data: { planId: "p1", plan } };
      }
    });
    const { access, rights } = await page(transport);
    await lookUp(access, "finance");
    await apply(rights, { Mailbox: "finance", Users: "bob" });
    await settle();

    expect(access.textContent).toContain("A change was applied to this mailbox: reading its access again…");
    expect(liveStatus(access)).toBe("A change was applied to this mailbox: reading its access again…");
    expect(isDisabled(button(access, "Looking up…"))).toBe(true);
  });

  it("raises a failed read again as the card's error", async () => {
    let lookups = 0;
    const transport = new ScriptedTransport((message) => {
      if (message.type === MessageType.mailboxAccess) {
        lookups += 1;
        return lookups === 1
          ? { type: MessageType.mailboxHolders, data: { mailbox: finance, holders: [] } }
          : { type: MessageType.error, data: { code: "sidecar-unavailable", message: "the session ended" } };
      }
      return message.type === MessageType.applyPlan
        ? { type: MessageType.planApplied, data: { plan: { ...plan, mode: "applied" } } }
        : { type: MessageType.plan, data: { planId: "p1", plan } };
    });
    const { access, rights } = await lookUpThenApply(transport);
    const alert = access.querySelector('[role="alert"]') as HTMLElement;
    expect(alert.textContent).toContain("the session ended");
    expect(alert.textContent).toContain("sidecar-unavailable");
    expect(access.querySelector('[data-testid="refreshed"]')).toBeNull();
    expect(rights.querySelector('[data-testid="re-read"]')).toBeNull();
  });
});

describe("the mailbox access card's user view after an apply (task 103)", () => {
  async function showUser(card: HTMLElement, user: string) {
    await click(radio(card, "A user"));
    await type(input(card, "User"), user);
    await submit(card.querySelector("form") as HTMLFormElement);
    await settle();
  }

  const grantRows = (card: HTMLElement) =>
    [...card.querySelectorAll("tbody tr")].map((row) =>
      [...row.querySelectorAll("td")].map((cell) => cell.textContent),
    );

  /** A plan of a FullAccess grant for bob on Finance, as the host plans it and, `applied`, applies it. */
  const bobsGrant = (applied: boolean) => ({
    operation: MessageType.mailboxAccessGrant,
    title: "Grant",
    changes: [
      {
        action: "add",
        target: "Finance (finance@contoso.com)",
        detail: "FullAccess for Bob Example (bob@contoso.com)",
        current: "no FullAccess",
        desired: "holds FullAccess",
        ...(applied ? { result: { status: "applied" } } : {}),
      },
    ],
    unchanged: [],
    warnings: [],
    mode: applied ? "applied" : "dry-run",
  });

  it("marks the list after a grant that names the user, and reads it once on Read again", async () => {
    const { transport, sent, types } = recorded();
    const { access, rights } = await page(transport);
    await showUser(access, "bob@contoso.com");
    expect(access.querySelector('[data-testid="stale"]')).toBeNull();
    expect(grantRows(access)).not.toContainEqual(["Finance", "finance@contoso.com", "SharedMailbox", "FullAccess"]);

    await apply(rights, { Mailbox: "finance", Users: "bob@contoso.com" });
    await settle();

    // Marked, and nothing read: the reverse lookup goes over every mailbox.
    expect(access.querySelector('[data-testid="stale"]')?.textContent).toContain(
      "A change on the Mailbox rights card named this user since this list was read, so it may be out of date.",
    );
    expect(types().filter((sentType) => sentType === MessageType.mailboxAccess)).toHaveLength(1);
    expect(liveStatus(access)).toContain("so it may be out of date");

    // Another apply that names the user changes nothing more.
    await click(button(rights, "Start a new change"));
    await click(button(rights, "Send As"));
    await apply(rights, { Users: "bob@contoso.com" });
    await settle();
    expect(types().filter((sentType) => sentType === MessageType.mailboxAccess)).toHaveLength(1);

    await click(button(access, "Read again"));
    await settle();

    expect(types().filter((sentType) => sentType === MessageType.mailboxAccess)).toHaveLength(2);
    expect(sent.at(-1)?.data).toEqual({ user: "bob@contoso.com" });
    expect(access.querySelector('[data-testid="stale"]')).toBeNull();
    expect(grantRows(access)).toContainEqual(["Finance", "finance@contoso.com", "SharedMailbox", "FullAccess"]);
    // "Read again" is gone with the mark: the focus is back in the user field.
    expect(document.activeElement).toBe(input(access, "User"));
  });

  it("marks it after a revoke that names the user", async () => {
    const { access, rights } = await page(new MockTransport());
    await showUser(access, "bob@contoso.com");

    await click(radio(rights, "Revoke"));
    await apply(rights, { Mailbox: "reception", Users: "BOB@contoso.com" });
    await settle();

    expect(rights.textContent).toContain("Every change was made.");
    expect(access.querySelector('[data-testid="stale"]')).not.toBeNull();
  });

  it("marks nothing after an apply for another user, or a forwarding to the user", async () => {
    const { transport, types } = recorded();
    const { access, rights, forwarding } = await page(transport);
    await showUser(access, "bob@contoso.com");

    await apply(rights, { Mailbox: "bob@contoso.com", Users: "alice@contoso.com" });
    await settle();
    await apply(forwarding, { Mailbox: "alice@contoso.com", "Forward to": "bob@contoso.com" });
    await settle();

    expect(rights.textContent).toContain("Every change was made.");
    expect(forwarding.textContent).toContain("Every change was made.");
    expect(access.querySelector('[data-testid="stale"]')).toBeNull();
    expect(types().filter((sentType) => sentType === MessageType.mailboxAccess)).toHaveLength(1);
  });

  it("knows the user by an address of the plan's details, and sends no second lookup while one is under way", async () => {
    const transport = new ScriptedTransport((message) => {
      switch (message.type) {
        case MessageType.mailboxAccess:
          return transport.types.filter((sentType) => sentType === MessageType.mailboxAccess).length === 1
            ? {
                type: MessageType.userGrants,
                data: { user: "bob@contoso.com", grants: [], mailboxesScanned: 6, failed: [] },
              }
            : null;
        case MessageType.applyPlan:
          return { type: MessageType.planApplied, data: { plan: bobsGrant(true) } };
        default:
          return { type: MessageType.plan, data: { planId: "p1", plan: bobsGrant(false) } };
      }
    });
    const { access, rights } = await page(transport);
    await showUser(access, "bob");
    // The card asked for "b"; the plan's detail names bob@contoso.com, the user the card shows.
    await apply(rights, { Mailbox: "finance", Users: "b" });
    await settle();
    expect(access.querySelector('[data-testid="stale"]')).not.toBeNull();

    await click(button(access, "Read again"));
    await settle();

    expect(isDisabled(button(access, "Read again"))).toBe(true);
    expect(transport.types.filter((sentType) => sentType === MessageType.mailboxAccess)).toHaveLength(2);
  });
});
