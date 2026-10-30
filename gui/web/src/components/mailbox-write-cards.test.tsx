// @vitest-environment happy-dom
import { describe, expect, it, vi } from "vitest";
import { BridgeClient } from "../bridge/client";
import { MockTransport } from "../bridge/mock-transport";
import { type BridgeMessage, MessageType, type Plan } from "../bridge/protocol";
import { button, click, render, settle, type } from "../test/dom";
import { type Answer, ScriptedTransport } from "../test/scripted-transport";
import { MailboxAccessWriteCard, MailboxForwardingCard, splitList } from "./mailbox-write-cards";

/** A mock transport whose requests the test can read: what the page sent, in order. */
function recorded() {
  const transport = new MockTransport();
  const sent: BridgeMessage[] = [];
  const send = transport.send.bind(transport);
  vi.spyOn(transport, "send").mockImplementation((message) => {
    sent.push(message);
    send(message);
  });
  return { transport, sent };
}

async function accessCard(transport: MockTransport | ScriptedTransport, signedIn = true) {
  const client = new BridgeClient(transport);
  const rendered = await render(<MailboxAccessWriteCard client={client} signedIn={signedIn} />);
  return { ...rendered, client };
}

async function forwardingCard(transport: MockTransport | ScriptedTransport, signedIn = true) {
  return render(<MailboxForwardingCard client={new BridgeClient(transport)} signedIn={signedIn} />);
}

const input = (container: HTMLElement, label: string) =>
  container.querySelector(`input[aria-label="${label}"]`) as HTMLInputElement;

const checkbox = (container: HTMLElement, text: string) =>
  [...container.querySelectorAll("label")]
    .find((label) => label.textContent?.includes(text))
    ?.querySelector("input") as HTMLInputElement;

async function check(element: HTMLInputElement) {
  await click(element);
}

async function fill(container: HTMLElement, values: Record<string, string>) {
  for (const [label, value] of Object.entries(values)) {
    await type(input(container, label), value);
  }
}

async function seePlan(container: HTMLElement) {
  await click(button(container, "See the plan"));
  await settle();
}

async function confirmAndApply(container: HTMLElement) {
  await click(button(container, "Continue to confirm"));
  expect(button(container, "Confirm and apply").disabled).toBe(true);
  await check(checkbox(container, "I checked the plan"));
  await click(button(container, "Confirm and apply"));
  await settle();
}

const plan: Plan = {
  operation: "mailbox-access-grant",
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

/** A host that plans the sample plan and answers the apply with `apply`. */
const scripted = (apply: ReturnType<Answer>) =>
  new ScriptedTransport((message) =>
    message.type === MessageType.applyPlan ? apply : { type: MessageType.plan, data: { planId: "p1", plan } },
  );

describe("the mailbox rights card", () => {
  it("grants FullAccess with a plan and a confirmation, the apply only after the confirmation", async () => {
    const { transport, sent } = recorded();
    const { container } = await accessCard(transport);
    expect(button(container, "See the plan").disabled).toBe(true);

    await fill(container, { Mailbox: " finance ", Users: "bob@contoso.com" });
    await seePlan(container);

    const text = () => container.textContent ?? "";
    expect(text()).toContain("Grant mailbox rights on Finance (finance@contoso.com)");
    expect(text()).toContain("FullAccess for Bob Example (bob@contoso.com)");
    expect(text()).toContain("no FullAccess → holds FullAccess");
    expect(sent.map((message) => message.type)).toEqual([MessageType.mailboxAccessGrant]);
    expect(sent[0].data).toEqual({
      mailbox: "finance",
      users: ["bob@contoso.com"],
      rights: ["full-access"],
      noAutoMapping: false,
    });

    await confirmAndApply(container);

    expect(sent.map((message) => message.type)).toEqual([MessageType.mailboxAccessGrant, MessageType.applyPlan]);
    expect(sent[1].data).toEqual({ planId: "mock-plan-1", confirm: true });
    expect(text()).toContain("Every change was made.");
    expect(text()).toMatch(/The plan record: %LOCALAPPDATA%.*mailbox-access-grant\.json/);
    expect(container.querySelector('[aria-label="applied"]')).not.toBeNull();

    await click(button(container, "Done"));
    expect(button(container, "See the plan").disabled).toBe(false);
  });

  it("sends nothing when the card goes back from the confirmation and from the plan", async () => {
    const { transport, sent } = recorded();
    const { container } = await accessCard(transport);
    await fill(container, { Mailbox: "finance", Users: "bob@contoso.com" });
    await seePlan(container);

    await click(button(container, "Continue to confirm"));
    await click(button(container, "Back to the plan"));
    await click(button(container, "Change the picks"));

    expect(sent.map((message) => message.type)).toEqual([MessageType.mailboxAccessGrant]);
    expect(input(container, "Mailbox").value).toBe("finance");
  });

  it("revokes the rights chosen, and leaves automapping to a FullAccess grant", async () => {
    const { transport, sent } = recorded();
    const { container } = await accessCard(transport);
    const withoutAutomapping = () => checkbox(container, "Without automapping");

    await check(withoutAutomapping());
    expect(withoutAutomapping().checked).toBe(true);
    await click(button(container, "Send As"));
    await click(button(container, "FullAccess"));
    expect(withoutAutomapping().disabled).toBe(true);
    await click(button(container, "Send As"));
    expect(button(container, "See the plan").disabled).toBe(true);
    await click(button(container, "Send As"));
    await click(button(container, "Revoke"));
    await fill(container, { Mailbox: "reception", Users: "bob@contoso.com; alice@contoso.com" });
    await seePlan(container);

    expect(sent[0]).toMatchObject({
      type: MessageType.mailboxAccessRevoke,
      data: { users: ["bob@contoso.com", "alice@contoso.com"], rights: ["send-as"], noAutoMapping: false },
    });
    expect(container.textContent).toContain("holds Send As → no Send As");
    expect(container.textContent).toContain("Left as it is:");
    expect(container.textContent).toContain("(no Send As)");
  });

  it("asks for a grant without automapping", async () => {
    const { transport, sent } = recorded();
    const { container } = await accessCard(transport);

    await check(checkbox(container, "Without automapping"));
    await fill(container, { Mailbox: "finance", Users: "bob@contoso.com" });
    await seePlan(container);

    expect(sent[0].data).toMatchObject({ noAutoMapping: true });
    expect(container.textContent).toContain("FullAccess for Bob Example (bob@contoso.com), without automapping");
  });

  it("says when there is nothing to do, and offers no confirmation", async () => {
    const { container } = await accessCard(new MockTransport());

    await fill(container, { Mailbox: "finance", Users: "alice@contoso.com" });
    await seePlan(container);

    expect(container.textContent).toContain("Nothing to do");
    expect(container.textContent).toContain("(holds FullAccess already)");
    expect([...container.querySelectorAll("button")].some((item) => item.textContent === "Continue to confirm")).toBe(
      false,
    );
  });

  it("shows the host's refusal of the plan: nothing was changed", async () => {
    const { container } = await accessCard(new MockTransport());

    await fill(container, { Mailbox: "archive", Users: "bob@contoso.com" });
    await seePlan(container);

    expect(container.textContent).toContain("Refused: nothing was changed");
    expect(container.textContent).toContain("mailbox 'archive' not found");
    expect(container.textContent).toContain("What to do: check the name or the address and try again");
    await click(button(container, "Change the picks"));
    expect(input(container, "Mailbox").value).toBe("archive");
  });

  it("shows a partial apply with each change's result and the service's message", async () => {
    const { container } = await accessCard(new MockTransport());

    await click(button(container, "FullAccess"));
    await click(button(container, "Send As"));
    await click(button(container, "Send on Behalf"));
    await fill(container, { Mailbox: "reception", Users: "alice@contoso.com" });
    await seePlan(container);
    await confirmAndApply(container);

    expect(container.textContent).toContain("Partly done");
    expect(container.textContent).toContain("1 of 2 change(s) were made.");
    expect(container.textContent).toContain("the mock refuses Send on Behalf on Reception");
    expect(container.querySelector('[aria-label="failed"]')).not.toBeNull();
  });

  it("tells an apply the host refused from one that stopped on the way", async () => {
    const refusedCard = await accessCard(
      scripted({ type: MessageType.error, data: { code: "usage", message: "the plan is older than 15 minutes" } }),
    );
    await fill(refusedCard.container, { Mailbox: "finance", Users: "bob" });
    await seePlan(refusedCard.container);
    await confirmAndApply(refusedCard.container);
    expect(refusedCard.container.textContent).toContain("Refused: nothing was changed");
    expect(refusedCard.container.textContent).toContain("the plan is older than 15 minutes");

    const stoppedCard = await accessCard(
      scripted({ type: MessageType.error, data: { code: "internal", message: "the window broke" } }),
    );
    await fill(stoppedCard.container, { Mailbox: "finance", Users: "bob" });
    await seePlan(stoppedCard.container);
    await confirmAndApply(stoppedCard.container);
    expect(stoppedCard.container.textContent).toContain("Stopped: check the mailbox before you try again");
  });

  it("shows an apply that changed nothing, a skipped change and no record", async () => {
    const skipped = { ...plan.changes[0], detail: "Send As for Bob", result: { status: "skipped" } };
    const failed = {
      ...plan.changes[0],
      result: { status: "failed", error: { code: "forbidden", message: "the role is missing" } },
    };
    const { container } = await accessCard(
      scripted({
        type: MessageType.planApplied,
        data: {
          plan: { ...plan, changes: [failed, skipped], mode: "applied" },
          error: { code: "forbidden", message: "the role is missing" },
        },
      }),
    );
    await fill(container, { Mailbox: "finance", Users: "bob" });
    await seePlan(container);
    await confirmAndApply(container);

    expect(container.textContent).toContain("Not done");
    expect(container.textContent).toContain("0 of 2 change(s) were made.");
    expect(container.querySelector('[aria-label="skipped"]')).not.toBeNull();
    expect(container.textContent).not.toContain("The plan record");
    expect(container.textContent).not.toContain("What to do");
  });

  it("shows an apply that ended without the host's summary of the failure", async () => {
    const skipped = { ...plan.changes[0], result: { status: "skipped" } };
    const { container } = await accessCard(
      scripted({ type: MessageType.planApplied, data: { plan: { ...plan, changes: [skipped], mode: "applied" } } }),
    );
    await fill(container, { Mailbox: "finance", Users: "bob" });
    await seePlan(container);
    await confirmAndApply(container);

    expect(container.textContent).toContain("Not done");
    expect(container.querySelector('[role="alert"] .font-mono')).toBeNull();
  });

  it("waits for a sign-in, and goes back to the picks when the account signs out", async () => {
    const transport = new MockTransport();
    const client = new BridgeClient(transport);
    const { container, rerender } = await render(<MailboxAccessWriteCard client={client} signedIn={false} />);
    await fill(container, { Mailbox: "finance", Users: "bob@contoso.com" });

    expect(container.textContent).toContain("Signed out");
    expect(button(container, "See the plan").disabled).toBe(true);

    await rerender(<MailboxAccessWriteCard client={client} signedIn={true} />);
    await seePlan(container);
    expect(container.textContent).toContain("Continue to confirm");

    await rerender(<MailboxAccessWriteCard client={client} signedIn={false} />);
    expect(container.textContent).toContain("Signed out");
    expect(container.textContent).not.toContain("Continue to confirm");
  });
});

describe("the mailbox forwarding card", () => {
  it("forwards to an outside address with the warnings, and applies it after the confirmation", async () => {
    const { transport, sent } = recorded();
    const { container } = await forwardingCard(transport);
    expect(button(container, "See the plan").disabled).toBe(true);

    await fill(container, { Mailbox: "alice@contoso.com", "Forward to": "alice@fabrikam.com" });
    await check(checkbox(container, "Keep a copy"));
    await seePlan(container);

    expect(sent[0]).toMatchObject({
      type: MessageType.mailboxForwardingSet,
      data: { mailbox: "alice@contoso.com", to: "alice@fabrikam.com", keepCopy: false },
    });
    expect(container.textContent).toContain("no forwarding → forwards to alice@fabrikam.com, keeps no copy");
    expect(container.querySelectorAll('[role="status"]').length).toBeGreaterThanOrEqual(2);
    expect(container.textContent).toContain("Good to know");

    await confirmAndApply(container);
    expect(container.textContent).toContain("Every change was made.");
  });

  it("stops a forwarding, and needs only the mailbox for it", async () => {
    const { transport, sent } = recorded();
    const { container } = await forwardingCard(transport);

    await click(button(container, "Stop forwarding"));
    expect(input(container, "Forward to")).toBeNull();
    await fill(container, { Mailbox: "bob@contoso.com" });
    await seePlan(container);

    expect(sent[0]).toEqual(
      expect.objectContaining({ type: MessageType.mailboxForwardingClear, data: { mailbox: "bob@contoso.com" } }),
    );
    expect(container.textContent).toContain("forwards to bob@fabrikam.com, keeps a copy → no forwarding");
    await click(button(container, "Change the picks"));
    await click(button(container, "Forward"));
    expect(input(container, "Forward to")).not.toBeNull();
  });
});

describe("the list of users", () => {
  it("splits on commas, semicolons and spaces", () => {
    expect(splitList(" bob@contoso.com, alice@contoso.com;carol@contoso.com  dave ")).toEqual([
      "bob@contoso.com",
      "alice@contoso.com",
      "carol@contoso.com",
      "dave",
    ]);
    expect(splitList(" , ")).toEqual([]);
  });
});
