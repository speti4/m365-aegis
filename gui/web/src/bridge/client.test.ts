import { describe, expect, it } from "vitest";
import { ScriptedTransport } from "../test/scripted-transport";
import { BridgeClient } from "./client";
import { MessageType, PROTOCOL_VERSION } from "./protocol";

/** The client's own corners (task 070); the round trips are in transport.test.ts. */
describe("the bridge client", () => {
  it("turns a pong whose payload is not a pong's into an error", async () => {
    const client = new BridgeClient(new ScriptedTransport(() => ({ type: MessageType.pong, data: { version: 1 } })));

    const reply = await client.call(MessageType.ping);

    expect(reply).toEqual({
      type: MessageType.error,
      data: { code: "bad-reply", message: "the window answered 'ping' with a 'pong' the page cannot read" },
    });
  });

  it("sends a notification with its payload", () => {
    const transport = new ScriptedTransport();

    new BridgeClient(transport).notify(MessageType.ready, { page: "main" });

    expect(transport.sent).toEqual([{ aegis: PROTOCOL_VERSION, type: MessageType.ready, data: { page: "main" } }]);
  });

  it("stops handing notifications to a handler once it is unregistered", () => {
    const transport = new ScriptedTransport();
    const client = new BridgeClient(transport);
    const seen: string[] = [];
    const stop = client.onNotification((notification) => seen.push(notification.data.userCode));
    const deviceCode = {
      aegis: PROTOCOL_VERSION,
      type: MessageType.deviceCode,
      data: {
        verificationUri: "https://login.microsoftonline.com/common/oauth2/deviceauth",
        userCode: "ABCD1234",
        message: "enter ABCD1234",
        expiresOn: "2026-10-29T14:15:00+00:00",
      },
    };

    transport.push(deviceCode);
    stop();
    transport.push(deviceCode);
    // A reply nobody waits for is dropped.
    transport.push({
      aegis: PROTOCOL_VERSION,
      id: "nobody",
      type: MessageType.pong,
      data: { product: "m365-aegis", version: "1" },
    });

    expect(seen).toEqual(["ABCD1234"]);
  });
});
