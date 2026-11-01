import { readdirSync, readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import {
  type BridgeMessage,
  MessageType,
  notification,
  PROTOCOL_VERSION,
  parseMessage,
  readNotification,
  readReply,
  request,
  writeMessage,
  writeRequests,
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
      "access-report.json",
      "access.json",
      "apply-plan.json",
      "device-code.json",
      "error.json",
      "group-members.json",
      "logged-out.json",
      "login.json",
      "logout.json",
      "mailbox-access-grant.json",
      "mailbox-access-revoke.json",
      "mailbox-access.json",
      "mailbox-forwarding-clear.json",
      "mailbox-forwarding-set.json",
      "mailbox-holders.json",
      "mailbox-list.json",
      "mailboxes.json",
      "members.json",
      "no-snapshot.json",
      "open-sign-in-page.json",
      "ping.json",
      "plan-applied.json",
      "plan.group-owners-remove.json",
      "plan.json",
      "pong.json",
      "ready.json",
      "sign-in-page-opened.json",
      "signed-in.json",
      "signed-out.json",
      "user-grants.json",
      "user-matches.json",
      "user-search.json",
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
    expect(reply.data.hint).toBe("choose Sign in on the sign-in card to sign in again");
  });

  it("writes the open-sign-in-page request without a payload and reads its reply sample (task 047)", () => {
    expect(JSON.parse(writeMessage(request(MessageType.openSignInPage, undefined, "9")))).toEqual(
      JSON.parse(sample("open-sign-in-page.json")),
    );
    expect(readReply(parsed("sign-in-page-opened.json"))).toEqual({
      type: "sign-in-page-opened",
      data: { verificationUri: "https://login.microsoftonline.com/common/oauth2/deviceauth" },
    });
    expect(readReply({ aegis: PROTOCOL_VERSION, id: "9", type: "sign-in-page-opened", data: {} })).toBeNull();
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
  it("writes the login, logout and mailbox requests as the samples have them", () => {
    const same = (message: BridgeMessage, name: string) =>
      expect(JSON.parse(writeMessage(message))).toEqual(JSON.parse(sample(name)));
    same(request(MessageType.login, undefined, "5"), "login.json");
    same(request(MessageType.logout, undefined, "6"), "logout.json");
    same(request(MessageType.mailboxList, { shared: true }, "7"), "mailbox-list.json");
    same(request(MessageType.mailboxAccess, { mailbox: "finance@contoso.com" }, "8"), "mailbox-access.json");
  });

  it("reads the device-code sample as a notification of the host, never as a reply", () => {
    const message = parsed("device-code.json");
    expect(message.id).toBeUndefined();
    expect(readReply(message)).toBeNull();
    const read = readNotification(message);
    expect(read?.type).toBe(MessageType.deviceCode);
    expect(read?.data.userCode).toBe("ABCD1234");
    expect(read?.data.verificationUri).toBe("https://login.microsoftonline.com/common/oauth2/deviceauth");
    // A reply (with an id) or a payload of another shape is not a notification.
    expect(readNotification({ ...message, id: "1" })).toBeNull();
    expect(readNotification({ ...message, data: { userCode: "X" } })).toBeNull();
  });

  it("reads the logged-out sample", () => {
    expect(readReply(parsed("logged-out.json"))).toEqual({
      type: "logged-out",
      data: { profile: "work", account: "alice@contoso.com", removedAccounts: 1 },
    });
  });

  it("reads the mailboxes sample: the shared mailboxes by name", () => {
    const reply = readReply(parsed("mailboxes.json"));
    expect(reply?.type).toBe(MessageType.mailboxes);
    if (reply?.type !== MessageType.mailboxes) return;
    expect(reply.data.sharedOnly).toBe(true);
    expect(reply.data.mailboxes.map((mailbox) => mailbox.displayName)).toEqual(["Finance", "Reception"]);
  });

  it("reads the mailbox-holders sample: a right per holder, each with the layer that read it (task 063)", () => {
    const reply = readReply(parsed("mailbox-holders.json"));
    expect(reply?.type).toBe(MessageType.mailboxHolders);
    if (reply?.type !== MessageType.mailboxHolders) return;
    expect(reply.data.mailbox.primarySmtpAddress).toBe("finance@contoso.com");
    expect(reply.data.mailboxLayer).toBe("admin-api");
    expect(reply.data.holders.map((holder) => [holder.right, holder.holder, holder.address, holder.layer])).toEqual([
      ["full-access", "alice@contoso.com", "alice@contoso.com", "pwsh"],
      ["send-as", "alice@contoso.com", "alice@contoso.com", "pwsh"],
      ["send-on-behalf", "Bob Example", "bob@contoso.com", "admin-api"],
    ]);
  });

  it("reads a mailbox-holders reply without layers, as a host before task 063 sends it", () => {
    const finance = { displayName: "Finance", primarySmtpAddress: "finance@contoso.com", type: "SharedMailbox" };
    const reply = readReply({
      aegis: PROTOCOL_VERSION,
      id: "8",
      type: "mailbox-holders",
      data: { mailbox: finance, holders: [{ right: "full-access", holder: "alice@contoso.com" }] },
    });
    expect(reply?.type === MessageType.mailboxHolders && reply.data.mailboxLayer).toBeUndefined();
  });

  it("reads the user-grants sample: the mailboxes the user holds a right on, nothing failed", () => {
    const reply = readReply(parsed("user-grants.json"));
    expect(reply?.type).toBe(MessageType.userGrants);
    if (reply?.type !== MessageType.userGrants) return;
    expect(reply.data.user).toBe("bob@contoso.com");
    expect(reply.data.grants.map((grant) => `${grant.right} ${grant.mailbox.displayName}`)).toEqual([
      "send-on-behalf Finance",
      "full-access Reception",
      "send-as Reception",
    ]);
    expect(reply.data.mailboxesScanned).toBe(6);
    expect(reply.data.failed).toEqual([]);
  });

  it("refuses a mailbox reply whose payload does not have its type's shape", () => {
    const reply = (type: string, data: unknown) => readReply({ aegis: PROTOCOL_VERSION, id: "9", type, data });
    const finance = { displayName: "Finance", primarySmtpAddress: "finance@contoso.com", type: "SharedMailbox" };
    expect(reply("mailboxes", { mailboxes: [finance] })).toBeNull();
    expect(reply("mailbox-holders", { mailbox: finance, holders: [{ right: "owner", holder: "x" }] })).toBeNull();
    expect(
      reply("mailbox-holders", { mailbox: finance, holders: [{ right: "send-as", holder: "x", layer: "smtp" }] }),
    ).toBeNull();
    expect(reply("mailbox-holders", { mailbox: finance, holders: [], mailboxLayer: 42 })).toBeNull();
    expect(reply("user-grants", { user: "x", grants: [], mailboxesScanned: 1 })).toBeNull();
    expect(reply("logged-out", { profile: "work", account: "x" })).toBeNull();
  });

  it("writes the write requests and the apply as the samples have them (task 082)", () => {
    const same = (message: BridgeMessage, name: string) =>
      expect(JSON.parse(writeMessage(message))).toEqual(JSON.parse(sample(name)));
    same(
      request(
        MessageType.mailboxAccessGrant,
        { mailbox: "finance@contoso.com", users: ["bob@contoso.com"], rights: ["full-access"], noAutoMapping: true },
        "10",
      ),
      "mailbox-access-grant.json",
    );
    same(
      request(
        MessageType.mailboxAccessRevoke,
        { mailbox: "finance@contoso.com", users: ["alice@contoso.com"], rights: ["send-as"] },
        "12",
      ),
      "mailbox-access-revoke.json",
    );
    same(
      request(
        MessageType.mailboxForwardingSet,
        { mailbox: "alice@contoso.com", to: "alice@fabrikam.com", keepCopy: true },
        "13",
      ),
      "mailbox-forwarding-set.json",
    );
    same(
      request(MessageType.mailboxForwardingClear, { mailbox: "bob@contoso.com" }, "14"),
      "mailbox-forwarding-clear.json",
    );
    same(
      request(MessageType.applyPlan, { planId: "0123456789abcdef0123456789abcdef", confirm: true }, "11"),
      "apply-plan.json",
    );
  });

  it("names the write requests the samples hold, as the host marks them", () => {
    expect(writeRequests.map((type) => parsed(`${type}.json`).type)).toEqual([...writeRequests]);
    expect([...writeRequests].sort()).toEqual([
      "mailbox-access-grant",
      "mailbox-access-revoke",
      "mailbox-forwarding-clear",
      "mailbox-forwarding-set",
    ]);
  });

  it("reads the plan sample: the id to confirm and the plan as --dry-run prints it", () => {
    const reply = readReply(parsed("plan.json"));
    expect(reply?.type).toBe(MessageType.plan);
    if (reply?.type !== MessageType.plan) return;
    expect(reply.data.planId).toBe("0123456789abcdef0123456789abcdef");
    expect(reply.data.plan.mode).toBe("dry-run");
    expect(
      reply.data.plan.changes.map((change) => [change.action, change.detail, change.current, change.desired]),
    ).toEqual([
      ["add", "FullAccess for Bob Example (bob@contoso.com), without automapping", "no FullAccess", "holds FullAccess"],
    ]);
  });

  it("reads the plan-applied sample: each change's result and the record", () => {
    const reply = readReply(parsed("plan-applied.json"));
    expect(reply?.type).toBe(MessageType.planApplied);
    if (reply?.type !== MessageType.planApplied) return;
    expect(reply.data.error).toBeUndefined();
    expect(reply.data.plan.mode).toBe("applied");
    expect(reply.data.plan.changes[0].result).toEqual({ status: "applied" });
    expect(reply.data.plan.record).toMatch(/plans.20261030T120000\.000Z-mailbox-access-grant\.json$/);
  });

  it("reads a plan with nothing to change, and an applied plan with a failure", () => {
    const reply = (type: string, data: unknown) => readReply({ aegis: PROTOCOL_VERSION, id: "9", type, data });
    const empty = {
      operation: "mailbox-forwarding-clear",
      title: "Clear",
      changes: [],
      warnings: [],
      mode: "up-to-date",
    };
    const unchanged = [{ target: "Alice", detail: "the forwarding", state: "no forwarding", row: 2 }];
    expect(reply("plan", { plan: { ...empty, unchanged } })?.type).toBe(MessageType.plan);
    const failed = {
      action: "set",
      target: "Alice",
      detail: "forwarding to x@fabrikam.com",
      current: "no forwarding",
      desired: "forwards to x@fabrikam.com, keeps a copy",
      row: 3,
      result: { status: "failed", error: { code: "cmdlet-failed", message: "refused" } },
    };
    const applied = { ...empty, unchanged: [], changes: [failed], mode: "applied", record: "r.json" };
    expect(reply("plan-applied", { plan: applied, error: { code: "cmdlet-failed", message: "refused" } })?.type).toBe(
      MessageType.planApplied,
    );
  });

  it("refuses a plan reply whose payload does not have its type's shape", () => {
    const reply = (type: string, data: unknown) => readReply({ aegis: PROTOCOL_VERSION, id: "9", type, data });
    const plan = { operation: "x", title: "y", changes: [], unchanged: [], warnings: [], mode: "dry-run" };
    const change = { action: "add", target: "t", detail: "d", current: "c", desired: "e" };
    expect(reply("plan", null)).toBeNull();
    expect(reply("plan", { planId: 1, plan })).toBeNull();
    expect(reply("plan", { plan: { ...plan, mode: "maybe" } })).toBeNull();
    expect(reply("plan", { plan: { ...plan, warnings: [1] } })).toBeNull();
    expect(reply("plan", { plan: { ...plan, changes: [{ ...change, action: "move" }] } })).toBeNull();
    expect(reply("plan", { plan: { ...plan, changes: [{ ...change, result: { status: "done" } }] } })).toBeNull();
    expect(
      reply("plan", { plan: { ...plan, changes: [{ ...change, result: { status: "failed", error: {} } }] } }),
    ).toBeNull();
    expect(reply("plan", { plan: { ...plan, unchanged: [{ target: "t", detail: "d" }] } })).toBeNull();
    expect(reply("plan-applied", { plan: { ...plan, record: 7 } })).toBeNull();
    expect(reply("plan-applied", { plan, error: { code: "partial" } })).toBeNull();
    expect(reply("plan-applied", "no")).toBeNull();
  });

  it("writes the access request as its sample, and reads the access-report and no-snapshot samples (task 102)", () => {
    expect(JSON.parse(writeMessage(request(MessageType.access, { user: "dave@contoso.com" }, "15")))).toEqual(
      JSON.parse(sample("access.json")),
    );
    const report = readReply(parsed("access-report.json"));
    expect(report?.type).toBe(MessageType.accessReport);
    if (report?.type !== MessageType.accessReport) return;
    expect(report.data.user.userPrincipalName).toBe("dave@contoso.com");
    expect(report.data.groups.map((group) => [group.displayName, group.direct, group.via])).toEqual([
      ["Finance", false, ["Finance EU"]],
      ["Finance EU", true, []],
      ["Finance Staff", false, ["Finance EU"]],
      ["Project Phoenix", true, []],
    ]);
    expect(report.data.everyone).toEqual({
      organizationLinks: 1,
      anonymousLinks: 1,
      defaultCalendars: 1,
      anonymousCalendars: 1,
    });
    expect(readReply(parsed("no-snapshot.json"))).toEqual({
      type: MessageType.noSnapshot,
      data: JSON.parse(sample("no-snapshot.json")).data,
    });
  });

  it("writes the user search as its sample, and reads the user-matches sample (task 112)", () => {
    expect(JSON.parse(writeMessage(request(MessageType.userSearch, { text: "exa" }, "17")))).toEqual(
      JSON.parse(sample("user-search.json")),
    );
    const matches = readReply(parsed("user-matches.json"));
    expect(matches?.type).toBe(MessageType.userMatches);
    if (matches?.type !== MessageType.userMatches) return;
    expect(matches.data.more).toBe(false);
    expect(matches.data.users.map((user) => user.displayName)).toEqual([
      "Alice Example",
      "Bob Example",
      "Carol Example",
      "Dave Example",
      "Eve Example",
    ]);
  });

  it("refuses a user-matches reply whose payload does not have its type's shape", () => {
    const reply = (data: unknown) => readReply({ aegis: PROTOCOL_VERSION, id: "17", type: "user-matches", data });
    const matches = JSON.parse(sample("user-matches.json")).data;
    expect(reply(null)).toBeNull();
    expect(reply({ ...matches, snapshot: 1 })).toBeNull();
    expect(reply({ ...matches, more: "no" })).toBeNull();
    expect(reply({ ...matches, users: {} })).toBeNull();
    expect(reply({ ...matches, users: [{ mail: 7 }] })).toBeNull();
    expect(reply({ ...matches, users: [] })?.type).toBe(MessageType.userMatches);
  });

  it("refuses an access reply whose payload does not have its type's shape", () => {
    const reply = (type: string, data: unknown) => readReply({ aegis: PROTOCOL_VERSION, id: "9", type, data });
    const report = JSON.parse(sample("access-report.json")).data;
    expect(reply("access-report", report)?.type).toBe(MessageType.accessReport);
    expect(reply("access-report", null)).toBeNull();
    expect(reply("access-report", { ...report, user: "dave" })).toBeNull();
    expect(reply("access-report", { ...report, everyone: undefined })).toBeNull();
    expect(reply("access-report", { ...report, snapshot: 1 })).toBeNull();
    expect(reply("access-report", { ...report, taken: undefined })).toBeNull();
    expect(reply("access-report", { ...report, ageHours: "2" })).toBeNull();
    expect(reply("access-report", { ...report, user: { mail: 7 } })).toBeNull();
    expect(reply("access-report", { ...report, groups: {} })).toBeNull();
    expect(reply("access-report", { ...report, groups: [{ via: "Finance EU", direct: false }] })).toBeNull();
    expect(reply("access-report", { ...report, groups: [{ via: [], direct: "yes" }] })).toBeNull();
    expect(reply("access-report", { ...report, groups: [{ via: [], direct: true, kind: 3 }] })).toBeNull();
    expect(reply("access-report", { ...report, sites: [{ name: "Finance" }] })).toBeNull();
    expect(reply("access-report", { ...report, sites: [null] })).toBeNull();
    expect(
      reply("access-report", { ...report, sharedItems: [{ site: "Finance", kind: "link", roles: 1 }] }),
    ).toBeNull();
    expect(reply("access-report", { ...report, mailboxes: [{ right: "owner" }] })).toBeNull();
    expect(reply("access-report", { ...report, calendars: [{ folder: "Calendar" }] })).toBeNull();
    expect(reply("access-report", { ...report, everyone: { ...report.everyone, anonymousLinks: "1" } })).toBeNull();
    expect(reply("access-report", { ...report, warnings: [1] })).toBeNull();
    expect(reply("no-snapshot", { profile: "contoso", message: "none" })).toBeNull();
    expect(reply("no-snapshot", "none")).toBeNull();
  });
});
