// @vitest-environment happy-dom
import { act } from "react";
import { describe, expect, it, vi } from "vitest";
import { BridgeClient } from "../bridge/client";
import { MockTransport } from "../bridge/mock-transport";
import { type BridgeMessage, MessageType, type Plan } from "../bridge/protocol";
import { button, click, hasButton, isDisabled, liveStatus, press, render, settle, submit, type } from "../test/dom";
import { type Answer, ScriptedTransport } from "../test/scripted-transport";
import { MAX_MAILBOXES, mailboxSuggestions } from "./mailbox-picker";
import { MailboxAccessWriteCard, MailboxForwardingCard, splitList } from "./mailbox-write-cards";
import { DEBOUNCE_MS } from "./suggest-field";

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
  await click(button(container, "Review the plan"));
  await settle();
}

async function confirmAndApply(container: HTMLElement, action = "Grant permissions") {
  await click(button(container, "Continue"));
  expect(button(container, action).disabled).toBe(true);
  await check(checkbox(container, "I've reviewed these changes."));
  await click(button(container, action));
  await settle();
}

const plan: Plan = {
  operation: "mailbox-access-grant",
  title: "Grant mailbox permissions on Finance (finance@contoso.com)",
  changes: [
    {
      action: "add",
      target: "Finance (finance@contoso.com)",
      detail: "Full Access for Bob Example (bob@contoso.com)",
      current: "no Full Access",
      desired: "has Full Access",
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
    expect(isDisabled(button(container, "Review the plan"))).toBe(true);
    expect(container.textContent).toContain("Still needed: the mailbox, at least one user.");

    await fill(container, { Mailbox: " finance ", Users: "bob@contoso.com" });
    expect(container.textContent).not.toContain("Still needed");
    await seePlan(container);

    const text = () => container.textContent ?? "";
    expect(text()).toContain("Grant mailbox permissions on Finance (finance@contoso.com)");
    expect(text()).toContain("Full Access for Bob Example (bob@contoso.com)");
    // The badge is the card's verb, the states sentences (task 122).
    expect(text()).toContain("GrantFinance (finance@contoso.com)");
    expect(text()).toContain("No Full Access → Has Full Access");
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
    expect(container.querySelector('[aria-label="Made"]')).not.toBeNull();

    // "Start a new change" keeps the mailbox and clears the rest (task 126): the user and the rights must not carry
    // over to the next change.
    expect(hasButton(container, "Review the plan again")).toBe(false);
    await click(button(container, "Start a new change"));
    expect(input(container, "Mailbox").value).toBe(" finance ");
    expect(input(container, "Users").value).toBe("");
    expect(button(container, "Full Access").getAttribute("aria-checked")).toBe("true");
    expect(container.textContent).toContain("Still needed: at least one user.");
  });

  it("sends nothing when the card goes back from the confirmation and from the plan", async () => {
    const { transport, sent } = recorded();
    const { container } = await accessCard(transport);
    await fill(container, { Mailbox: "finance", Users: "bob@contoso.com" });
    await seePlan(container);

    await click(button(container, "Continue"));
    await click(button(container, "Back to the plan"));
    await click(button(container, "Back to edit"));

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
    await click(button(container, "Full Access"));
    expect(withoutAutomapping().disabled).toBe(true);
    await click(button(container, "Send As"));
    expect(isDisabled(button(container, "Review the plan"))).toBe(true);
    expect(container.textContent).toContain("Still needed: the mailbox, at least one user, at least one permission.");
    await click(button(container, "Send As"));
    await click(radio(container, "Revoke"));
    expect(radio(container, "Revoke").getAttribute("aria-checked")).toBe("true");
    await fill(container, { Mailbox: "reception", Users: "bob@contoso.com; alice@contoso.com" });
    await seePlan(container);

    expect(sent[0]).toMatchObject({
      type: MessageType.mailboxAccessRevoke,
      data: { users: ["bob@contoso.com", "alice@contoso.com"], rights: ["send-as"], noAutoMapping: false },
    });
    expect(container.textContent).toContain("Has Send As → No Send As");
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
    expect(container.textContent).toContain("Full Access for Bob Example (bob@contoso.com), without automapping");
  });

  it("says when there is nothing to do, and offers no confirmation", async () => {
    const { container } = await accessCard(new MockTransport());

    await fill(container, { Mailbox: "finance", Users: "alice@contoso.com" });
    await seePlan(container);

    expect(container.textContent).toContain("Nothing to do");
    expect(container.textContent).toContain("(has Full Access already)");
    expect([...container.querySelectorAll("button")].some((item) => item.textContent === "Continue")).toBe(false);
  });

  it("shows the host's refusal of the plan: nothing was changed", async () => {
    const { container } = await accessCard(new MockTransport());

    await fill(container, { Mailbox: "archive", Users: "bob@contoso.com" });
    await seePlan(container);

    expect(container.textContent).toContain("Refused: nothing was changed");
    expect(container.textContent).toContain("Mailbox 'archive' not found.");
    expect(container.textContent).toContain("What to do: Check the name or the address and try again.");
    await click(button(container, "Back to edit"));
    expect(input(container, "Mailbox").value).toBe("archive");
  });

  it("shows a partial apply with each change's result and the service's message", async () => {
    const { container } = await accessCard(new MockTransport());

    await click(button(container, "Full Access"));
    await click(button(container, "Send As"));
    await click(button(container, "Send on Behalf"));
    await fill(container, { Mailbox: "reception", Users: "alice@contoso.com" });
    await seePlan(container);
    await confirmAndApply(container);

    expect(container.textContent).toContain("Partly done");
    expect(container.textContent).toContain("1 of 2 changes were made.");
    expect(container.textContent).toContain(
      "The mock refuses Send on Behalf on Reception, to show a partial result. Exchange Online could not do this;",
    );
    expect(container.querySelector('[aria-label="Failed"]')).not.toBeNull();
  });

  it("tells an apply the host refused from one that stopped on the way", async () => {
    const refusedCard = await accessCard(
      scripted({ type: MessageType.error, data: { code: "usage", message: "the plan is older than 15 minutes" } }),
    );
    await fill(refusedCard.container, { Mailbox: "finance", Users: "bob" });
    await seePlan(refusedCard.container);
    await confirmAndApply(refusedCard.container);
    expect(refusedCard.container.textContent).toContain("Refused: nothing was changed");
    expect(refusedCard.container.textContent).toContain("The plan is older than 15 minutes.");

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
    expect(container.textContent).toContain("0 of 2 changes were made.");
    expect(container.querySelector('[aria-label="Skipped"]')).not.toBeNull();
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
    expect(isDisabled(button(container, "Review the plan"))).toBe(true);
    expect(container.textContent).toContain("Still needed: a sign-in on the Sign-in card.");

    await rerender(<MailboxAccessWriteCard client={client} signedIn={true} />);
    await seePlan(container);
    expect(container.textContent).toContain("Continue");

    await rerender(<MailboxAccessWriteCard client={client} signedIn={false} />);
    expect(container.textContent).toContain("Signed out");
    expect(container.textContent).not.toContain("Continue");
    // A sign-out moves no focus to the picks.
    expect(document.activeElement).not.toBe(input(container, "Mailbox"));
  });
});

describe("the mailbox forwarding card", () => {
  it("forwards to an outside address with the warnings, and applies it after the confirmation", async () => {
    const { transport, sent } = recorded();
    const { container } = await forwardingCard(transport);
    expect(isDisabled(button(container, "Review the plan"))).toBe(true);
    await fill(container, { Mailbox: "alice@contoso.com" });
    expect(container.textContent).toContain("Still needed: the email address to forward to.");

    await fill(container, { Mailbox: "alice@contoso.com", "Forward to": "alice@fabrikam.com" });
    await check(checkbox(container, "Keep a copy"));
    await seePlan(container);

    expect(sent[0]).toMatchObject({
      type: MessageType.mailboxForwardingSet,
      data: { mailbox: "alice@contoso.com", to: "alice@fabrikam.com", keepCopy: false },
    });
    expect(container.textContent).toContain("ForwardAlice Example (alice@contoso.com)");
    expect(container.textContent).toContain("No forwarding → Forwards to alice@fabrikam.com, keeps no copy");
    expect(container.querySelectorAll('[role="status"]').length).toBeGreaterThanOrEqual(2);
    // A plan's warnings are titled for what they ask (task 122), and the outside address is warned in plain words.
    expect(container.textContent).toContain("Check before you confirm");
    expect(container.textContent).not.toContain("Good to know");
    expect(container.textContent).toContain(
      "alice@fabrikam.com is outside your organization. Exchange Online blocks automatic forwarding to external addresses unless your outbound spam filter policy allows it, so the mail may not arrive",
    );

    await click(button(container, "Continue"));
    // The confirmation repeats the warnings, in the window's words.
    const warnings = container.querySelector('[data-testid="confirm-warnings"]') as HTMLElement;
    expect(warnings.textContent).toContain("keeps no copy");
    expect(warnings.textContent).not.toContain("--keep-copy");
    expect(button(container, "Forward mail").className).not.toContain("bg-destructive");
    await click(button(container, "Back to the plan"));
    await confirmAndApply(container, "Forward mail");
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
    expect(container.textContent).toContain("Stop forwardingBob Example (bob@contoso.com)");
    expect(container.textContent).toContain("Forwards to bob@fabrikam.com, keeps a copy → No forwarding");
    await click(button(container, "Continue"));
    // Stopping a forwarding is a removal: the button says so and is drawn as one.
    expect(button(container, "Stop forwarding").className).toContain("bg-destructive");
    await click(button(container, "Back to the plan"));
    await click(button(container, "Back to edit"));
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
    expect(focused().textContent).toBe("Grant mailbox permissions on Finance (finance@contoso.com)");
    expect(status()).toBe("Plan: 1 change");

    await click(button(container, "Continue"));
    expect(focused().textContent).toBe("You're about to make this change as alice@contoso.com (profile work):");
    expect(status()).toBe("Confirm: 1 change");

    // Escape from the confirmation goes back to the plan, from the plan back to the picks.
    await press(focused(), "Escape");
    expect(focused().textContent).toBe("Grant mailbox permissions on Finance (finance@contoso.com)");
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
    await click(button(container, "Continue"));

    const lead = container.querySelector('[data-testid="confirm-lead"]') as HTMLElement;
    expect(lead.textContent).toMatch(/^You're about to make these \d+ changes as alice@contoso.com \(profile work\):$/);
    // Each change with its action's badge and its state now → after, as the plan showed it.
    const items = [...container.querySelectorAll("ul > li")];
    expect(items.length).toBeGreaterThan(1);
    expect(items[0].querySelector('[data-slot="badge"]')?.textContent).toBe("Grant");
    expect(items[0].textContent).toContain("→");
    expect(container.querySelector('[data-testid="confirm-warnings"]')).toBeNull();
    expect(button(container, "Grant permissions").className).not.toContain("bg-destructive");

    await click(button(container, "Cancel"));
    expect(focused()).toBe(input(container, "Mailbox"));
    expect(container.textContent).toContain("Review the plan");
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
    await click(button(container, "Continue"));
    const revoke = button(container, "Revoke permissions");
    expect(revoke.className).toContain("bg-destructive");
    expect(liveStatus(container)).toBe("Confirm: 2 changes");

    await check(checkbox(container, "I've reviewed these changes."));
    await click(revoke);
    expect(focused().textContent).toContain("Making the changes, one at a time…");
    expect(liveStatus(container)).toBe("Making the changes…");
    await press(focused(), "Escape");
    expect(container.textContent).toContain("Making the changes");
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
    expect(steps).toEqual(["1Choose (done)›", "2Review (done)›", "3Confirm (done)›", "4Result (current)"]);
    expect(container.querySelectorAll('ol[aria-label="Steps"] [aria-hidden]')).toHaveLength(3);

    await click(button(container, "Start a new change"));
    expect(focused()).toBe(input(container, "Mailbox"));
  });

  it("says a partial or failed apply, and focuses a refusal", async () => {
    const partial = await accessCard(new MockTransport());
    await click(button(partial.container, "Full Access"));
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

/** Presses a key in a field; true when the field took it (the default prevented), false when it is left to the form. */
async function key(element: HTMLElement, name: string): Promise<boolean> {
  const event = new KeyboardEvent("keydown", { key: name, bubbles: true, cancelable: true });
  await act(async () => {
    element.dispatchEvent(event);
  });
  return event.defaultPrevented;
}

/** Types into a picker and waits for its suggestions: the pause before it asks, then the answer. */
async function typeAndWait(field: HTMLInputElement, text: string) {
  // In the field, as the colleague is: leaving another field closes its list.
  await act(async () => field.focus());
  await type(field, text);
  await settle(DEBOUNCE_MS);
  await settle();
}

const options = (container: HTMLElement) =>
  [...container.querySelectorAll('[role="option"]')].map((option) => option.textContent);

const note = (container: HTMLElement) =>
  [...(container.querySelector('[data-testid="suggestion-note"]')?.querySelectorAll("p") ?? [])].map(
    (line) => line.textContent,
  );

describe("the pickers on the write cards (task 125)", () => {
  it("picks the mailbox and two users with the keyboard, and plans with the picked addresses", async () => {
    const { transport, sent } = recorded();
    const { container } = await accessCard(transport);
    const mailbox = input(container, "Mailbox");
    const users = input(container, "Users");

    // The mailbox: the shared mailboxes, listed once and filtered here; Enter on the open list picks the first.
    await typeAndWait(mailbox, "fin");
    expect(options(container)).toEqual(["Finance finance@contoso.com"]);
    expect(mailbox.getAttribute("aria-expanded")).toBe("true");
    expect(liveStatus(container)).toBe("1 matching shared mailbox: use the arrow keys to choose one.");
    expect(note(container)).toEqual(["From the shared mailboxes; any other mailbox by its full email address."]);
    expect(await key(mailbox, "Enter")).toBe(true);
    expect(mailbox.value).toBe("finance@contoso.com");
    expect(options(container)).toEqual([]);

    // The users: each pick joins the list, the arrow keys or Enter on the open list.
    await typeAndWait(users, "ali");
    expect(options(container)).toEqual(["Alice Example alice@contoso.com"]);
    await key(users, "ArrowDown");
    expect(await key(users, "Enter")).toBe(true);
    expect(users.value).toBe("alice@contoso.com, ");
    await typeAndWait(users, "alice@contoso.com, bob e");
    expect(options(container)).toEqual(["Bob Example bob@contoso.com"]);
    expect(await key(users, "Enter")).toBe(true);
    expect(users.value).toBe("alice@contoso.com, bob@contoso.com, ");

    // Enter on a closed list is the form's: "Review the plan", with the addresses picked.
    expect(await key(users, "Enter")).toBe(false);
    await submit(container.querySelector("form") as HTMLFormElement);
    await settle();
    expect(sent.map((message) => message.type)).toEqual([
      MessageType.mailboxList,
      MessageType.userSearch,
      MessageType.userSearch,
      MessageType.mailboxAccessGrant,
    ]);
    expect(sent[0].data).toEqual({ shared: true });
    expect(sent[3].data).toEqual({
      mailbox: "finance@contoso.com",
      users: ["alice@contoso.com", "bob@contoso.com"],
      rights: ["full-access"],
      noAutoMapping: false,
    });
    // The plan step says the plan, no longer what a list said: alice has Full Access on Finance already.
    expect(liveStatus(container)).toBe("Plan: 1 change");
  });

  it("lets a typed address through, lists the shared mailboxes once, and says when nothing matches", async () => {
    const { transport, sent } = recorded();
    const container = (await forwardingCard(transport)).container;
    const mailbox = input(container, "Mailbox");

    await typeAndWait(mailbox, "f");
    expect(options(container)).toEqual(["Finance finance@contoso.com"]);
    await typeAndWait(mailbox, "alice@contoso.com");
    expect(options(container)).toEqual([]);
    expect(note(container)).toEqual([
      'No shared mailbox starts with "alice@contoso.com": a full email address still works.',
      "From the shared mailboxes; any other mailbox by its full email address.",
    ]);
    expect(mailbox.getAttribute("aria-expanded")).toBe("false");
    expect(await key(mailbox, "Enter")).toBe(false);

    // Forward to: a pick fills it; an outside address the directory does not know goes as typed.
    const to = input(container, "Forward to");
    await typeAndWait(to, "bob");
    expect(options(container)).toEqual(["Bob Example bob@contoso.com"]);
    await key(to, "Enter");
    expect(to.value).toBe("bob@contoso.com");
    await typeAndWait(to, "alice@fabrikam.com");
    expect(note(container)).toEqual([
      'No user starts with "alice@fabrikam.com": a full email address still works.',
      "From the directory.",
    ]);
    expect(await key(to, "Enter")).toBe(false);
    await submit(container.querySelector("form") as HTMLFormElement);
    await settle();

    expect(sent.filter((message) => message.type === MessageType.mailboxList)).toHaveLength(1);
    expect(sent.at(-1)).toMatchObject({
      type: MessageType.mailboxForwardingSet,
      data: { mailbox: "alice@contoso.com", to: "alice@fabrikam.com", keepCopy: true },
    });
  });

  it("asks for no list signed out, and says when the shared mailboxes could not be listed", async () => {
    const signedOut = recorded();
    const card = await accessCard(signedOut.transport, false);
    await typeAndWait(input(card.container, "Mailbox"), "rec");
    expect(signedOut.sent).toEqual([]);
    expect(options(card.container)).toEqual([]);
    await card.unmount();

    let answer: { type: string; data: unknown } = {
      type: MessageType.error,
      data: { code: "exchange", message: "the session did not start" },
    };
    const transport = new ScriptedTransport(() => answer);
    const failed = await accessCard(transport);
    const mailbox = input(failed.container, "Mailbox");
    await typeAndWait(mailbox, "r");
    const line =
      "No suggestions: the shared mailboxes could not be listed (the session did not start). Enter the mailbox's email address.";
    expect(note(failed.container)).toEqual([line]);
    expect(liveStatus(failed.container)).toBe(line);

    // A reply of another kind fails the same way; the next letter asks again.
    answer = { type: MessageType.pong, data: { product: "m365-aegis", version: "1" } };
    await typeAndWait(mailbox, "re");
    expect(note(failed.container)[0]).toContain("the window answered with a 'pong'");
    expect(transport.types.filter((sentType) => sentType === MessageType.mailboxList)).toHaveLength(2);
  });

  it("names more mailboxes than it shows, by a word of the name or the address", () => {
    const many = Array.from({ length: 10 }, (_, index) => ({
      displayName: `Room ${index}`,
      primarySmtpAddress: `room${index}@contoso.com`,
      type: "SharedMailbox",
    }));
    const found = mailboxSuggestions(many, "ro");
    expect(found.options).toHaveLength(MAX_MAILBOXES);
    expect(found.announce).toBe("8 matching shared mailboxes, and more: use the arrow keys to choose one.");
    expect(found.note[0]).toBe("More mailboxes match: type more of the name.");
    expect(mailboxSuggestions(many, "3").options.map((option) => option.value)).toEqual(["room3@contoso.com"]);
    expect(mailboxSuggestions(many, "ROOM9@").options.map((option) => option.name)).toEqual(["Room 9"]);
  });
});

describe("the result of an apply not done in full (task 126)", () => {
  it("says it once, the code behind Details, and sees the plan again with what is still missing", async () => {
    const { transport, sent } = recorded();
    const { container } = await accessCard(transport);
    await click(button(container, "Full Access"));
    await click(button(container, "Send As"));
    await click(button(container, "Send on Behalf"));
    await fill(container, { Mailbox: "reception", Users: "alice@contoso.com" });
    await seePlan(container);
    await confirmAndApply(container);

    const notice = container.querySelector('[role="status"][tabindex="-1"]') as HTMLElement;
    expect(notice.textContent).toContain("Partly done");
    expect(notice.textContent).toContain("1 of 2 changes were made.");
    // The host's summary repeats the list and the record: only its code stays, collapsed.
    expect(notice.textContent).not.toContain("applied, 1 failed");
    expect(notice.textContent).not.toContain("What to do");
    const details = notice.querySelector('[data-testid="details"]') as HTMLDetailsElement;
    expect(details.open).toBe(false);
    expect(details.textContent).toBe("DetailsError code: partial");
    expect(container.textContent?.match(/%LOCALAPPDATA%/g)).toHaveLength(1);
    expect(button(container, "Start a new change").className).not.toContain("bg-primary");

    await click(button(container, "Review the plan again"));
    await settle();
    expect(sent.map((message) => message.type)).toEqual([
      MessageType.mailboxAccessGrant,
      MessageType.applyPlan,
      MessageType.mailboxAccessGrant,
    ]);
    expect(sent[2].data).toEqual(sent[0].data);
    // What was made is left as it is; only the failed change is planned again.
    expect(liveStatus(container)).toBe("Plan: 1 change");
    expect(container.textContent).toContain("Send on Behalf for Alice Example (alice@contoso.com)");
    expect(container.textContent).toContain("(has Send As already)");
  });

  it("shows the host's sentence when no change carries a message of its own", async () => {
    const skipped = { ...plan.changes[0], result: { status: "skipped" } };
    const { container } = await accessCard(
      scripted({
        type: MessageType.planApplied,
        data: {
          plan: { ...plan, changes: [skipped], mode: "applied" },
          error: { code: "internal", message: "the apply stopped", hint: "see the log" },
        },
      }),
    );
    await fill(container, { Mailbox: "finance", Users: "bob" });
    await seePlan(container);
    await confirmAndApply(container);

    expect(container.textContent).toContain("0 of 1 change was made.The apply stopped.");
    expect(container.textContent).not.toContain("see the log");
    expect(container.querySelector('[data-testid="details"]')?.textContent).toBe("DetailsError code: internal");
  });

  it("keeps the forwarding card's mailbox only when a new change starts", async () => {
    const { container } = await forwardingCard(new MockTransport());
    await fill(container, { Mailbox: "alice@contoso.com", "Forward to": "bob@contoso.com" });
    await check(checkbox(container, "Keep a copy"));
    await seePlan(container);
    await confirmAndApply(container, "Forward mail");
    expect(hasButton(container, "Review the plan again")).toBe(false);

    await click(button(container, "Start a new change"));
    expect(input(container, "Mailbox").value).toBe("alice@contoso.com");
    expect(input(container, "Forward to").value).toBe("");
    expect(checkbox(container, "Keep a copy").checked).toBe(true);
    expect(radio(container, "Forward").getAttribute("aria-checked")).toBe("true");
  });
});
