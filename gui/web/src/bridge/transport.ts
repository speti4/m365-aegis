import {
  type BridgeMessage,
  type GroupMembers,
  MessageType,
  PROTOCOL_VERSION,
  parseMessage,
  type SignInInfo,
  writeMessage,
} from "./protocol";

/**
 * How messages reach the host. In the window (WinForms with the WebView2 control, D-009) the page talks to
 * WebView2 directly (task 030): it posts with `chrome.webview.postMessage` and listens on `chrome.webview`'s
 * `message` event; the host injects nothing. In a plain browser (the Vite dev server without the host) the mock
 * answers itself, so the page renders without the exe.
 */
export interface Transport {
  send(message: BridgeMessage): void;
  /** Registers a handler for every message of the protocol; the return value unregisters it. */
  onMessage(handler: (message: BridgeMessage) => void): () => void;
}

type MessageListener = (event: { data: unknown }) => void;

/** The part of WebView2's `window.chrome.webview` the transport uses. */
export interface WebViewBridge {
  postMessage(message: unknown): void;
  addEventListener(type: "message", listener: MessageListener): void;
  removeEventListener(type: "message", listener: MessageListener): void;
}

/** WebView2's `chrome.webview` when the page runs in the window; null in a plain browser (Chrome has `chrome`, not `chrome.webview`). */
export function findWebView(scope: unknown = globalThis): WebViewBridge | null {
  const webview = (scope as { chrome?: { webview?: Partial<WebViewBridge> } } | undefined)?.chrome?.webview;
  if (
    webview &&
    typeof webview.postMessage === "function" &&
    typeof webview.addEventListener === "function" &&
    typeof webview.removeEventListener === "function"
  ) {
    return webview as WebViewBridge;
  }
  return null;
}

/** The window's transport: a message is posted as its JSON text, and the host's text replies are parsed back. */
export class WebViewTransport implements Transport {
  constructor(private readonly webview: WebViewBridge) {}

  send(message: BridgeMessage): void {
    this.webview.postMessage(writeMessage(message));
  }

  onMessage(handler: (message: BridgeMessage) => void): () => void {
    const listener: MessageListener = (event) => {
      // The host posts strings (PostWebMessageAsString); anything else is not of this protocol.
      const message = typeof event.data === "string" ? parseMessage(event.data) : null;
      if (message) {
        handler(message);
      }
    };
    this.webview.addEventListener("message", listener);
    return () => this.webview.removeEventListener("message", listener);
  }
}

/** The browser mock's signed-in account: made up, as the samples under tests/contracts/bridge. */
const mockSignIn: SignInInfo = {
  profile: "work",
  account: "alice@contoso.com",
  displayName: "Alice Example",
  tenantId: "00000000-0000-0000-0000-000000000002",
  objectId: "00000000-0000-0000-0000-000000000001",
  tokens: [
    {
      resource: "graph",
      expiresOn: "2026-10-29T15:00:00+01:00",
      scopes: ["User.Read", "Group.Read.All"],
      origin: "broker",
    },
    { resource: "exchange", error: "the consent for Exchange Online is missing (a mock)", scopes: [] },
  ],
};

/** The browser mock's one group. */
const mockFinance: GroupMembers = {
  group: {
    id: "00000000-0000-0000-0000-000000000031",
    displayName: "Finance",
    kind: "microsoft365",
    mail: "finance@contoso.com",
    synced: false,
  },
  members: [
    {
      type: "user",
      displayName: "Alice Example",
      userPrincipalName: "alice@contoso.com",
      mail: "alice@contoso.com",
      id: "00000000-0000-0000-0000-000000000001",
    },
    {
      type: "user",
      displayName: "Bob Example",
      userPrincipalName: "bob@contoso.com",
      mail: "bob@contoso.com",
      id: "00000000-0000-0000-0000-000000000003",
    },
    {
      type: "user",
      displayName: "Carol Example",
      userPrincipalName: "carol@contoso.com",
      id: "00000000-0000-0000-0000-000000000004",
    },
    {
      type: "group",
      displayName: "Finance EU",
      mail: "finance-eu@contoso.com",
      id: "00000000-0000-0000-0000-000000000032",
    },
  ],
};

/**
 * The browser mock: no host, so the requests are answered here with made-up data (a signed-in account, one group,
 * `Finance`, by name, address or id; any other group is not found), after `delayMs` so the page's waiting state
 * shows; an unknown type gets an error.
 */
export class MockTransport implements Transport {
  private handlers: Array<(message: BridgeMessage) => void> = [];

  constructor(private readonly delayMs = 0) {}

  send(message: BridgeMessage): void {
    if (message.id === undefined) {
      return;
    }
    const reply: BridgeMessage = { aegis: PROTOCOL_VERSION, id: message.id, ...MockTransport.answer(message) };
    setTimeout(() => {
      for (const handler of this.handlers) {
        handler(reply);
      }
    }, this.delayMs);
  }

  onMessage(handler: (message: BridgeMessage) => void): () => void {
    this.handlers.push(handler);
    return () => {
      this.handlers = this.handlers.filter((registered) => registered !== handler);
    };
  }

  private static answer(message: BridgeMessage): { type: string; data: unknown } {
    switch (message.type) {
      case MessageType.ping:
        return { type: MessageType.pong, data: { product: "m365-aegis", version: "dev (browser mock)" } };
      case MessageType.whoami:
        return { type: MessageType.signedIn, data: mockSignIn };
      case MessageType.groupMembers: {
        const group = (message.data as { group?: unknown } | undefined)?.group;
        const name = typeof group === "string" ? group.trim() : "";
        if (name === "") {
          return { type: MessageType.error, data: { code: "usage", message: "the group is missing" } };
        }
        const { id, displayName, mail } = mockFinance.group;
        return [id, displayName, mail].some((value) => value?.toLowerCase() === name.toLowerCase())
          ? { type: MessageType.members, data: mockFinance }
          : {
              type: MessageType.error,
              data: {
                code: "not-found",
                message: `no group named '${name}'`,
                hint: "give the exact display name, the address, the object id, or a word of the name",
              },
            };
      }
      default:
        return { type: MessageType.error, data: { code: "no-host", message: "no host in the browser" } };
    }
  }
}

/** The transport of this page: WebView2's when the page runs in the window, the mock otherwise. */
export function createTransport(scope: unknown = globalThis): { transport: Transport; hosted: boolean } {
  const webview = findWebView(scope);
  return webview
    ? { transport: new WebViewTransport(webview), hosted: true }
    : { transport: new MockTransport(300), hosted: false };
}
