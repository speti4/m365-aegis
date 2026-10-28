import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import { notification, PROTOCOL_VERSION, parseMessage, request, writeMessage } from "./protocol";

// The same samples the C# tests read (tests/contracts/bridge), so the two sides agree on the shape.
const contracts = path.resolve(__dirname, "../../../../tests/contracts/bridge");
const sample = (name: string) => readFileSync(path.join(contracts, name), "utf8");

describe("the bridge protocol", () => {
  it("parses the ping sample as a request", () => {
    const message = parseMessage(sample("ping.json"));
    expect(message).toEqual({ aegis: PROTOCOL_VERSION, id: "1", type: "ping", data: undefined });
  });

  it("parses the ready sample as a notification without an id", () => {
    const message = parseMessage(sample("ready.json"));
    expect(message?.type).toBe("ready");
    expect(message?.id).toBeUndefined();
  });

  it("parses the pong sample with its payload", () => {
    const message = parseMessage(sample("pong.json"));
    expect(message?.type).toBe("pong");
    expect(message?.data).toEqual({ product: "m365-aegis", version: "0.1.0" });
  });

  it("refuses foreign text", () => {
    for (const text of ["", "not json", '{"aegis":2,"type":"ping"}', '{"type":"ping"}', '{"aegis":1}']) {
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
