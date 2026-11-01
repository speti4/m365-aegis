// @vitest-environment happy-dom
import { readFileSync } from "node:fs";
import path from "node:path";
import { useState } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { BridgeClient } from "../bridge/client";
import { mockUserSearch } from "../bridge/mock-access";
import { MockTransport } from "../bridge/mock-transport";
import { type BridgeMessage, MessageType, PROTOCOL_VERSION, parseMessage, readReply } from "../bridge/protocol";
import { render, settle, type } from "../test/dom";
import { ScriptedTransport } from "../test/scripted-transport";
import { DEBOUNCE_MS, pickedAddress, UserPicker } from "./user-picker";

const contracts = path.resolve(__dirname, "../../../../tests/contracts/bridge");

/** A sample of the bridge (tests/contracts/bridge), as the page reads it. */
function sample(name: string): BridgeMessage {
  const message = parseMessage(readFileSync(path.join(contracts, name), "utf8"));
  if (!message) {
    throw new Error(`${name} is not a message of the protocol`);
  }
  return message;
}

/** The picker in a form, as a card holds it: the value is the form's, a pick and a submit are recorded. */
function Harness({
  client,
  disabled,
  onPick,
  onSubmit,
}: {
  client: BridgeClient;
  disabled?: boolean;
  onPick: (address: string) => void;
  onSubmit: (value: string) => void;
}) {
  const [value, setValue] = useState("");
  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        onSubmit(value);
      }}
    >
      <UserPicker client={client} value={value} onChange={setValue} onPick={onPick} disabled={disabled} />
    </form>
  );
}

async function picker(transport: MockTransport | ScriptedTransport = new MockTransport()) {
  const onPick = vi.fn();
  const onSubmit = vi.fn();
  const client = new BridgeClient(transport);
  const rendered = await render(<Harness client={client} onPick={onPick} onSubmit={onSubmit} />);
  const input = () => rendered.container.querySelector("input") as HTMLInputElement;
  const options = () =>
    [...rendered.container.querySelectorAll('[role="option"]')].map((option) => option.textContent ?? "");
  const note = () => rendered.container.querySelector("p")?.textContent ?? null;
  /** Presses a key in the field; true when the picker took it (the default prevented). */
  const press = async (key: string) => {
    const event = new KeyboardEvent("keydown", { key, bubbles: true, cancelable: true });
    await settleAfter(() => input().dispatchEvent(event));
    return event.defaultPrevented;
  };
  const disable = (disabled: boolean) =>
    rendered.rerender(<Harness client={client} onPick={onPick} onSubmit={onSubmit} disabled={disabled} />);
  return { ...rendered, input, options, note, press, disable, onPick, onSubmit };
}

/** Runs a step that changes the page, then lets React settle. */
async function settleAfter(step: () => unknown) {
  step();
  await settle(1);
}

/** Types into the field, waits the debounce and the answer. */
async function typeAndWait(input: HTMLInputElement, text: string) {
  await type(input, text);
  await settle(DEBOUNCE_MS);
  await settle(1);
}

const searches = (transport: ScriptedTransport) =>
  transport.sent.filter((message) => message.type === MessageType.userSearch).map((message) => message.data);

describe("the user picker", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("is answered from the bridge sample: the mock's search for 'exa' is the user-matches sample", () => {
    const text = (sample("user-search.json").data as { text: string }).text;
    const answer = readReply({ aegis: PROTOCOL_VERSION, id: "17", ...mockUserSearch(text, true) });
    expect(answer).toEqual(readReply(sample("user-matches.json")));
  });

  it("suggests after three letters, and a word typed at speed sends one request", async () => {
    const transport = new ScriptedTransport(() => ({
      type: MessageType.userMatches,
      data: {
        snapshot: "s",
        users: [{ displayName: "Carol Example", userPrincipalName: "carol@contoso.com" }],
        more: false,
      },
    }));
    const { input, options } = await picker(transport);

    await type(input(), "c");
    await type(input(), "ca");
    await settle(DEBOUNCE_MS * 2);
    expect(searches(transport)).toEqual([]);

    for (const text of ["car", "caro", "carol"]) {
      await type(input(), text);
      await settle(DEBOUNCE_MS - 1);
    }
    expect(searches(transport)).toEqual([]);
    await settle(1);
    await settle(1);

    expect(searches(transport)).toEqual([{ text: "carol" }]);
    expect(options()).toEqual(["Carol Examplecarol@contoso.com"]);
    expect(input().getAttribute("aria-expanded")).toBe("true");
  });

  it("picks with the keyboard: the arrows wrap, Enter hands the address to the card", async () => {
    const { input, options, press, onPick, onSubmit } = await picker();

    await typeAndWait(input(), "exa");
    expect(options()).toHaveLength(5);
    expect(input().getAttribute("aria-activedescendant")).toBeNull();

    expect(await press("ArrowDown")).toBe(true);
    const first = input().getAttribute("aria-activedescendant");
    expect(document.getElementById(first as string)?.getAttribute("aria-selected")).toBe("true");
    expect(await press("ArrowUp")).toBe(true);
    expect(document.getElementById(input().getAttribute("aria-activedescendant") as string)?.textContent).toBe(
      "Eve Exampleeve@contoso.com",
    );
    await press("ArrowDown");
    await press("ArrowDown");
    expect(await press("Tab")).toBe(false);
    expect(await press("Enter")).toBe(true);

    expect(onPick).toHaveBeenCalledWith("bob@contoso.com");
    expect(onSubmit).not.toHaveBeenCalled();
    expect(input().value).toBe("bob@contoso.com");
    expect(options()).toEqual([]);
    expect(input().getAttribute("aria-expanded")).toBe("false");
  });

  it("closes on Escape and on leaving the field, and the arrows open it again from the top", async () => {
    const { container, input, options, press } = await picker();
    await typeAndWait(input(), "ali");
    expect(options()).toEqual(["Alice Examplealice@contoso.com"]);

    await press("ArrowDown");
    expect(await press("Escape")).toBe(false);
    expect(options()).toEqual([]);
    await press("ArrowUp");
    expect(options()).toEqual(["Alice Examplealice@contoso.com"]);
    expect(container.querySelector('[aria-selected="true"]')?.textContent).toBe("Alice Examplealice@contoso.com");

    await settleAfter(() => input().dispatchEvent(new FocusEvent("focusout", { bubbles: true })));
    expect(options()).toEqual([]);
  });

  it("picks with the mouse, the field keeping the focus until the click", async () => {
    const { container, input, onPick } = await picker();
    await typeAndWait(input(), "dav");
    const option = container.querySelector('[role="option"]') as HTMLElement;

    await settleAfter(() => option.dispatchEvent(new MouseEvent("mouseover", { bubbles: true })));
    expect(option.getAttribute("aria-selected")).toBe("true");
    const down = new MouseEvent("mousedown", { bubbles: true, cancelable: true });
    await settleAfter(() => option.dispatchEvent(down));
    expect(down.defaultPrevented).toBe(true);
    await settleAfter(() => option.dispatchEvent(new MouseEvent("click", { bubbles: true })));

    expect(onPick).toHaveBeenCalledWith("dave@contoso.com");
    expect(input().value).toBe("dave@contoso.com");
  });

  it("leaves Enter to the form when no user is highlighted, so a full address typed by hand still works", async () => {
    const { input, press, onPick } = await picker();
    await typeAndWait(input(), "frank@contoso.com");

    expect(await press("Enter")).toBe(false);
    expect(await press("ArrowDown")).toBe(false);
    expect(onPick).not.toHaveBeenCalled();
  });

  it("says when nothing matches, and when the computer has no snapshot", async () => {
    const empty = await picker();
    await typeAndWait(empty.input(), "zzz");
    expect(empty.options()).toEqual([]);
    expect(empty.note()).toBe('No user in the snapshot starts with "zzz": a full address still works.');
    await empty.unmount();

    const none = await picker(new MockTransport({ snapshot: false }));
    await typeAndWait(none.input(), "dav");
    expect(none.note()).toBe("No snapshot on this computer to suggest from: type the full address.");
    expect(await none.press("ArrowDown")).toBe(false);
  });

  it("says when more users match than it lists, and names a user without a display name by the address", async () => {
    const transport = new ScriptedTransport(() => ({
      type: MessageType.userMatches,
      data: {
        snapshot: "s",
        users: [{ mail: "room@contoso.com" }, { objectId: "00000000-0000-0000-0000-000000000099" }],
        more: true,
      },
    }));
    const { input, options, note } = await picker(transport);

    await typeAndWait(input(), "roo");

    expect(options()).toEqual(["room@contoso.com", "00000000-0000-0000-0000-000000000099"]);
    expect(note()).toBe("More users match: type more of the name.");
  });

  it("drops the answer to an older text, shows no list after a failed search, and clears it under three letters", async () => {
    const transport = new ScriptedTransport(() => null);
    const { input, options, note } = await picker(transport);
    const answer = (index: number, reply: { type: string; data: unknown }) =>
      settleAfter(() => transport.push({ aegis: PROTOCOL_VERSION, id: transport.sent[index].id, ...reply }));
    const matches = (name: string) => ({
      type: MessageType.userMatches,
      data: { snapshot: "s", users: [{ displayName: name, userPrincipalName: `${name}@contoso.com` }], more: false },
    });

    await typeAndWait(input(), "abc");
    await typeAndWait(input(), "abcd");
    expect(searches(transport)).toEqual([{ text: "abc" }, { text: "abcd" }]);
    await answer(0, matches("old"));
    expect(options()).toEqual([]);
    await answer(1, matches("new"));
    expect(options()).toEqual(["newnew@contoso.com"]);

    await typeAndWait(input(), "abcde");
    await answer(2, { type: MessageType.error, data: { code: "auth", message: "no profile" } });
    expect(options()).toEqual([]);
    expect(note()).toBeNull();
    expect(input().getAttribute("aria-expanded")).toBe("false");

    await typeAndWait(input(), "abcdef");
    await answer(3, matches("again"));
    await type(input(), "ab");
    expect(options()).toEqual([]);
    expect(searches(transport)).toHaveLength(4);
  });

  it("sends nothing once the card starts its lookup, or once the page is gone", async () => {
    const transport = new ScriptedTransport(() => null);
    const busy = await picker(transport);
    await type(busy.input(), "exa");
    await busy.disable(true);
    await settle(DEBOUNCE_MS * 2);
    expect(searches(transport)).toEqual([]);
    await busy.disable(false);
    await busy.unmount();

    const gone = await picker(transport);
    await type(gone.input(), "exa");
    await gone.unmount();
    await settle(DEBOUNCE_MS * 2);
    expect(searches(transport)).toEqual([]);
  });

  it("hands the card the user principal name, else the address, else the object id", () => {
    expect(pickedAddress({ userPrincipalName: "a@contoso.com", mail: "b@contoso.com" })).toBe("a@contoso.com");
    expect(pickedAddress({ mail: "b@contoso.com", objectId: "00000000-0000-0000-0000-000000000001" })).toBe(
      "b@contoso.com",
    );
    expect(pickedAddress({ objectId: "00000000-0000-0000-0000-000000000001" })).toBe(
      "00000000-0000-0000-0000-000000000001",
    );
    expect(pickedAddress({})).toBe("");
  });
});
