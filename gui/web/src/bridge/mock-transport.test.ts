import { describe, expect, it } from "vitest";
import { BridgeClient } from "./client";
import { MockTransport } from "./mock-transport";
import { MessageType, request } from "./protocol";

/** The browser mock's corners the transport tests leave (task 070): the full reply of a user, the unknown ones. */
describe("the browser mock", () => {
  const client = () => new BridgeClient(new MockTransport());

  it("answers a user's grants without a failed list for anyone but bob", async () => {
    const reply = await client().call(MessageType.mailboxAccess, { user: "Alice Example" });

    expect(reply.type).toBe(MessageType.userGrants);
    if (reply.type !== MessageType.userGrants) return;
    expect(reply.data.failed).toEqual([]);
    expect(reply.data.grants.map((grant) => `${grant.right} ${grant.mailbox.displayName}`)).toEqual([
      "full-access Finance",
      "send-as Finance",
      "full-access Reception",
    ]);
  });

  it("does not know a user it has no mailbox for", async () => {
    const reply = await client().call(MessageType.mailboxAccess, { user: "dave@contoso.com" });

    expect(reply).toEqual({
      type: MessageType.error,
      data: {
        code: "not-found",
        message: "user 'dave@contoso.com' not found",
        hint: "check the name or the address and try again",
      },
    });
  });

  it("answers dave's access by his display name, and a missing user with a usage error (task 102)", async () => {
    expect((await client().call(MessageType.access, { user: "Dave Example" })).type).toBe(MessageType.accessReport);
    expect(await client().call(MessageType.access, {})).toEqual({
      type: MessageType.error,
      data: {
        code: "usage",
        message: "no user was named",
        hint: "give the user's user principal name, address or object id",
      },
    });
  });

  it("answers a request type it does not know with an error", async () => {
    const reply = await client().call("frobnicate");

    expect(reply).toEqual({ type: MessageType.error, data: { code: "no-host", message: "no host in the browser" } });
  });

  it("stops answering a handler once it is unregistered", async () => {
    const transport = new MockTransport();
    const seen: string[] = [];
    const stop = transport.onMessage((message) => seen.push(message.type));
    transport.send(request(MessageType.ping));
    await new Promise((resolve) => setTimeout(resolve, 0));

    stop();
    transport.send(request(MessageType.ping));
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(seen).toEqual([MessageType.pong]);
  });
});
