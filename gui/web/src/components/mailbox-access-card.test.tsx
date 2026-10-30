// @vitest-environment happy-dom
import { describe, expect, it, vi } from "vitest";
import { BridgeClient } from "../bridge/client";
import { MockTransport } from "../bridge/mock-transport";
import { type MailboxAccess, MessageType, PROTOCOL_VERSION, type UserAccess } from "../bridge/protocol";
import { button, click, render, settle, submit, type } from "../test/dom";
import { type Answer, ScriptedTransport } from "../test/scripted-transport";
import { MailboxAccessCard } from "./mailbox-access-card";

async function card(transport: MockTransport | ScriptedTransport = new MockTransport()) {
  const onError = vi.fn();
  const rendered = await render(<MailboxAccessCard client={new BridgeClient(transport)} onError={onError} />);
  const input = () => rendered.container.querySelector("input") as HTMLInputElement;
  const form = rendered.container.querySelector("form") as HTMLFormElement;
  const lookUp = async (value: string) => {
    await type(input(), value);
    await submit(form);
    await settle();
  };
  return { ...rendered, input, lookUp, onError };
}

const scripted = (answer: Answer) => new ScriptedTransport(answer);

describe("the mailbox access card", () => {
  it("lists the shared mailboxes and shows the access of the one picked", async () => {
    const { container, input } = await card();

    await click(button(container, "List the shared mailboxes"));
    await settle();
    expect(container.textContent).toContain("2 shared mailbox(es); pick one to see its access.");
    expect(container.textContent).toMatch(/answered in \d+ ms/);

    await click(button(container, "Reception"));
    await settle();
    expect(input().value).toBe("reception@contoso.com");
    const rows = [...container.querySelectorAll("tbody tr")].map((row) =>
      [...row.querySelectorAll("td")].map((cell) => cell.textContent),
    );
    expect(rows).toEqual([
      ["FullAccess", "alice@contoso.com", "alice@contoso.com"],
      ["FullAccess", "bob@contoso.com", "bob@contoso.com"],
      ["Send As", "bob@contoso.com", "bob@contoso.com"],
    ]);
    expect(container.textContent).toContain("· 3 grant(s)");
  });

  it("says when nobody but the mailbox's own account holds a right", async () => {
    const { container, lookUp } = await card();

    await lookUp("Alice Example");

    expect(container.textContent).toContain("Nobody holds a right on this mailbox but its own account.");
    expect(container.querySelector("table")).toBeNull();
  });

  it("looks a user up, and shows a partial answer as a warning", async () => {
    const { container, input, lookUp } = await card();
    const byUser = button(container, "A user");

    await click(byUser);
    expect(byUser.getAttribute("aria-checked")).toBe("true");
    expect(input().getAttribute("aria-label")).toBe("User");
    expect(input().placeholder).toBe("User principal name");
    await lookUp("bob@contoso.com");

    const text = container.textContent ?? "";
    expect(text).toContain("Bob Example");
    expect(text).toContain("partial");
    expect(text).toContain("1 list(s) could not be read; the grants below may be incomplete.");
    expect(text).toContain("the FullAccess list of 'Archive' could not be read (a mock)");
    expect(container.querySelectorAll("tbody tr")).toHaveLength(3);

    await click(button(container, "A mailbox"));
    expect(input().getAttribute("aria-label")).toBe("Mailbox");
  });

  it("looks a user up without a display name, a holder without an address, and nothing to list", async () => {
    const grants: UserAccess = { user: "dave@contoso.com", grants: [], mailboxesScanned: 4, failed: [] };
    const { container, lookUp } = await card(scripted(() => ({ type: MessageType.userGrants, data: grants })));
    await click(button(container, "A user"));

    await lookUp("dave@contoso.com");

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
  });

  it("hands an error to onError", async () => {
    const { lookUp, onError } = await card();

    await lookUp("nobody");

    expect(onError).toHaveBeenCalledWith(expect.objectContaining({ code: "not-found" }));
  });

  it("tells that the first lookup starts the session, and ignores a reply of another type", async () => {
    const transport = new ScriptedTransport();
    const { container, input, onError } = await card(transport);
    await type(input(), "finance");
    await submit(container.querySelector("form") as HTMLFormElement);

    expect(container.textContent).toContain("The first lookup starts the Exchange Online session");
    expect(button(container, "Looking up…").disabled).toBe(true);
    expect(button(container, "List the shared mailboxes").disabled).toBe(true);
    transport.push({
      aegis: PROTOCOL_VERSION,
      id: transport.sent[0].id,
      type: MessageType.pong,
      data: { product: "m365-aegis", version: "0.1.0" },
    });
    await settle();

    expect(onError).not.toHaveBeenCalled();
    expect(container.textContent).not.toContain("The first lookup starts");
    expect(button(container, "Look up").disabled).toBe(false);
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
    expect(container.textContent).toContain("Nobody holds a right");
  });
});
