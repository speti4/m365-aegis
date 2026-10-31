import { MockTransport } from "./mock-transport";
import { type BridgeMessage, parseMessage, writeMessage } from "./protocol";

/**
 * How messages reach the host. In the window (WinForms with the WebView2 control, D-009) the page talks to
 * WebView2 directly (task 030): it posts with `chrome.webview.postMessage` and listens on `chrome.webview`'s
 * `message` event; the host injects nothing. In a plain browser (the Vite dev server without the host) the mock
 * answers itself (`mock-transport.ts`), so the page renders without the exe.
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

/**
 * The transport of this page: WebView2's when the page runs in the window, the mock otherwise. In the browser,
 * `?mock=signed-out` in the address starts the mock signed out, so the sign-in card's flow can be tried, and
 * `?mock=no-snapshot` starts it without a snapshot, so the access card's plain state shows (task 102).
 */
export function createTransport(scope: unknown = globalThis): { transport: Transport; hosted: boolean } {
  const webview = findWebView(scope);
  if (webview) {
    return { transport: new WebViewTransport(webview), hosted: true };
  }
  const search = (scope as { location?: { search?: unknown } } | undefined)?.location?.search;
  const mock = typeof search === "string" ? new URLSearchParams(search).get("mock") : null;
  return {
    transport: new MockTransport({ delayMs: 300, signedIn: mock !== "signed-out", snapshot: mock !== "no-snapshot" }),
    hosted: false,
  };
}
