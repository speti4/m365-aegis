// @vitest-environment happy-dom

import { act } from "react";
import { toast } from "sonner";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { App } from "./App";
import { BridgeClient } from "./bridge/client";
import {
  type BridgeMessage,
  type HostNotification,
  MessageType,
  notification,
  PROTOCOL_VERSION,
  parseMessage,
} from "./bridge/protocol";
import type { WebViewBridge } from "./bridge/transport";
import { button, click, hasButton, render, settle, submit, type } from "./test/dom";

/** The mock's answer time (createTransport); its device-code sign-in takes ten times as long. */
const MOCK_MS = 300;

type Answer = (message: BridgeMessage) => { type: string; data?: unknown } | null;

/** WebView2's chrome.webview as the window has it, answering the page's requests with `answer`. */
function hostWith(answer: Answer) {
  const listeners: Array<(event: { data: unknown }) => void> = [];
  const posted: BridgeMessage[] = [];
  const post = (message: BridgeMessage) => {
    for (const listener of listeners) {
      listener({ data: JSON.stringify(message) });
    }
  };
  const webview: WebViewBridge = {
    postMessage: (text) => {
      const message = parseMessage(text as string) as BridgeMessage;
      posted.push(message);
      const reply = message.id === undefined ? null : answer(message);
      if (reply) {
        setTimeout(() => post({ aegis: PROTOCOL_VERSION, id: message.id, ...reply }), 0);
      }
    },
    addEventListener: (_type, listener) => {
      listeners.push(listener);
    },
    removeEventListener: () => {},
  };
  vi.stubGlobal("chrome", { webview });
  return { posted, post };
}

const failure = (message: string) => ({ type: MessageType.error, data: { code: "auth", message, hint: "try again" } });

const signedOut = { type: MessageType.signedOut, data: { profile: "work", message: "no silent sign-in" } };

/** The toasts the page raised: the message and the hint of each `error` reply. */
let toasts: ReturnType<typeof vi.spyOn>;

beforeEach(() => {
  vi.useFakeTimers();
  toasts = vi.spyOn(toast, "error");
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  document.body.innerHTML = "";
});

describe("the window's page", () => {
  it("in a plain browser: the mock's version and account, a sign-out and both sign-ins", async () => {
    const { container } = await render(<App />);
    expect(container.textContent).toContain("browser mock: made-up data, no host");
    expect(container.textContent).toContain("Checking the sign-in…");

    await settle(MOCK_MS);
    expect(container.querySelector('[data-testid="version"]')?.textContent).toBe("dev (browser mock)");
    expect(container.textContent).toContain("Alice Example · alice@contoso.com");

    // Signed out, the page reads the state again.
    await click(button(container, "Sign out"));
    await settle(MOCK_MS);
    await settle(MOCK_MS);
    expect(container.textContent).toContain("Profile work (alice@contoso.com) has no usable sign-in.");

    await click(button(container, "Sign in"));
    expect(container.querySelector('[data-testid="signing-in"]')).not.toBeNull();
    await settle(MOCK_MS);
    expect(container.textContent).toContain("Alice Example · alice@contoso.com");

    await click(button(container, "Sign out"));
    await settle(MOCK_MS);
    await settle(MOCK_MS);
    await click(button(container, "Sign in with a code"));
    await settle(MOCK_MS);
    expect(container.textContent).toContain("MOCK1234");
    await click(button(container, "Open in browser"));
    await settle(MOCK_MS);
    await settle(MOCK_MS * 10);
    expect(container.textContent).toContain("Alice Example · alice@contoso.com");
    expect(hasButton(container, "Sign in")).toBe(false);
    expect(toasts).not.toHaveBeenCalled();
  });

  it("in the window: no mock badge, and a failed request is a toast", async () => {
    const { posted, post } = hostWith((message) => {
      switch (message.type) {
        case MessageType.ping:
          return failure("the host is busy");
        case MessageType.whoami:
          return failure("the cache could not be read");
        default:
          return null;
      }
    });
    const { container } = await render(<App />);
    await settle();

    expect(container.textContent).not.toContain("browser mock");
    expect(container.querySelector('[data-testid="version"]')?.textContent).toBe("…");
    expect(container.textContent).toContain("The sign-in state could not be read.");
    expect(toasts).toHaveBeenCalledWith("the cache could not be read", { description: "try again" });
    expect(posted.map((message) => message.type).slice(0, 3)).toEqual([
      MessageType.ready,
      MessageType.ping,
      MessageType.whoami,
    ]);
    // A device code the page cannot read changes nothing.
    post(notification(MessageType.deviceCode, { userCode: 1 }));
    await settle();
    expect(container.textContent).toContain("The sign-in state could not be read.");
  });

  it("in the window: a failed sign-in and sign-out are toasts and the state goes back", async () => {
    let signedIn = false;
    hostWith((message) => {
      switch (message.type) {
        case MessageType.ping:
          return { type: MessageType.pong, data: { product: "m365-aegis", version: "0.1.0" } };
        case MessageType.whoami:
          return signedIn
            ? { type: MessageType.signedIn, data: { profile: "work", account: "alice@contoso.com", tokens: [] } }
            : signedOut;
        case MessageType.login:
          signedIn = true;
          return failure("the account picker was closed");
        case MessageType.logout:
          return failure("the cache is locked");
        default:
          return null;
      }
    });
    const { container } = await render(<App />);
    await settle();
    expect(container.querySelector('[data-testid="version"]')?.textContent).toBe("0.1.0");

    await click(button(container, "Sign in with a code"));
    await settle();
    expect(toasts).toHaveBeenCalledWith("the account picker was closed", { description: "try again" });
    expect(container.textContent).toContain("Profile work has no usable sign-in.");

    await click(button(container, "Refresh"));
    await settle();
    await click(button(container, "Sign out"));
    await settle();
    expect(toasts).toHaveBeenCalledWith("the cache is locked", { description: "try again" });
    expect(container.querySelector('[data-testid="account"]')?.textContent).toBe("alice@contoso.com");
  });

  it("in the window: the device code of the host, and a refused page open as a toast", async () => {
    const { post } = hostWith((message) => {
      switch (message.type) {
        case MessageType.whoami:
          return signedOut;
        case MessageType.openSignInPage:
          return failure("no sign-in waits");
        default:
          return null;
      }
    });
    const { container } = await render(<App />);
    await settle();
    await click(button(container, "Sign in"));
    post(
      notification(MessageType.deviceCode, {
        verificationUri: "https://login.microsoftonline.com/common/oauth2/deviceauth",
        userCode: "ABCD1234",
        message: "To sign in, enter ABCD1234.",
        expiresOn: "2026-10-29T14:15:00+00:00",
      }),
    );
    await settle();
    expect(container.textContent).toContain("ABCD1234");

    await click(button(container, "Open in browser"));
    await settle();
    expect(toasts).toHaveBeenCalledWith("no sign-in waits", { description: "try again" });
  });

  it("in a plain browser: a grant on the rights card is read again by the access card that shows the mailbox", async () => {
    const { container } = await render(<App />);
    await settle(MOCK_MS);
    const [, , access, rights] = [...container.querySelectorAll('[data-slot="card"]')] as HTMLElement[];
    const field = (card: HTMLElement, label: string) =>
      card.querySelector(`input[aria-label="${label}"]`) as HTMLInputElement;

    await type(field(access, "Mailbox"), "finance");
    await submit(access.querySelector("form") as HTMLFormElement);
    await settle(MOCK_MS);
    expect(access.textContent).toContain("· 3 grant(s)");

    await type(field(rights, "Mailbox"), "finance");
    await type(field(rights, "Users"), "bob@contoso.com");
    await click(button(rights, "See the plan"));
    await settle(MOCK_MS);
    await click(button(rights, "Continue to confirm"));
    await click(rights.querySelector('input[type="checkbox"]') as HTMLInputElement);
    await click(button(rights, "Confirm and apply"));
    await settle(MOCK_MS);
    expect(access.textContent).toContain("A change was applied to this mailbox: reading its access again…");
    await settle(MOCK_MS);

    expect(access.textContent).toContain("· 4 grant(s)");
    expect(access.textContent).toContain("Read again after a change on the Mailbox rights card.");
  });

  it("leaves the sign-in as it is on a host notification of another kind", async () => {
    const register = vi.spyOn(BridgeClient.prototype, "onNotification");
    hostWith((message) => (message.type === MessageType.whoami ? signedOut : null));
    const { container } = await render(<App />);
    await settle();
    const handler = register.mock.calls[0][0];

    await act(async () => handler({ type: "progress", data: {} } as unknown as HostNotification));

    expect(container.textContent).toContain("Profile work has no usable sign-in.");
  });
});
