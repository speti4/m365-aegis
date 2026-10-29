import { readdirSync, readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import {
  MessageType,
  notification,
  PROTOCOL_VERSION,
  parseMessage,
  readReply,
  request,
  writeMessage,
} from "./protocol";

// The same samples the C# tests read (tests/contracts/bridge), so the two sides agree on the shape: the C# tests
// hold the core's replies to exactly these messages.
const contracts = path.resolve(__dirname, "../../../../tests/contracts/bridge");
const sample = (name: string) => readFileSync(path.join(contracts, name), "utf8");
const parsed = (name: string) => {
  const message = parseMessage(sample(name));
  if (!message) {
    throw new Error(`${name} is not a message of the protocol`);
  }
  return message;
};

describe("the bridge protocol", () => {
  it("knows every sample", () => {
    expect(
      readdirSync(contracts)
        .filter((name) => name.endsWith(".json"))
        .sort(),
    ).toEqual([
      "error.json",
      "group-members.json",
      "members.json",
      "ping.json",
      "pong.json",
      "ready.json",
      "signed-in.json",
      "signed-out.json",
      "whoami.json",
    ]);
  });

  it("parses the ping sample as a request", () => {
    const message = parseMessage(sample("ping.json"));
    expect(message).toEqual({ aegis: PROTOCOL_VERSION, id: "1", type: "ping", data: undefined });
  });

  it("parses the ready sample as a notification without an id", () => {
    const message = parseMessage(sample("ready.json"));
    expect(message?.type).toBe(MessageType.ready);
    expect(message?.id).toBeUndefined();
  });

  it("reads the pong sample with its payload", () => {
    expect(readReply(parsed("pong.json"))).toEqual({
      type: "pong",
      data: { product: "m365-aegis", version: "0.1.0" },
    });
  });

  it("writes the whoami and group-members requests as the samples have them", () => {
    expect(JSON.parse(writeMessage(request(MessageType.whoami, undefined, "2")))).toEqual(
      JSON.parse(sample("whoami.json")),
    );
    expect(JSON.parse(writeMessage(request(MessageType.groupMembers, { group: "Finance" }, "3")))).toEqual(
      JSON.parse(sample("group-members.json")),
    );
  });

  it("reads the signed-in sample: the account, the profile and a token per resource", () => {
    const reply = readReply(parsed("signed-in.json"));
    expect(reply?.type).toBe(MessageType.signedIn);
    if (reply?.type !== MessageType.signedIn) return;
    expect(reply.data.account).toBe("alice@contoso.com");
    expect(reply.data.profile).toBe("work");
    expect(reply.data.tokens.map((token) => token.resource)).toEqual(["graph", "exchange"]);
  });

  it("reads the signed-out sample with the profile and the hint", () => {
    const reply = readReply(parsed("signed-out.json"));
    expect(reply?.type).toBe(MessageType.signedOut);
    if (reply?.type !== MessageType.signedOut) return;
    expect(reply.data.profile).toBe("work");
    expect(reply.data.hint).toBe("run `m365-aegis login`");
  });

  it("reads the members sample: the group and its members in order", () => {
    const reply = readReply(parsed("members.json"));
    expect(reply?.type).toBe(MessageType.members);
    if (reply?.type !== MessageType.members) return;
    expect(reply.data.group).toEqual({
      id: "00000000-0000-0000-0000-000000000031",
      displayName: "Finance",
      kind: "microsoft365",
      mail: "finance@contoso.com",
      synced: false,
    });
    expect(reply.data.members.map((member) => member.displayName)).toEqual([
      "Alice Example",
      "Bob Example",
      "Carol Example",
      "Finance EU",
    ]);
    expect(reply.data.members[2].mail).toBeUndefined();
  });

  it("reads the error sample: the code, the message and the hint", () => {
    expect(readReply(parsed("error.json"))).toEqual({
      type: "error",
      data: {
        code: "not-found",
        message: "no group named 'Nothing'",
        hint: "give the exact display name, the address, the object id, or a word of the name",
      },
    });
  });

  it("refuses a reply whose payload does not have its type's shape", () => {
    const reply = (type: string, data: unknown) => readReply({ aegis: PROTOCOL_VERSION, id: "9", type, data });
    expect(reply("signed-in", { profile: "work" })).toBeNull();
    expect(
      reply("members", { group: { id: "1", displayName: "x", kind: "team", synced: false }, members: [] }),
    ).toBeNull();
    expect(reply("members", { group: null, members: [] })).toBeNull();
    expect(reply("error", { code: "not-found" })).toBeNull();
    expect(reply("signed-out", "no")).toBeNull();
    expect(reply("frobnicate", {})).toBeNull();
  });

  it("refuses foreign text", () => {
    for (const text of ["", "not json", "null", '{"aegis":2,"type":"ping"}', '{"type":"ping"}', '{"aegis":1}']) {
      expect(parseMessage(text)).toBeNull();
    }
  });

  it("writes a request the host parses back", () => {
    const text = writeMessage(request("ping", undefined, "42"));
    expect(text).toBe('{"aegis":1,"id":"42","type":"ping"}');
    expect(parseMessage(text)?.id).toBe("42");
  });

  it("writes a notification without an id", () => {
    expect(writeMessage(notification("ready"))).toBe('{"aegis":1,"type":"ready"}');
  });
});
