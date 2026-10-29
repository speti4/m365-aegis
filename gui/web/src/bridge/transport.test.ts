import { readFileSync } from "node:fs";
import path from "node:path";
import { afterEach, describe, expect, it, vi } from "vitest";
import { BridgeClient } from "./client";
import { type BridgeMessage, MessageType, notification, PROTOCOL_VERSION, request } from "./protocol";
import { createTransport, findWebView, MockTransport, type WebViewBridge, WebViewTransport } from "./transport";

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
