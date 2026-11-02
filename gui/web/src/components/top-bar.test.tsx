// @vitest-environment happy-dom
import { act } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { SignInInfo } from "../bridge/protocol";
import { reveal } from "../lib/scroll";
import { button, click, hasButton, render } from "../test/dom";
import type { SignInState } from "./sign-in-state";
import { pageGroups, shortTenant, TopBar } from "./top-bar";

const alice: SignInInfo = {
  profile: "work",
  account: "alice@contoso.com",
  displayName: "Alice Example",
  tenantId: "00000000-0000-0000-0000-000000000002",
  tokens: [
    { resource: "graph", expiresOn: "2026-10-29T15:00:00+01:00", scopes: ["User.Read"] },
    { resource: "exchange", expiresOn: "2026-10-29T15:00:00+01:00", scopes: ["Exchange.Manage"] },
  ],
};
const withoutExchange: SignInInfo = {
  ...alice,
  tokens: [alice.tokens[0], { resource: "exchange", error: "the consent is missing", scopes: [] }],
};

async function bar(signIn: SignInState, hosted = true) {
  const on = { onSignIn: vi.fn(), onSignOut: vi.fn() };
  const rendered = await render(<TopBar version="0.2.0" hosted={hosted} signIn={signIn} {...on} />);
  const { container } = rendered;
  const testId = (id: string) => container.querySelector(`[data-testid="${id}"]`);
  return {
    ...rendered,
    on,
    status: () => testId("status")?.textContent,
    dot: () => testId("status-dot")?.className ?? "",
    account: () => testId("bar-account"),
    tenant: () => testId("bar-tenant"),
    session: () => testId("session")?.textContent ?? "",
  };
}

afterEach(() => {
  vi.restoreAllMocks();
  document.body.innerHTML = "";
});

describe("the top bar (task 124)", () => {
  it("shows the account, the tenant shortened and the profile signed in, and signs out", async () => {
    const view = await bar({ status: "signed-in", info: alice });

    expect(view.container.querySelector("header")?.className).toContain("sticky");
    expect(view.container.querySelector('[data-testid="version"]')?.textContent).toBe("0.2.0");
    expect(view.status()).toBe("signed in");
    expect(view.dot()).toContain("bg-success");
    expect(view.account()?.textContent).toBe("alice@contoso.com");
    expect(view.account()?.getAttribute("title")).toBe("Alice Example");
    expect(view.tenant()?.textContent).toBe("tenant 00000000…");
    expect(view.tenant()?.getAttribute("title")).toBe("00000000-0000-0000-0000-000000000002");
    expect(view.session()).toContain("profile work");
    expect(view.container.textContent).not.toContain("mock");
    expect(hasButton(view.container, "Sign in")).toBe(false);

    await click(button(view.container, "Sign out"));
    expect(view.on.onSignOut).toHaveBeenCalledOnce();
  });

  it("says Exchange unavailable when the Exchange Online row has no token", async () => {
    const view = await bar({ status: "signed-in", info: withoutExchange });

    expect(view.status()).toBe("signed in · Exchange unavailable");
    expect(view.dot()).toContain("bg-warning");
    expect(hasButton(view.container, "Sign out")).toBe(true);
  });

  it("shows the profile's account signed out, plainly, with Sign in; and the mock's badge in a browser", async () => {
    const view = await bar(
      { status: "signed-out", info: { profile: "work", account: "alice@contoso.com", message: "no silent sign-in" } },
      false,
    );

    expect(view.status()).toBe("signed out");
    expect(view.dot()).toContain("bg-warning");
    expect(view.account()?.className).toContain("text-muted-foreground");
    expect(view.tenant()).toBeNull();
    expect(view.container.querySelector('[title="The browser mock: made-up data, no host"]')?.textContent).toBe("mock");
    expect(hasButton(view.container, "Sign out")).toBe(false);

    await click(button(view.container, "Sign in"));
    expect(view.on.onSignIn).toHaveBeenCalledOnce();
  });

  it("offers no sign-in without a profile, and shows the states in between", async () => {
    const noProfile = await bar({ status: "signed-out", info: { message: "no profile" } });
    expect(noProfile.account()).toBeNull();
    expect(noProfile.session()).not.toContain("profile");
    expect(hasButton(noProfile.container, "Sign in")).toBe(false);
    await noProfile.unmount();

    const checking = await bar({ status: "checking" });
    expect(checking.status()).toBe("checking the sign-in…");
    expect(checking.dot()).toContain("bg-muted-foreground");
    await checking.unmount();

    const unknown = await bar({ status: "unknown" });
    expect(unknown.status()).toBe("sign-in unknown");
    expect(unknown.dot()).toContain("bg-danger");
    await unknown.unmount();

    const signingOut = await bar({ status: "signing-out", info: { ...alice, tenantId: undefined } });
    expect(signingOut.status()).toBe("signing out…");
    expect(signingOut.account()?.textContent).toBe("alice@contoso.com");
    expect(signingOut.tenant()).toBeNull();
    expect(hasButton(signingOut.container, "Sign out")).toBe(false);
    await signingOut.unmount();

    const signingIn = await bar({ status: "signing-in", before: null });
    expect(signingIn.status()).toBe("signing in…");
    expect(signingIn.account()).toBeNull();
  });

  it("scrolls to a group and to the sign-in card instead of changing the address", async () => {
    const scrolled = vi.spyOn(HTMLElement.prototype, "scrollIntoView").mockImplementation(() => {});
    const view = await bar({ status: "signed-in", info: alice });
    const targets = ["sign-in", ...pageGroups.map((group) => group.id)].map((id) => {
      const section = document.createElement("section");
      section.id = id;
      section.tabIndex = -1;
      document.body.append(section);
      return section;
    });
    const links = [...view.container.querySelectorAll("nav a")] as HTMLAnchorElement[];
    expect(links.map((link) => [link.textContent, link.getAttribute("href")])).toEqual([
      ["Look up", "#look-up"],
      ["Snapshots", "#snapshots"],
      ["Change", "#change"],
    ]);

    const follow = async (link: HTMLAnchorElement) => {
      const event = new MouseEvent("click", { bubbles: true, cancelable: true });
      await act(async () => link.dispatchEvent(event));
      return event.defaultPrevented;
    };

    const prevented = [];
    for (const link of [...links, view.container.querySelector('a[href="#sign-in"]') as HTMLAnchorElement]) {
      prevented.push(await follow(link));
    }

    expect(prevented).toEqual([true, true, true, true]);
    expect(scrolled.mock.contexts).toEqual([targets[1], targets[2], targets[3], targets[0]]);
    expect(document.activeElement).toBe(targets[0]);
  });
});

describe("the bar's helpers", () => {
  it("shortens a tenant id to its first eight characters, and leaves a short one as it is", () => {
    expect(shortTenant("00000000-0000-0000-0000-000000000002")).toBe("00000000…");
    expect(shortTenant("contoso")).toBe("contoso");
  });

  it("scrolls without motion when the user asked for less, and does nothing without an element", () => {
    const element = document.createElement("div");
    const scrolled = vi.spyOn(element, "scrollIntoView").mockImplementation(() => {});
    const still = (matches: boolean) => ({ matchMedia: () => ({ matches }) as MediaQueryList });

    reveal(element, still(true));
    reveal(element, still(false));
    reveal(element, {});
    reveal(null);

    expect(scrolled.mock.calls).toEqual([
      [{ behavior: "auto", block: "start" }],
      [{ behavior: "smooth", block: "start" }],
      [{ behavior: "smooth", block: "start" }],
    ]);
  });
});
