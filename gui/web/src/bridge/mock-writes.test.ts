import { describe, expect, it } from "vitest";
import { BridgeClient } from "./client";
import { MockTransport } from "./mock-transport";
import { MessageType, type Reply } from "./protocol";

/** The browser mock's writes (task 082): planned as the core plans them, applied only with the confirmation. */
describe("the browser mock's writes", () => {
  const client = () => new BridgeClient(new MockTransport());
  const plan = (reply: Reply) => {
    if (reply.type !== MessageType.plan) {
      throw new Error(`not a plan: ${JSON.stringify(reply)}`);
    }
    return reply.data;
  };
  const error = (reply: Reply) => (reply.type === MessageType.error ? reply.data.message : `no error: ${reply.type}`);

  it("plans a grant of what is not held, and leaves what is", async () => {
    const ready = plan(
      await client().call(MessageType.mailboxAccessGrant, {
        mailbox: "Finance",
        users: ["alice@contoso.com", "bob@contoso.com"],
        rights: ["full-access"],
        noAutoMapping: true,
      }),
    );

    expect(ready.planId).toBe("mock-plan-1");
    expect(ready.plan.title).toBe("Grant mailbox rights on Finance (finance@contoso.com)");
    expect(ready.plan.changes.map((change) => [change.action, change.detail, change.current, change.desired])).toEqual([
      ["add", "FullAccess for Bob Example (bob@contoso.com), without automapping", "no FullAccess", "holds FullAccess"],
    ]);
    expect(ready.plan.unchanged.map((line) => line.state)).toEqual(["holds FullAccess already"]);
  });

  it("plans a revoke of what is held, and leaves what is not", async () => {
    const ready = plan(
      await client().call(MessageType.mailboxAccessRevoke, {
        mailbox: "reception@contoso.com",
        users: ["bob@contoso.com"],
        rights: ["send-as", "send-on-behalf"],
        noAutoMapping: true,
      }),
    );

    expect(ready.plan.changes.map((change) => [change.action, change.current, change.desired])).toEqual([
      ["remove", "holds Send As", "no Send As"],
    ]);
    expect(ready.plan.unchanged.map((line) => line.state)).toEqual(["no Send on Behalf"]);
  });

  it("answers a plan with nothing to change without an id", async () => {
    const ready = plan(
      await client().call(MessageType.mailboxAccessGrant, {
        mailbox: "finance",
        users: ["alice@contoso.com"],
        rights: ["send-as"],
      }),
    );

    expect(ready.planId).toBeUndefined();
    expect(ready.plan.mode).toBe("up-to-date");
  });

  it("refuses a write that asks for nothing whole, as the host does", async () => {
    const mock = client();
    const grant = (data: unknown) => mock.call(MessageType.mailboxAccessGrant, data);

    expect(error(await grant({ mailbox: "finance", users: [" ", 7], rights: ["full-access"] }))).toBe(
      "no user was named",
    );
    expect(error(await grant({ mailbox: "finance", users: ["bob"], rights: "full-access" }))).toBe(
      "no right was chosen",
    );
    expect(error(await grant({ mailbox: "finance", users: ["bob"], rights: ["owner"] }))).toBe("unknown right 'owner'");
    expect(error(await grant({ mailbox: "finance", users: ["bob"], rights: ["send-as"], noAutoMapping: true }))).toBe(
      "without automapping goes with FullAccess",
    );
    expect(error(await grant({ mailbox: "archive", users: ["bob"], rights: ["send-as"] }))).toBe(
      "mailbox 'archive' not found",
    );
    expect(error(await grant({ mailbox: "finance", users: ["dave@contoso.com"], rights: ["send-as"] }))).toBe(
      "recipient 'dave@contoso.com' not found",
    );
  });

  it("plans a forwarding set and clear with the core's warnings", async () => {
    const mock = client();
    const set = (data: unknown) => mock.call(MessageType.mailboxForwardingSet, data);

    const outside = plan(await set({ mailbox: "alice@contoso.com", to: "alice@fabrikam.com", keepCopy: false }));
    const inside = plan(await set({ mailbox: "alice@contoso.com", to: "finance@contoso.com", keepCopy: true }));
    const same = plan(await set({ mailbox: "bob@contoso.com", to: "bob@fabrikam.com", keepCopy: true }));
    const other = plan(await set({ mailbox: "bob@contoso.com", to: "bob@fabrikam.com" }));
    const clear = plan(await mock.call(MessageType.mailboxForwardingClear, { mailbox: "bob@contoso.com" }));
    const none = plan(await mock.call(MessageType.mailboxForwardingClear, { mailbox: "alice@contoso.com" }));

    expect(outside.plan.changes[0].desired).toBe("forwards to alice@fabrikam.com, keeps no copy");
    expect(outside.plan.warnings).toHaveLength(2);
    expect(inside.plan.warnings).toEqual([]);
    expect(same.plan.unchanged[0].state).toBe("forwards to bob@fabrikam.com, keeps a copy already");
    expect(other.plan.changes[0].current).toBe("forwards to bob@fabrikam.com, keeps a copy");
    expect(clear.plan.changes[0]).toMatchObject({ action: "clear", desired: "no forwarding" });
    expect(clear.plan.title).toBe("Clear the forwarding of Bob Example (bob@contoso.com)");
    expect(none.plan.unchanged[0].state).toBe("no forwarding");
    expect(error(await set({ mailbox: "alice@contoso.com", to: " " }))).toBe("the forwarding address is missing");
    expect(error(await set({ mailbox: "alice@contoso.com", to: "alice at fabrikam" }))).toBe(
      "the forwarding address: 'alice at fabrikam' is not an address",
    );
    expect(error(await set({ mailbox: "archive", to: "a@fabrikam.com" }))).toBe("mailbox 'archive' not found");
  });

  it("applies a held plan only with the confirmation, and only once", async () => {
    const mock = client();
    const ready = plan(await mock.call(MessageType.mailboxForwardingClear, { mailbox: "bob@contoso.com" }));
    const apply = (data: unknown) => mock.call(MessageType.applyPlan, data);

    const refused = await apply({ planId: ready.planId, confirm: "yes" });
    const applied = await apply({ planId: ready.planId, confirm: true });
    const again = await apply({ planId: ready.planId, confirm: true });
    const unnamed = await apply({ confirm: true });

    expect(refused).toMatchObject({ type: MessageType.error, data: { code: "refused-without-yes" } });
    expect(applied.type).toBe(MessageType.planApplied);
    if (applied.type !== MessageType.planApplied) return;
    expect(applied.data.error).toBeUndefined();
    expect(applied.data.plan.mode).toBe("applied");
    expect(applied.data.plan.changes[0].result).toEqual({ status: "applied" });
    expect(applied.data.plan.record).toMatch(/plans\\\d{8}T\d{6}\.\d{3}Z-mailbox-forwarding-clear\.json$/);
    expect(again).toMatchObject({ type: MessageType.error, data: { code: "usage" } });
    expect(unnamed).toMatchObject({ type: MessageType.error, data: { code: "usage" } });
  });

  it("fails a Send on Behalf change on Reception, so the page's partial result shows", async () => {
    const mock = client();
    const ready = plan(
      await mock.call(MessageType.mailboxAccessGrant, {
        mailbox: "reception",
        users: ["alice@contoso.com"],
        rights: ["send-as", "send-on-behalf"],
      }),
    );

    const applied = await mock.call(MessageType.applyPlan, { planId: ready.planId, confirm: true });

    expect(applied.type === MessageType.planApplied && applied.data.error?.code).toBe("partial");
    expect(
      applied.type === MessageType.planApplied && applied.data.plan.changes.map((change) => change.result?.status),
    ).toEqual(["applied", "failed"]);
  });

  it("drops the plans waiting for a confirmation at a sign-in and a sign-out", async () => {
    const mock = client();
    const first = plan(await mock.call(MessageType.mailboxForwardingClear, { mailbox: "bob@contoso.com" }));
    await mock.call(MessageType.logout);
    const afterSignOut = await mock.call(MessageType.applyPlan, { planId: first.planId, confirm: true });
    const second = plan(await mock.call(MessageType.mailboxForwardingClear, { mailbox: "bob@contoso.com" }));
    await mock.call(MessageType.login);
    const afterSignIn = await mock.call(MessageType.applyPlan, { planId: second.planId, confirm: true });

    expect(afterSignOut).toMatchObject({ type: MessageType.error, data: { code: "usage" } });
    expect(afterSignIn).toMatchObject({ type: MessageType.error, data: { code: "usage" } });
  });
});
