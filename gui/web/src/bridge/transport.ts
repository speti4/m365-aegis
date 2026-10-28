import { type BridgeMessage, parseMessage, writeMessage } from "./protocol";

/**
 * How messages reach the host. In the window, Photino injects `window.external.sendMessage` and
 * `window.external.receiveMessage`; in a plain browser (the Vite dev server without the host, the tests) the mock
 * answers `ping` itself, so the page renders without the exe.
 */
export interface Transport {
  send(message: BridgeMessage): void;
  onMessage(handler: (message: BridgeMessage) => void): void;
}

interface PhotinoExternal {
  sendMessage(message: string): void;
  receiveMessage(handler: (message: string) => void): void;
}

function photinoExternal(): PhotinoExternal | null {
  const external = (window as unknown as { external?: Partial<PhotinoExternal> }).external;
  if (external && typeof external.sendMessage === "function" && typeof external.receiveMessage === "function") {
    return external as PhotinoExternal;
  }
  return null;
}

class PhotinoTransport implements Transport {
  constructor(private readonly external: PhotinoExternal) {}

  send(message: BridgeMessage): void {
    this.external.sendMessage(writeMessage(message));
  }

  onMessage(handler: (message: BridgeMessage) => void): void {
    this.external.receiveMessage((text) => {
      const message = parseMessage(text);
      if (message) {
        handler(message);
      }
    });
  }
}

/** The browser mock: no host, so `ping` is answered here and everything else gets an error. */
export class MockTransport implements Transport {
  private handlers: Array<(message: BridgeMessage) => void> = [];

  send(message: BridgeMessage): void {
    const reply: BridgeMessage =
      message.type === "ping"
        ? { aegis: 1, id: message.id, type: "pong", data: { product: "m365-aegis", version: "dev (browser mock)" } }
        : { aegis: 1, id: message.id, type: "error", data: { code: "no-host", message: "no host in the browser" } };
    if (message.id !== undefined) {
      queueMicrotask(() => {
        for (const handler of this.handlers) {
          handler(reply);
        }
      });
    }
  }

  onMessage(handler: (message: BridgeMessage) => void): void {
    this.handlers.push(handler);
  }
}

/** The transport of this page: Photino when the host injected its bridge, the mock otherwise. */
export function createTransport(): { transport: Transport; hosted: boolean } {
  const external = photinoExternal();
  return external
    ? { transport: new PhotinoTransport(external), hosted: true }
    : { transport: new MockTransport(), hosted: false };
}
