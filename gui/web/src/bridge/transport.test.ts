import { readFileSync } from "node:fs";
import path from "node:path";
import { afterEach, describe, expect, it, vi } from "vitest";
import { BridgeClient, SIGN_IN_TIMEOUT_MS } from "./client";
import { MockTransport } from "./mock-transport";
import { type BridgeMessage, MessageType, notification, PROTOCOL_VERSION, request } from "./protocol";
import { createTransport, findWebView, type WebViewBridge, WebViewTransport } from "./transport";

const contracts = path.resolve(__dirname, "../../../../tests/contracts/bridge");
const sample = (name: string) => readFileSync(path.join(contracts, name), "utf8").trim();

/** A stand-in for WebView2's `chrome.webview`: records what the page posts and lets the test deliver the host's posts. */
class FakeWebView implements WebViewBridge {
  posted: unknown[] = [];
  private listeners: Array<(event: { data: unknown }) => void> = [];

  postMessage(message: unknown): void {
    this.posted.push(message);
  }

  addEventListener(_type: "message", listener: (event: { data: unknown }) => void): void {
    this.listeners.push(listener);
  }

  removeEventListener(_type: "message", listener: (event: { data: unknown }) => void): void {
    this.listeners = this.listeners.filter((registered) => registered !== listener);
  }

  /** The host's PostWebMessageAsString: the page sees the string as the event's data. */
  hostPosts(data: unknown): void {
    for (const listener of this.listeners) {
      listener({ data });
    }
  }

  get listenerCount(): number {
    return this.listeners.length;
  }
}

afterEach(() => {
  vi.useRealTimers();
});

describe("the WebView2 transport", () => {
  it("is found on chrome.webview, and not in a plain browser", () => {
    const webview = new FakeWebView();
    expect(findWebView({ chrome: { webview } })).toBe(webview);
    expect(findWebView({ chrome: {} })).toBeNull();
    expect(findWebView({})).toBeNull();
    expect(findWebView(undefined)).toBeNull();
    expect(createTransport({ chrome: { webview } }).hosted).toBe(true);
    expect(createTransport({ chrome: {} }).hosted).toBe(false);
  });

  it("posts a message as its JSON text, as the host reads it", () => {
    const webview = new FakeWebView();
    new WebViewTransport(webview).send(request(MessageType.whoami, undefined, "2"));
    expect(webview.posted).toEqual(['{"aegis":1,"id":"2","type":"whoami"}']);
    expect(JSON.parse(webview.posted[0] as string)).toEqual(JSON.parse(sample("whoami.json")));
  });

  it("hands the host's string posts to the handler and drops anything else", () => {
    const webview = new FakeWebView();
    const received: BridgeMessage[] = [];
    const unsubscribe = new WebViewTransport(webview).onMessage((message) => received.push(message));

    webview.hostPosts(sample("signed-in.json"));
    webview.hostPosts({ aegis: 1, type: "pong" }); // an object, not the host's string
    webview.hostPosts("not json");
    webview.hostPosts('{"aegis":2,"type":"pong"}');
    expect(received.map((message) => message.type)).toEqual(["signed-in"]);

    unsubscribe();
    expect(webview.listenerCount).toBe(0);
  });
});

describe("the bridge client", () => {
  it("resolves each request with the reply that repeats its id, in any order", async () => {
    const webview = new FakeWebView();
    const client = new BridgeClient(new WebViewTransport(webview));

    const whoami = client.call(MessageType.whoami);
    const members = client.call(MessageType.groupMembers, { group: "Finance" });
    const [first, second] = webview.posted.map((text) => JSON.parse(text as string) as BridgeMessage);
    expect(first.type).toBe("whoami");
    expect(second.data).toEqual({ group: "Finance" });

    const reply = (name: string, id: string | undefined) => JSON.stringify({ ...JSON.parse(sample(name)), id });
    webview.hostPosts(reply("members.json", second.id));
    webview.hostPosts(reply("signed-in.json", "no-such-request"));
    webview.hostPosts(reply("signed-in.json", first.id));

    expect((await members).type).toBe("members");
    expect((await whoami).type).toBe("signed-in");
  });

  it("turns a reply it cannot read into an error", async () => {
    const webview = new FakeWebView();
    const client = new BridgeClient(new WebViewTransport(webview));

    const pending = client.call(MessageType.whoami);
    const { id } = JSON.parse(webview.posted[0] as string) as BridgeMessage;
    webview.hostPosts(JSON.stringify({ aegis: PROTOCOL_VERSION, id, type: "signed-in", data: { profile: 1 } }));

    const reply = await pending;
    expect(reply.type).toBe("error");
    expect(reply.type === "error" && reply.data.code).toBe("bad-reply");
  });

  it("gives up on a host that does not answer", async () => {
    vi.useFakeTimers();
    const client = new BridgeClient(new WebViewTransport(new FakeWebView()), 1000);

    const pending = client.call(MessageType.groupMembers, { group: "Finance" });
    vi.advanceTimersByTime(1000);

    const reply = await pending;
    expect(reply.type === "error" && reply.data.code).toBe("timeout");
  });

  it("sends a notification without an id", () => {
    const webview = new FakeWebView();
    new BridgeClient(new WebViewTransport(webview)).notify(MessageType.ready);
    expect(webview.posted).toEqual([JSON.stringify(notification("ready"))]);
  });
});

describe("the browser mock", () => {
  const client = () => new BridgeClient(new MockTransport());

  it("answers ping and whoami with replies of the protocol's shape", async () => {
    expect((await client().call(MessageType.ping)).type).toBe("pong");
    const whoami = await client().call(MessageType.whoami);
    expect(whoami.type).toBe("signed-in");
  });

  it("knows one group by name, address or id, and nothing else", async () => {
    for (const group of ["Finance", "finance@contoso.com", "00000000-0000-0000-0000-000000000031"]) {
      const reply = await client().call(MessageType.groupMembers, { group });
      expect(reply.type).toBe("members");
      expect(reply.type === "members" && reply.data.members.length).toBe(4);
    }
    const missing = await client().call(MessageType.groupMembers, { group: "Nothing" });
    expect(missing).toEqual({
      type: "error",
      data: {
        code: "not-found",
        message: "no group named 'Nothing'",
        hint: "give the exact display name, the address, the object id, or a word of the name",
      },
    });
    expect((await client().call(MessageType.groupMembers, { group: " " })).type).toBe("error");
  });

  it("does not answer a notification", async () => {
    vi.useFakeTimers();
    const transport = new MockTransport();
    const received: BridgeMessage[] = [];
    transport.onMessage((message) => received.push(message));
    transport.send(notification(MessageType.ready));
    vi.runAllTimers();
    expect(received).toEqual([]);
  });
});

describe("the host's notifications", () => {
  it("reach the notification handlers, never a pending request", async () => {
    const webview = new FakeWebView();
    const client = new BridgeClient(new WebViewTransport(webview));
    const seen: string[] = [];
    const unsubscribe = client.onNotification((notification) => seen.push(notification.data.userCode));

    const login = client.call(MessageType.login, undefined, 5000);
    const { id } = JSON.parse(webview.posted[0] as string) as BridgeMessage;
    webview.hostPosts(sample("device-code.json"));
    webview.hostPosts('{"aegis":1,"type":"device-code","data":{"userCode":"no"}}'); // not the shape: dropped
    webview.hostPosts(JSON.stringify({ ...JSON.parse(sample("signed-in.json")), id }));

    expect((await login).type).toBe("signed-in");
    expect(seen).toEqual(["ABCD1234"]);
    unsubscribe();
    webview.hostPosts(sample("device-code.json"));
    expect(seen).toEqual(["ABCD1234"]);
  });

  it("let a sign-in wait longer than a lookup", async () => {
    vi.useFakeTimers();
    const client = new BridgeClient(new WebViewTransport(new FakeWebView()), 1000);

    let settled = false;
    const login = client.call(MessageType.login, undefined, SIGN_IN_TIMEOUT_MS).then((reply) => {
      settled = true;
      return reply;
    });
    await vi.advanceTimersByTimeAsync(1000);
    expect(settled).toBe(false);
    await vi.advanceTimersByTimeAsync(SIGN_IN_TIMEOUT_MS);

    const reply = await login;
    expect(reply.type === "error" && reply.data.code).toBe("timeout");
  });
});

describe("the browser mock's sign-in and mailboxes", () => {
  it("starts signed out with ?mock=signed-out, and signs in through a device code", async () => {
    const { transport, hosted } = createTransport({ location: { search: "?mock=signed-out" } });
    expect(hosted).toBe(false);
    const client = new BridgeClient(transport);
    const codes: string[] = [];
    client.onNotification((notification) => codes.push(notification.data.userCode));

    expect((await client.call(MessageType.whoami)).type).toBe("signed-out");
    const login = await client.call(MessageType.login, { deviceCode: true });
    expect(login.type).toBe("signed-in");
    expect(codes).toEqual(["MOCK1234"]);
    expect((await client.call(MessageType.whoami)).type).toBe("signed-in");
  });

  it("answers open-sign-in-page while a device-code sign-in waits, and refuses it otherwise (task 047)", async () => {
    const client = new BridgeClient(new MockTransport({ signedIn: false }));
    const opened: Promise<unknown>[] = [];
    client.onNotification(() => opened.push(client.call(MessageType.openSignInPage)));

    const before = await client.call(MessageType.openSignInPage);
    expect(before.type === "error" && before.data.code).toBe("usage");
    expect((await client.call(MessageType.login, { deviceCode: true })).type).toBe("signed-in");
    expect(await Promise.all(opened)).toEqual([
      {
        type: "sign-in-page-opened",
        data: { verificationUri: "https://login.microsoftonline.com/common/oauth2/deviceauth" },
      },
    ]);
    const after = await client.call(MessageType.openSignInPage);
    expect(after.type === "error" && after.data.code).toBe("usage");
  });

  it("signs out with logout, and whoami says so", async () => {
    const client = new BridgeClient(new MockTransport());
    const logout = await client.call(MessageType.logout);
    expect(logout).toEqual({
      type: "logged-out",
      data: { profile: "work", account: "alice@contoso.com", removedAccounts: 1 },
    });
    expect((await client.call(MessageType.whoami)).type).toBe("signed-out");
  });

  it("lists the shared mailboxes and answers the access of a mailbox and of a user", async () => {
    const client = new BridgeClient(new MockTransport());

    const shared = await client.call(MessageType.mailboxList, { shared: true });
    expect(shared.type === "mailboxes" && shared.data.mailboxes.map((mailbox) => mailbox.displayName)).toEqual([
      "Finance",
      "Reception",
    ]);
    const holders = await client.call(MessageType.mailboxAccess, { mailbox: "Finance" });
    expect(holders.type === "mailbox-holders" && holders.data.holders.length).toBe(3);
    const grants = await client.call(MessageType.mailboxAccess, { user: "bob@contoso.com" });
    expect(grants.type).toBe("user-grants");
    if (grants.type !== "user-grants") return;
    expect(grants.data.grants.map((grant) => `${grant.right} ${grant.mailbox.displayName}`)).toEqual([
      "send-on-behalf Finance",
      "full-access Reception",
      "send-as Reception",
    ]);
    expect(grants.data.failed).toHaveLength(1); // the mock's partial answer
  });

  it("refuses a mailbox-access request without exactly one of the two, and an unknown mailbox", async () => {
    const client = new BridgeClient(new MockTransport());
    const usage = await client.call(MessageType.mailboxAccess, { mailbox: "Finance", user: "bob@contoso.com" });
    expect(usage.type === "error" && usage.data.code).toBe("usage");
    const missing = await client.call(MessageType.mailboxAccess, { mailbox: "nobody" });
    expect(missing.type === "error" && missing.data.code).toBe("not-found");
  });
});
