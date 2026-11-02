// @vitest-environment happy-dom
import { describe, expect, it, vi } from "vitest";
import { BridgeClient } from "../bridge/client";
import { MockTransport } from "../bridge/mock-transport";
import { type BridgeMessage, MessageType, type Plan } from "../bridge/protocol";
import { button, click, isDisabled, liveStatus, press, render, settle, submit, type } from "../test/dom";
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
  const rendered = await render(
    <MailboxAccessWriteCard
      client={client}
      signedIn={signedIn}
      runAs={{ account: "alice@contoso.com", profile: "work" }}
    />,
  );
  return { ...rendered, client };
}

async function forwardingCard(transport: MockTransport | ScriptedTransport, signedIn = true) {
  return render(<MailboxForwardingCard client={new BridgeClient(transport)} signedIn={signedIn} />);
}

/** The field under the visible label, or null when the card shows none. */
const input = (container: HTMLElement, label: string) => {
  const found = [...container.querySelectorAll("label")].find((candidate) => candidate.textContent === label);
  return (found ? document.getElementById(found.htmlFor) : null) as HTMLInputElement;
};

/** An option of a choice (a radio of the radio group) by its text. */
const radio = (container: HTMLElement, name: string) =>
  [...container.querySelectorAll('[role="radio"]')].find((item) => item.textContent === name) as HTMLElement;

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

async function confirmAndApply(container: HTMLElement, action = "Grant permissions") {
  await click(button(container, "Continue to confirm"));
  expect(button(container, action).disabled).toBe(true);
  await check(checkbox(container, "I've reviewed these changes."));
  await click(button(container, action));
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
    expect(isDisabled(button(container, "See the plan"))).toBe(true);
    expect(container.textContent).toContain("Still needed: the mailbox, at least one user.");

    await fill(container, { Mailbox: " finance ", Users: "bob@contoso.com" });
    expect(container.textContent).not.toContain("Still needed");
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

    await click(button(container, "Start a new change"));
    expect(isDisabled(button(container, "See the plan"))).toBe(false);
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
    expect(isDisabled(button(container, "See the plan"))).toBe(true);
    expect(container.textContent).toContain("Still needed: the mailbox, at least one user, at least one right.");
    await click(button(container, "Send As"));
    await click(radio(container, "Revoke"));
    expect(radio(container, "Revoke").getAttribute("aria-checked")).toBe("true");
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
    expect(isDisabled(button(container, "See the plan"))).toBe(true);
    expect(container.textContent).toContain("Still needed: a sign-in on the Sign-in card.");

    await rerender(<MailboxAccessWriteCard client={client} signedIn={true} />);
    await seePlan(container);
    expect(container.textContent).toContain("Continue to confirm");

    await rerender(<MailboxAccessWriteCard client={client} signedIn={false} />);
    expect(container.textContent).toContain("Signed out");
    expect(container.textContent).not.toContain("Continue to confirm");
    // A sign-out moves no focus to the picks.
    expect(document.activeElement).not.toBe(input(container, "Mailbox"));
  });
});

describe("the mailbox forwarding card", () => {
  it("forwards to an outside address with the warnings, and applies it after the confirmation", async () => {
    const { transport, sent } = recorded();
    const { container } = await forwardingCard(transport);
    expect(isDisabled(button(container, "See the plan"))).toBe(true);
    await fill(container, { Mailbox: "alice@contoso.com" });
    expect(container.textContent).toContain("Still needed: the address to forward to.");

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

    await click(button(container, "Continue to confirm"));
    // The confirmation repeats the warnings, in the window's words.
    const warnings = container.querySelector('[data-testid="confirm-warnings"]') as HTMLElement;
    expect(warnings.textContent).toContain("keeps no copy");
    expect(warnings.textContent).not.toContain("--keep-copy");
    expect(button(container, "Start forwarding").className).not.toContain("bg-destructive");
    await click(button(container, "Back to the plan"));
    await confirmAndApply(container, "Start forwarding");
    expect(container.textContent).toContain("Every change was made.");
  });

  it("stops a forwarding, and needs only the mailbox for it", async () => {
    const { transport, sent } = recorded();
    const { container } = await forwardingCard(transport);

    await click(radio(container, "Stop forwarding"));
    expect(input(container, "Forward to")).toBeNull();
    await fill(container, { Mailbox: "bob@contoso.com" });
    await seePlan(container);

    expect(sent[0]).toEqual(
      expect.objectContaining({ type: MessageType.mailboxForwardingClear, data: { mailbox: "bob@contoso.com" } }),
    );
    expect(container.textContent).toContain("forwards to bob@fabrikam.com, keeps a copy → no forwarding");
    await click(button(container, "Continue to confirm"));
    // Stopping a forwarding is a removal: the button says so and is drawn as one.
    expect(button(container, "Stop forwarding").className).toContain("bg-destructive");
    await click(button(container, "Back to the plan"));
    await click(button(container, "Change the picks"));
    await click(radio(container, "Forward"));
    expect(input(container, "Forward to")).not.toBeNull();
  });
});

describe("a write card's keyboard, focus and announcements (tasks 120 and 121)", () => {
  const form = (container: HTMLElement) => container.querySelector("form") as HTMLFormElement;
  const focused = () => document.activeElement as HTMLElement;

  it("plans on Enter, moves the focus to each step's heading and says the step, and goes back on Escape", async () => {
    const { transport, sent } = recorded();
    const { container } = await accessCard(transport);
    const status = () => liveStatus(container);
    expect(status()).toBe("");
    // The first render takes no focus.
    expect(container.contains(focused())).toBe(false);

    // Enter on a pick that a click would toggle plans instead, and plans nothing while a pick is missing.
    await press(button(container, "Send As"), "Enter");
    expect(sent).toEqual([]);
    expect(button(container, "Send As").getAttribute("aria-checked")).toBe("false");
    await fill(container, { Mailbox: "finance", Users: "bob@contoso.com" });
    await submit(form(container));
    await settle();

    expect(sent.map((message) => message.type)).toEqual([MessageType.mailboxAccessGrant]);
    expect(focused().textContent).toBe("Grant mailbox rights on Finance (finance@contoso.com)");
    expect(status()).toBe("Plan: 1 change");

    await click(button(container, "Continue to confirm"));
    expect(focused().textContent).toBe("You're about to make this change as alice@contoso.com (profile work):");
    expect(status()).toBe("Confirm: 1 change");

    // Escape from the confirmation goes back to the plan, from the plan back to the picks.
    await press(focused(), "Escape");
    expect(focused().textContent).toBe("Grant mailbox rights on Finance (finance@contoso.com)");
    await press(focused(), "Escape");
    expect(focused()).toBe(input(container, "Mailbox"));
    expect(input(container, "Mailbox").value).toBe("finance");
    expect(status()).toBe("");
    expect(sent).toHaveLength(1);
  });

  it("confirms with the plan's actions, from → to, the account, and a Cancel that sends nothing", async () => {
    const { transport, sent } = recorded();
    const { container } = await accessCard(transport);
    await click(button(container, "Send As"));
    await fill(container, { Mailbox: "finance", Users: "bob@contoso.com" });
    await seePlan(container);
    await click(button(container, "Continue to confirm"));

    const lead = container.querySelector('[data-testid="confirm-lead"]') as HTMLElement;
    expect(lead.textContent).toMatch(/^You're about to make these \d+ changes as alice@contoso.com \(profile work\):$/);
    // Each change with its action's badge and its state now → after, as the plan showed it.
    const items = [...container.querySelectorAll("ul > li")];
    expect(items.length).toBeGreaterThan(1);
    expect(items[0].querySelector('[data-slot="badge"]')?.textContent).toBe("add");
    expect(items[0].textContent).toContain("→");
    expect(container.querySelector('[data-testid="confirm-warnings"]')).toBeNull();
    expect(button(container, "Grant permissions").className).not.toContain("bg-destructive");

    await click(button(container, "Cancel"));
    expect(focused()).toBe(input(container, "Mailbox"));
    expect(container.textContent).toContain("See the plan");
    expect(sent).toHaveLength(1);
  });

  it("revokes behind a destructive button, and leaves the apply alone on Escape", async () => {
    const answerApply = false;
    const transport = new ScriptedTransport((message) => {
      if (message.type !== MessageType.applyPlan) {
        return {
          type: MessageType.plan,
          data: {
            planId: "p1",
            plan: {
              ...plan,
              operation: "mailbox-access-revoke",
              changes: [plan.changes[0], { ...plan.changes[0], action: "remove", detail: "Send As for Bob" }],
            },
          },
        };
      }
      return answerApply
        ? {
            type: MessageType.planApplied,
            data: {
              plan: { ...plan, mode: "applied", changes: [{ ...plan.changes[0], result: { status: "applied" } }] },
            },
          }
        : null;
    });
    const { container } = await accessCard(transport);
    await click(radio(container, "Revoke"));
    await fill(container, { Mailbox: "finance", Users: "bob" });
    await seePlan(container);
    expect(liveStatus(container)).toBe("Plan: 2 changes");
    await click(button(container, "Continue to confirm"));
    const revoke = button(container, "Revoke permissions");
    expect(revoke.className).toContain("bg-destructive");
    expect(liveStatus(container)).toBe("Confirm: 2 changes");

    await check(checkbox(container, "I've reviewed these changes."));
    await click(revoke);
    expect(focused().textContent).toContain("Applying the plan");
    expect(liveStatus(container)).toBe("Applying the plan…");
    await press(focused(), "Escape");
    expect(container.textContent).toContain("Applying the plan");
    expect(container.querySelector('li[aria-current="step"]')?.textContent).toBe("4Result (current)");
    expect(container.querySelector('[aria-current="step"]')?.tagName).toBe("LI");
  });

  it("focuses the result, says how it ended, and starts over at the mailbox field", async () => {
    const { container } = await accessCard(new MockTransport());
    await fill(container, { Mailbox: "finance", Users: "bob@contoso.com" });
    await seePlan(container);
    await confirmAndApply(container);

    expect(focused().textContent).toContain("Every change was made.");
    expect(liveStatus(container)).toBe("Done: every change was made");
    // The steps say which are done; the separators are drawn only.
    const steps = [...container.querySelectorAll('ol[aria-label="Steps"] > li')].map((item) => item.textContent);
    expect(steps).toEqual(["1Pick (done)›", "2Plan (done)›", "3Confirm (done)›", "4Result (current)"]);
    expect(container.querySelectorAll('ol[aria-label="Steps"] [aria-hidden]')).toHaveLength(3);

    await click(button(container, "Start a new change"));
    expect(focused()).toBe(input(container, "Mailbox"));
  });

  it("says a partial or failed apply, and focuses a refusal", async () => {
    const partial = await accessCard(new MockTransport());
    await click(button(partial.container, "FullAccess"));
    await click(button(partial.container, "Send As"));
    await click(button(partial.container, "Send on Behalf"));
    await fill(partial.container, { Mailbox: "reception", Users: "alice@contoso.com" });
    await seePlan(partial.container);
    await confirmAndApply(partial.container);
    expect(liveStatus(partial.container)).toBe("Partly done: 1 of 2 changes made");

    const failed = await accessCard(
      scripted({
        type: MessageType.planApplied,
        data: { plan: { ...plan, mode: "applied", changes: [{ ...plan.changes[0], result: { status: "skipped" } }] } },
      }),
    );
    await fill(failed.container, { Mailbox: "finance", Users: "bob" });
    await seePlan(failed.container);
    await confirmAndApply(failed.container);
    expect(liveStatus(failed.container)).toBe("Not done: no change was made");

    const refused = await accessCard(new MockTransport());
    await fill(refused.container, { Mailbox: "archive", Users: "bob@contoso.com" });
    await seePlan(refused.container);
    expect(focused().getAttribute("role")).toBe("alert");
    expect(focused().textContent).toContain("Refused: nothing was changed");
    expect(liveStatus(refused.container)).toBe("");
    await press(focused(), "Escape");
    expect(focused()).toBe(input(refused.container, "Mailbox"));
  });

  it("says when the plan has nothing to change", async () => {
    const { container } = await accessCard(new MockTransport());
    await fill(container, { Mailbox: "finance", Users: "alice@contoso.com" });
    await seePlan(container);
    expect(liveStatus(container)).toBe("Plan: nothing to change");
  });

  it("moves the choice with the arrow keys, one Tab stop for the group, and Enter on it plans", async () => {
    const { transport, sent } = recorded();
    const { container } = await forwardingCard(transport);
    const forward = radio(container, "Forward");
    const stop = radio(container, "Stop forwarding");
    expect(forward.tabIndex).toBe(0);
    expect(stop.tabIndex).toBe(-1);

    forward.focus();
    await press(forward, "ArrowRight");
    expect(stop.getAttribute("aria-checked")).toBe("true");
    expect(input(container, "Forward to")).toBeNull();

    await fill(container, { Mailbox: "bob@contoso.com" });
    await press(radio(container, "Stop forwarding"), "Enter");
    await settle();
    expect(sent.map((message) => message.type)).toEqual([MessageType.mailboxForwardingClear]);
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
