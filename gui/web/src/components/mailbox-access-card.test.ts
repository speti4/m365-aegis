import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import { BridgeClient } from "../bridge/client";
import { MockTransport } from "../bridge/mock-transport";
import { type MailboxAccess, MessageType, parseMessage, readReply } from "../bridge/protocol";
import { describeLayers, layerNames } from "./mailbox-access-card";

// The reply sample the C# tests hold the host's answer to (tests/contracts/bridge/mailbox-holders.json).
const contracts = path.resolve(__dirname, "../../../../tests/contracts/bridge");
const holdersSample = (): MailboxAccess => {
  const message = parseMessage(readFileSync(path.join(contracts, "mailbox-holders.json"), "utf8"));
  const reply = message ? readReply(message) : null;
  if (reply?.type !== MessageType.mailboxHolders) {
    throw new Error("mailbox-holders.json is not a mailbox-holders reply");
  }
  return reply.data;
};

describe("the access card's layers (task 063)", () => {
  it("names every layer of the sample's rows", () => {
    expect(holdersSample().holders.map((holder) => holder.layer && layerNames[holder.layer])).toEqual([
      "PowerShell",
      "PowerShell",
      "Admin API",
    ]);
  });

  it("says the Admin API read the mailbox and its delegates beside the session's rights", () => {
    expect(describeLayers(holdersSample())).toBe(
      "The mailbox and its Send on Behalf delegates by name come from the Admin API, FullAccess and Send As from the PowerShell session.",
    );
  });

  it("says the session read everything when the Admin API did not answer", () => {
    const access: MailboxAccess = { ...holdersSample(), mailboxLayer: "pwsh" };
    expect(describeLayers(access)).toMatch(/^Everything comes from the PowerShell session/);
  });

  it("says nothing when the reply names no layer", () => {
    const { mailboxLayer: _, ...withoutLayer } = holdersSample();
    expect(describeLayers(withoutLayer)).toBeNull();
  });

  it("shows the sample's rows in the browser mock, layers and all", async () => {
    const client = new BridgeClient(new MockTransport());
    const reply = await client.call(MessageType.mailboxAccess, { mailbox: "finance@contoso.com" });
    expect(reply.type === MessageType.mailboxHolders && reply.data).toEqual(holdersSample());
  });
});
