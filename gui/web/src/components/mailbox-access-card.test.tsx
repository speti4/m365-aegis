// @vitest-environment happy-dom
import { describe, expect, it } from "vitest";
import { BridgeClient } from "../bridge/client";
import { MockTransport } from "../bridge/mock-transport";
import { type MailboxAccess, MessageType, PROTOCOL_VERSION, type UserAccess } from "../bridge/protocol";
import { button, click, isDisabled, liveStatus, press, render, settle, submit, type } from "../test/dom";
import { type Answer, ScriptedTransport } from "../test/scripted-transport";
import { MailboxAccessCard } from "./mailbox-access-card";
import { DEBOUNCE_MS } from "./user-picker";

async function card(transport: MockTransport | ScriptedTransport = new MockTransport()) {
  const rendered = await render(<MailboxAccessCard client={new BridgeClient(transport)} />);
  // The field under the choice: its label is the choice's ("Mailbox" or "User").
  const label = () => rendered.container.querySelector("form label") as HTMLLabelElement;
  const input = () => document.getElementById(label().htmlFor) as HTMLInputElement;
  const form = rendered.container.querySelector("form") as HTMLFormElement;
  const lookUp = async (value: string) => {
    await type(input(), value);
    await submit(form);
    await settle();
  };
  return { ...rendered, label, input, form, lookUp };
}

/** An option of the "Look up by" choice, by its text. */
const radio = (container: HTMLElement, name: string) =>
  [...container.querySelectorAll('[role="radio"]')].find((item) => item.textContent === name) as HTMLElement;

const scripted = (answer: Answer) => new ScriptedTransport(answer);

describe("the mailbox access card", () => {
  it("lists the shared mailboxes and shows the access of the one picked", async () => {
    const { container, input } = await card();
    // The list button is no option of the choice.
    expect(button(container, "List the shared mailboxes").closest('[role="radiogroup"]')).toBeNull();

    await click(button(container, "List the shared mailboxes"));
    await settle();
    expect(container.textContent).toContain("2 shared mailbox(es); pick one to see its access.");
    // The time the answer took is a detail, behind the collapsed "Details" line (task 126).
    const listDetails = container.querySelector('[data-testid="details"]') as HTMLDetailsElement;
    expect(listDetails.open).toBe(false);
    expect(listDetails.textContent).toMatch(/^DetailsAnswered in \d+ ms\.$/);
    expect(liveStatus(container)).toBe("2 shared mailboxes");

    await click(button(container, "Reception"));
    await settle();
    expect(input().value).toBe("reception@contoso.com");
    // The button picked is gone with the list: the focus is back in the field.
    expect(document.activeElement).toBe(input());
    expect(liveStatus(container)).toBe("Reception: 3 grants");
    expect(container.querySelector('[data-testid="answer-for"]')?.textContent).toBe(
      'Answer for "reception@contoso.com"',
    );
    const rows = [...container.querySelectorAll("tbody tr")].map((row) =>
      [...row.querySelectorAll("td")].map((cell) => cell.textContent),
    );
    expect(rows).toEqual([
      ["FullAccess", "alice@contoso.com", "alice@contoso.com"],
      ["FullAccess", "bob@contoso.com", "bob@contoso.com"],
      ["Send As", "bob@contoso.com", "bob@contoso.com"],
    ]);
    // The layers are details too: the sentence and the layer of each row, not a column (task 126).
    expect([...container.querySelectorAll("th")].map((head) => head.textContent)).toEqual([
      "Right",
      "Holder",
      "Address",
    ]);
    const details = container.querySelector('[data-testid="details"]') as HTMLDetailsElement;
    expect(details.open).toBe(false);
    expect(details.textContent).toContain(
      "The mailbox and its Send on Behalf delegates by name come from the Admin API",
    );
    expect([...details.querySelectorAll("li")].map((item) => item.textContent)).toEqual([
      "FullAccess, alice@contoso.com: read through PowerShell",
      "FullAccess, bob@contoso.com: read through PowerShell",
      "Send As, bob@contoso.com: read through PowerShell",
    ]);
    expect(details.textContent).toMatch(/Answered in \d+ ms\.$/);
    expect(container.textContent).toContain("· 3 grant(s)");
  });

  it("says when nobody but the mailbox's own account holds a right", async () => {
    const { container, lookUp } = await card();

    await lookUp("Alice Example");

    expect(container.textContent).toContain("Nobody holds a right on this mailbox but its own account.");
    expect(container.querySelector("table")).toBeNull();
  });

  it("looks a user up, and shows a partial answer as a warning", async () => {
    const { container, label, input, lookUp } = await card();
    const byUser = radio(container, "A user");
    expect(label().textContent).toBe("Mailbox");

    await click(byUser);
    expect(byUser.getAttribute("aria-checked")).toBe("true");
    expect(label().textContent).toBe("User");
    expect(input().placeholder).toBe("Start typing a name, or a full address");
    expect(input().getAttribute("role")).toBe("combobox");
    await lookUp("bob@contoso.com");
    expect(liveStatus(container)).toBe("Bob Example: 3 grants over 4 mailboxes");

    const text = container.textContent ?? "";
    expect(text).toContain("Bob Example");
    expect(text).toContain("partial");
    expect(text).toContain("1 list(s) could not be read; the grants below may be incomplete.");
    expect(text).toContain("the FullAccess list of 'Archive' could not be read (a mock)");
    expect(container.querySelectorAll("tbody tr")).toHaveLength(3);

    await click(radio(container, "A mailbox"));
    expect(label().textContent).toBe("Mailbox");
  });

  it("looks up the user picked from the suggestions, and suggests no user for a mailbox (task 112)", async () => {
    const { container, input } = await card();
    await type(input(), "bob");
    await settle(DEBOUNCE_MS);
    expect(container.querySelector('[role="option"]')).toBeNull();

    await click(radio(container, "A user"));
    // The text typed for the mailbox stays; the field suggests once the colleague types on.
    await type(input(), "bob ex");
    await settle(DEBOUNCE_MS);
    await click(container.querySelector('[role="option"]') as HTMLElement);
    await settle();

    expect(input().value).toBe("bob@contoso.com");
    expect(container.textContent).toContain("Bob Example");
    expect(container.querySelectorAll("tbody tr")).toHaveLength(3);
  });

  it("looks a user up without a display name, a holder without an address, and nothing to list", async () => {
    const grants: UserAccess = { user: "dave@contoso.com", grants: [], mailboxesScanned: 4, failed: [] };
    const { container, lookUp } = await card(scripted(() => ({ type: MessageType.userGrants, data: grants })));
    await click(radio(container, "A user"));

    await lookUp("dave@contoso.com");
    expect(liveStatus(container)).toBe("dave@contoso.com: 0 grants over 4 mailboxes");

    expect(container.textContent).toContain("dave@contoso.com· 0 grant(s) over 4 mailbox(es)");
    expect(container.textContent).not.toContain("partial");
    expect(container.textContent).toContain("The user holds no right on another mailbox.");

    const access: MailboxAccess = {
      mailbox: { displayName: "Archive", primarySmtpAddress: "archive@contoso.com", type: "SharedMailbox" },
      holders: [{ right: "send-on-behalf", holder: "Finance team" }],
    };
    const second = await card(scripted(() => ({ type: MessageType.mailboxHolders, data: access })));
    await second.lookUp("archive");
    const cells = [...second.container.querySelectorAll("tbody td")].map((cell) => cell.textContent);
    expect(cells).toEqual(["Send on Behalf", "Finance team", ""]);
    // No layer named: the details have the time only.
    expect(second.container.querySelector('[data-testid="layers"]')).toBeNull();
    expect(liveStatus(second.container)).toBe("Archive: 1 grant");

    const one: UserAccess = { ...grants, mailboxesScanned: 1, displayName: "Dave Example" };
    const third = await card(scripted(() => ({ type: MessageType.userGrants, data: one })));
    await click(radio(third.container, "A user"));
    await third.lookUp("dave@contoso.com");
    expect(liveStatus(third.container)).toBe("Dave Example: 0 grants over 1 mailbox");
  });

  it("shows an error under the field and drops the old answer (task 120)", async () => {
    const { container, input, lookUp } = await card();
    await lookUp("finance");
    expect(container.querySelector("table")).not.toBeNull();

    await lookUp("nobody");

    const alert = container.querySelector('[role="alert"]') as HTMLElement;
    expect(alert.textContent).toContain("The lookup failed");
    expect(alert.textContent).toContain("not-found");
    expect(input().getAttribute("aria-invalid")).toBe("true");
    expect(input().getAttribute("aria-describedby")).toBe(alert.id);
    expect(container.querySelector("table")).toBeNull();

    // The user picker carries the same marks.
    await click(radio(container, "A user"));
    expect(input().getAttribute("aria-invalid")).toBe("true");
    expect(input().getAttribute("aria-describedby")).toBe(alert.id);
  });

  it("sends nothing for an empty field, and moves the choice with the arrow keys", async () => {
    const transport = new ScriptedTransport();
    const { container, form } = await card(transport);
    expect(isDisabled(button(container, "Look up"))).toBe(true);
    await submit(form);
    expect(transport.sent).toEqual([]);

    const byMailbox = radio(container, "A mailbox");
    byMailbox.focus();
    await press(byMailbox, "ArrowRight");
    expect(radio(container, "A user").getAttribute("aria-checked")).toBe("true");
    // Enter on the choice submits no form: the choice is outside the card's form.
    await press(radio(container, "A user"), "Enter");
    expect(transport.sent).toEqual([]);
  });

  it("tells that the first lookup starts the session, and ignores a reply of another type", async () => {
    const transport = new ScriptedTransport();
    const { container, input } = await card(transport);
    await type(input(), "finance");
    input().focus();
    await submit(container.querySelector("form") as HTMLFormElement);

    expect(container.textContent).toContain("The first lookup starts the Exchange Online session");
    expect(liveStatus(container)).toBe(
      "Looking up finance… The first lookup starts the Exchange Online session, which takes a few seconds.",
    );
    expect(isDisabled(button(container, "Looking up…"))).toBe(true);
    expect(isDisabled(button(container, "List the shared mailboxes"))).toBe(true);
    expect(input().readOnly).toBe(true);
    expect(document.activeElement).toBe(input());
    transport.push({
      aegis: PROTOCOL_VERSION,
      id: transport.sent[0].id,
      type: MessageType.pong,
      data: { product: "m365-aegis", version: "0.1.0" },
    });
    await settle();

    expect(container.querySelector('[role="alert"]')).toBeNull();
    expect(container.textContent).not.toContain("The first lookup starts");
    expect(isDisabled(button(container, "Look up"))).toBe(false);
  });

  it("keeps the last answer on screen while the next lookup runs", async () => {
    let answer = true;
    const holders: MailboxAccess = {
      mailbox: { displayName: "Finance", primarySmtpAddress: "finance@contoso.com", type: "SharedMailbox" },
      holders: [],
    };
    const { container, lookUp } = await card(
      scripted(() => (answer ? { type: MessageType.mailboxHolders, data: holders } : null)),
    );
    await lookUp("finance");
    answer = false;

    await lookUp("finance");

    expect(button(container, "Looking up…")).toBeDefined();
    expect(container.textContent).not.toContain("The first lookup starts");
    expect(liveStatus(container)).toBe("Looking up finance…");
    expect(container.textContent).toContain("Nobody holds a right");
    const shown = container.querySelector('[data-testid="answer-for"]')?.closest('[aria-busy="true"]');
    expect(shown?.className).toContain("opacity-50");
  });
});
