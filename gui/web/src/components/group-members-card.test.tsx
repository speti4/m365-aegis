// @vitest-environment happy-dom
import { describe, expect, it, vi } from "vitest";
import { BridgeClient } from "../bridge/client";
import { MockTransport } from "../bridge/mock-transport";
import { type GroupMembers, MessageType, PROTOCOL_VERSION } from "../bridge/protocol";
import { button, render, settle, submit, type } from "../test/dom";
import { type Answer, ScriptedTransport } from "../test/scripted-transport";
import { GroupMembersCard } from "./group-members-card";

async function card(answer: Answer | "mock") {
  const transport = answer === "mock" ? new MockTransport() : new ScriptedTransport(answer);
  const onError = vi.fn();
  const rendered = await render(<GroupMembersCard client={new BridgeClient(transport)} onError={onError} />);
  const input = rendered.container.querySelector("input") as HTMLInputElement;
  const form = rendered.container.querySelector("form") as HTMLFormElement;
  return { ...rendered, input, form, onError, transport };
}

const synced: GroupMembers = {
  group: { id: "00000000-0000-0000-0000-000000000033", displayName: "Sales", kind: "security", synced: true },
  members: [],
};

describe("the group members card", () => {
  it("looks a group up by its name and lists the direct members", async () => {
    const { container, input, form, unmount } = await card("mock");
    expect(button(container, "Look up").disabled).toBe(true);

    await type(input, "  Finance ");
    await submit(form);
    await settle();

    const text = container.textContent ?? "";
    expect(text).toContain("Microsoft 365");
    expect(text).toContain("finance@contoso.com");
    expect(text).toContain("· 4 direct member(s)");
    expect(text).not.toContain("synced");
    const rows = [...container.querySelectorAll("tbody tr")].map((row) =>
      [...row.querySelectorAll("td")].map((cell) => cell.textContent),
    );
    expect(rows[2]).toEqual(["user", "Carol Example", "carol@contoso.com", "00000000-0000-0000-0000-000000000004"]);
    // A member without a sign-in name shows its address.
    expect(rows[3]).toEqual(["group", "Finance EU", "finance-eu@contoso.com", "00000000-0000-0000-0000-000000000032"]);
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
    expect(cells).toEqual(["servicePrincipal", "", "", "00000000-0000-0000-0000-000000000040"]);
  });

  it("hands an error reply to onError and shows no table", async () => {
    const { container, input, form, onError } = await card("mock");

    await type(input, "Nope");
    await submit(form);
    await settle();

    expect(onError).toHaveBeenCalledWith(
      expect.objectContaining({ code: "not-found", message: "no group named 'Nope'" }),
    );
    expect(container.querySelector("table")).toBeNull();
  });

  it("waits while the host works and ignores a reply of another type", async () => {
    const transport = new ScriptedTransport();
    const onError = vi.fn();
    const { container } = await render(<GroupMembersCard client={new BridgeClient(transport)} onError={onError} />);
    await type(container.querySelector("input") as HTMLInputElement, "Finance");
    await submit(container.querySelector("form") as HTMLFormElement);

    expect(button(container, "Looking up…").disabled).toBe(true);
    expect((container.querySelector("input") as HTMLInputElement).disabled).toBe(true);
    transport.push({
      aegis: PROTOCOL_VERSION,
      id: transport.sent[0].id,
      type: MessageType.pong,
      data: { product: "m365-aegis", version: "0.1.0" },
    });
    await settle();

    expect(button(container, "Look up").disabled).toBe(false);
    expect(onError).not.toHaveBeenCalled();
    expect(container.querySelector("table")).toBeNull();
  });
});
