// @vitest-environment happy-dom
import { afterEach, describe, expect, it, vi } from "vitest";
import type { DeviceCodePrompt, SignInInfo } from "../bridge/protocol";
import { button, click, hasButton, render } from "../test/dom";
import { SignInCard, type SignInState } from "./sign-in-card";

const alice: SignInInfo = {
  profile: "work",
  account: "alice@contoso.com",
  displayName: "Alice Example",
  tenantId: "00000000-0000-0000-0000-000000000002",
  tokens: [
    { resource: "graph", expiresOn: "2026-10-29T15:00:00+01:00", scopes: ["User.Read"] },
    { resource: "exchange", error: "the consent is missing", scopes: [] },
    { resource: "sharepoint", scopes: [] },
  ],
};
const prompt: DeviceCodePrompt = {
  verificationUri: "https://login.microsoftonline.com/common/oauth2/deviceauth",
  userCode: "ABCD1234",
  message: "To sign in, open the page and enter the code ABCD1234.",
  expiresOn: "2026-10-29T14:15:00+00:00",
};

function handlers() {
  return { onRefresh: vi.fn(), onSignIn: vi.fn(), onSignOut: vi.fn(), onOpenSignInPage: vi.fn() };
}

async function card(state: SignInState) {
  const on = handlers();
  const rendered = await render(<SignInCard state={state} {...on} />);
  return { ...rendered, on, text: () => rendered.container.textContent ?? "" };
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("the sign-in card", () => {
  it("shows the account, the tenant and a line per token, and signs out", async () => {
    const { container, on, text } = await card({ status: "signed-in", info: alice });

    expect(text()).toContain("signed in");
    expect(container.querySelector('[data-testid="account"]')?.textContent).toBe("Alice Example · alice@contoso.com");
    expect(text()).toContain("00000000-0000-0000-0000-000000000002");
    expect(text()).toContain("Microsoft Graph");
    expect(text()).toContain("token until ");
    expect(text()).toContain("no token: the consent is missing");
    expect(text()).toContain("sharepoint");
    expect(text()).toContain("token until ?");
    expect(hasButton(container, "Sign in")).toBe(false);

    await click(button(container, "Sign out"));
    await click(button(container, "Refresh"));
    expect(on.onSignOut).toHaveBeenCalledOnce();
    expect(on.onRefresh).toHaveBeenCalledOnce();
  });

  it("shows an account without a display name or a tenant while it signs out", async () => {
    const { container, text } = await card({
      status: "signing-out",
      info: { profile: "work", account: "alice@contoso.com", tokens: [] },
    });

    expect(text()).toContain("signing out…");
    expect(container.querySelector('[data-testid="account"]')?.textContent).toBe("alice@contoso.com");
    expect(text()).not.toContain("Tenant");
    expect(button(container, "Refresh").disabled).toBe(true);
    expect(hasButton(container, "Sign out")).toBe(false);
  });

  it("offers both sign-ins when the profile has no usable sign-in", async () => {
    const { container, on, text } = await card({
      status: "signed-out",
      info: { profile: "work", account: "alice@contoso.com", message: "no silent sign-in", hint: "sign in again" },
    });

    expect(text()).toContain("signed out");
    expect(text()).toContain("Profile work (alice@contoso.com) has no usable sign-in.");
    expect(container.querySelector('[data-testid="sign-in-hint"]')?.textContent).toBe("What to do: sign in again");
    await click(button(container, "Sign in"));
    await click(button(container, "Sign in with a code"));
    expect(on.onSignIn.mock.calls).toEqual([[false], [true]]);
  });

  it("names a profile without an account, and a missing profile without a sign-in button", async () => {
    const withoutAccount = await card({ status: "signed-out", info: { profile: "lab", message: "no account" } });
    expect(withoutAccount.text()).toContain("Profile lab has no usable sign-in.");
    expect(withoutAccount.container.querySelector('[data-testid="sign-in-hint"]')).toBeNull();

    const withoutProfile = await card({ status: "signed-out", info: { message: "no config.json" } });
    expect(withoutProfile.text()).toContain("There is no tenant profile on this computer yet");
    expect(hasButton(withoutProfile.container, "Sign in")).toBe(false);
  });

  it("waits for the account picker, then shows the device code with its page and a copy button", async () => {
    const waiting = await card({ status: "signing-in", before: null });
    expect(waiting.text()).toContain("signing in…");
    expect(waiting.container.querySelector('[data-testid="signing-in"]')).not.toBeNull();

    const writeText = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal("navigator", { clipboard: { writeText } });
    const { container, on, text } = await card({ status: "signing-in", before: null, deviceCode: prompt });
    expect(text()).toContain(prompt.verificationUri);
    expect(text()).toContain("ABCD1234");

    await click(button(container, "Open in browser"));
    expect(on.onOpenSignInPage).toHaveBeenCalledOnce();
    await click(button(container, "Copy the code"));
    expect(writeText).toHaveBeenCalledWith("ABCD1234");
    expect(hasButton(container, "Copied")).toBe(true);
  });

  it("keeps the copy button as it is when the clipboard refuses", async () => {
    vi.stubGlobal("navigator", { clipboard: { writeText: vi.fn().mockRejectedValue(new Error("denied")) } });
    const { container } = await card({ status: "signing-in", before: null, deviceCode: prompt });

    await click(button(container, "Copy the code"));

    expect(hasButton(container, "Copy the code")).toBe(true);
  });

  it("says when it checks, and when the state could not be read", async () => {
    const checking = await card({ status: "checking" });
    expect(checking.text()).toContain("Checking the sign-in…");
    expect(checking.container.querySelector('[data-slot="badge"]')).toBeNull();

    const unknown = await card({ status: "unknown" });
    expect(unknown.text()).toContain("The sign-in state could not be read.");
    expect(button(unknown.container, "Refresh").disabled).toBe(false);
  });
});
