// @vitest-environment happy-dom
import { describe, expect, it } from "vitest";
import { BridgeClient } from "../bridge/client";
import { MockTransport } from "../bridge/mock-transport";
import { type GroupMembers, MessageType, PROTOCOL_VERSION } from "../bridge/protocol";
import { button, field, isDisabled, liveStatus, render, settle, submit, type } from "../test/dom";
import { type Answer, ScriptedTransport } from "../test/scripted-transport";
import { GroupMembersCard } from "./group-members-card";

async function card(answer: Answer | "mock") {
  const transport = answer === "mock" ? new MockTransport() : new ScriptedTransport(answer);
  const rendered = await render(<GroupMembersCard client={new BridgeClient(transport)} />);
  const input = field(rendered.container, "Group");
  const form = rendered.container.querySelector("form") as HTMLFormElement;
  return { ...rendered, input, form, transport };
}

const synced: GroupMembers = {
  group: { id: "00000000-0000-0000-0000-000000000033", displayName: "Sales", kind: "security", synced: true },
  members: [],
};

describe("the group members card", () => {
  it("looks a group up by its name and lists the direct members", async () => {
    const { container, input, form, unmount } = await card("mock");
    // A card is a region named by its heading, a level below the page's.
    const heading = container.querySelector("h2") as HTMLElement;
    expect(heading.textContent).toBe("Group members");
    expect(container.querySelector("section")?.getAttribute("aria-labelledby")).toBe(heading.id);
    // "Look up" waits for a group, in the Tab order, and says why.
    const lookUp = button(container, "Look up");
    expect(isDisabled(lookUp)).toBe(true);
    expect(lookUp.disabled).toBe(false);
    expect(document.getElementById(lookUp.getAttribute("aria-describedby") ?? "")?.textContent).toBe(
      "Type a group's name, address or object id first.",
    );

    await type(input, "  Finance ");
    expect(isDisabled(lookUp)).toBe(false);
    expect(lookUp.hasAttribute("aria-describedby")).toBe(false);
    await submit(form);
    await settle();

    const text = container.textContent ?? "";
    expect(liveStatus(container)).toBe("Finance: 4 direct members");
    expect(container.querySelector('[data-testid="answer-for"]')?.textContent).toBe('Answer for "Finance"');
    expect(text).toContain("Microsoft 365");
    expect(text).toContain("finance@contoso.com");
    expect(text).toContain("· 4 direct member(s)");
    expect(text).not.toContain("synced");
    const rows = [...container.querySelectorAll("tbody tr")].map((row) =>
      [...row.querySelectorAll("td")].map((cell) => cell.textContent),
    );
    expect(rows[2]).toEqual(["user", "Carol Example", "carol@contoso.com"]);
    // A member without a sign-in name shows its address.
    expect(rows[3]).toEqual(["group", "Finance EU", "finance-eu@contoso.com"]);
    // The object ids are details, behind a collapsed line (task 126).
    const details = container.querySelector('[data-testid="details"]') as HTMLDetailsElement;
    expect(details.open).toBe(false);
    expect(details.textContent).toContain("The group's object id: 00000000-0000-0000-0000-000000000031");
    expect([...details.querySelectorAll("li")].map((item) => item.textContent)[2]).toBe(
      "Carol Example: 00000000-0000-0000-0000-000000000004",
    );
    await unmount();
  });

  it("shows a synced group without an address and without members", async () => {
    const { container, input, form, transport } = await card((message) =>
      message.type === MessageType.groupMembers ? { type: MessageType.members, data: synced } : null,
    );

    await type(input, "Sales");
    await submit(form);
    await settle();

    expect((transport as ScriptedTransport).sent.at(-1)?.data).toEqual({ group: "Sales" });
    expect(container.textContent).toContain("synced");
    expect(container.textContent).toContain("The group has no direct members.");
    expect(container.querySelector("table")).toBeNull();
  });

  it("writes an empty cell for a member that has neither a name nor an address", async () => {
    const anonymous: GroupMembers = {
      group: synced.group,
      members: [{ type: "servicePrincipal", id: "00000000-0000-0000-0000-000000000040" }],
    };
    const { container, input, form } = await card(() => ({ type: MessageType.members, data: anonymous }));

    await type(input, "Sales");
    await submit(form);
    await settle();

    const cells = [...container.querySelectorAll("tbody td")].map((cell) => cell.textContent);
    expect(cells).toEqual(["servicePrincipal", "", ""]);
    // In the details, a member without a name or an address goes by its type.
    expect(container.querySelector('[data-testid="member-ids"]')?.textContent).toBe(
      "servicePrincipal: 00000000-0000-0000-0000-000000000040",
    );
  });

  it("shows a failed lookup under the field, marks the field, and drops the old table (task 120)", async () => {
    const { container, input, form } = await card("mock");
    await type(input, "Finance");
    await submit(form);
    await settle();
    expect(container.querySelector("table")).not.toBeNull();

    await type(input, "Nope");
    await submit(form);
    await settle();

    const alert = container.querySelector('[role="alert"]') as HTMLElement;
    expect(alert.textContent).toContain("The lookup failed");
    expect(alert.textContent).toContain("no group named 'Nope'");
    expect(alert.textContent).toContain("not-found");
    expect(input.getAttribute("aria-invalid")).toBe("true");
    expect(input.getAttribute("aria-describedby")).toBe(alert.id);
    expect(container.querySelector("table")).toBeNull();
    expect(liveStatus(container)).toBe("");

    // The next lookup clears the error.
    await type(input, "Finance");
    await submit(form);
    await settle();
    expect(container.querySelector('[role="alert"]')).toBeNull();
    expect(input.hasAttribute("aria-invalid")).toBe(false);
    expect(input.hasAttribute("aria-describedby")).toBe(false);
  });

  it("sends nothing for an empty field", async () => {
    const { form, transport } = await card(() => null);
    await submit(form);
    expect((transport as ScriptedTransport).sent).toEqual([]);
  });

  it("keeps the focus in the field while the host works, sends one lookup at a time, and ignores a reply of another type", async () => {
    const transport = new ScriptedTransport();
    const { container } = await render(<GroupMembersCard client={new BridgeClient(transport)} />);
    const input = field(container, "Group");
    const form = container.querySelector("form") as HTMLFormElement;
    await type(input, "Finance");
    input.focus();
    await submit(form);

    expect(isDisabled(button(container, "Looking up…"))).toBe(true);
    expect(input.disabled).toBe(false);
    expect(input.readOnly).toBe(true);
    expect(document.activeElement).toBe(input);
    expect(form.getAttribute("aria-busy")).toBe("true");
    expect(liveStatus(container)).toBe("Looking up Finance…");
    // A second Enter while the first lookup runs sends nothing.
    await submit(form);
    expect(transport.sent).toHaveLength(1);
    transport.push({
      aegis: PROTOCOL_VERSION,
      id: transport.sent[0].id,
      type: MessageType.pong,
      data: { product: "m365-aegis", version: "0.1.0" },
    });
    await settle();

    expect(isDisabled(button(container, "Look up"))).toBe(false);
    expect(input.readOnly).toBe(false);
    expect(container.querySelector('[role="alert"]')).toBeNull();
    expect(container.querySelector("table")).toBeNull();
    expect(liveStatus(container)).toBe("");
  });

  it("dims the old answer while the next lookup runs, and names one member in the singular", async () => {
    const one: GroupMembers = {
      group: synced.group,
      members: [{ type: "user", id: "00000000-0000-0000-0000-000000000041", displayName: "Dave Example" }],
    };
    let answer = true;
    const { container, input, form } = await card(() => (answer ? { type: MessageType.members, data: one } : null));
    await type(input, "Sales");
    await submit(form);
    await settle();
    expect(liveStatus(container)).toBe("Sales: 1 direct member");

    answer = false;
    await submit(form);
    const shown = container.querySelector('[data-testid="answer-for"]')?.parentElement as HTMLElement;
    expect(shown.className).toContain("opacity-50");
    expect(shown.getAttribute("aria-busy")).toBe("true");
  });
});
