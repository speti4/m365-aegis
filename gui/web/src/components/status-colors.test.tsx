// @vitest-environment happy-dom
import { describe, expect, it } from "vitest";
import { BridgeClient } from "../bridge/client";
import { mockSnapshotDiff } from "../bridge/mock-snapshot-diff";
import { MockTransport } from "../bridge/mock-transport";
import type { Plan, PlanAction, PlanApplied } from "../bridge/protocol";
import { button, click, render } from "../test/dom";
import { MailboxAccessCard } from "./mailbox-access-card";
import { MailboxAccessWriteCard } from "./mailbox-write-cards";
import { PlanView, ResultView } from "./plan-view";
import { SignInCard } from "./sign-in-card";
import { SnapshotDiffView } from "./snapshot-diff-card";
import { Badge } from "./ui/badge";

/**
 * The status colors and the selected state (task 123): each status reads from its own token (`success`, `warning`,
 * `danger`, soft backgrounds behind the badges), never a fixed Tailwind hue, and a selected choice carries a check
 * besides its fill, so it stays apart under forced colors.
 */

const noop = () => undefined;

const plan = (actions: PlanAction[]): Plan => ({
  operation: "mailbox-access-grant",
  title: "Grant on shared@contoso.com",
  changes: actions.map((action) => ({
    action,
    target: `${action}@contoso.com`,
    detail: "FullAccess",
    current: "none",
    desired: "FullAccess",
  })),
  unchanged: [],
  warnings: [],
  mode: "dry-run",
});

const badgeOf = (container: HTMLElement, text: string) =>
  [...container.querySelectorAll('[data-slot="badge"]')].find((badge) => badge.textContent === text) as HTMLElement;

describe("the status badges", () => {
  it("have a variant per status on the tokens", async () => {
    const { container } = await render(
      <>
        <Badge variant="success">ok</Badge>
        <Badge variant="warning">careful</Badge>
        <Badge variant="danger">gone</Badge>
      </>,
    );

    expect(badgeOf(container, "ok").className).toContain("bg-success-soft text-success");
    expect(badgeOf(container, "careful").className).toContain("bg-warning-soft text-warning");
    expect(badgeOf(container, "gone").className).toContain("bg-danger-soft text-danger");
  });

  it("color a plan's change by what it does", async () => {
    const { container } = await render(<PlanView plan={plan(["add", "remove", "clear", "set", "send"])} />);

    expect(badgeOf(container, "add").className).toContain("text-success");
    expect(badgeOf(container, "remove").className).toContain("text-danger");
    expect(badgeOf(container, "clear").className).toContain("text-danger");
    expect(badgeOf(container, "set").className).toContain("text-warning");
    expect(badgeOf(container, "send").className).toContain("text-warning");
  });

  it("tell an added row of the diff from a removed one", async () => {
    const { container } = await render(<SnapshotDiffView report={mockSnapshotDiff} />);

    expect(badgeOf(container, "added").className).toContain("text-success");
    expect(badgeOf(container, "removed").className).toContain("text-danger");
  });

  it("show the sign-in state in green or amber", async () => {
    const signedIn = await render(
      <SignInCard
        state={{
          status: "signed-in",
          info: { profile: "work", account: "alice@contoso.com", tenantId: "contoso.com", tokens: [] },
        }}
        onRefresh={noop}
        onSignIn={noop}
        onSignOut={noop}
        onOpenSignInPage={noop}
        onCancelSignIn={noop}
      />,
    );
    const signedOut = await render(
      <SignInCard
        state={{ status: "signed-out", info: { message: "No sign-in yet." } }}
        onRefresh={noop}
        onSignIn={noop}
        onSignOut={noop}
        onOpenSignInPage={noop}
        onCancelSignIn={noop}
      />,
    );

    const noExchange = await render(
      <SignInCard
        state={{
          status: "signed-in",
          info: {
            profile: "work",
            account: "alice@contoso.com",
            tenantId: "contoso.com",
            tokens: [{ resource: "exchange", error: "the consent is missing", scopes: [] }],
          },
        }}
        onRefresh={noop}
        onSignIn={noop}
        onSignOut={noop}
        onOpenSignInPage={noop}
        onCancelSignIn={noop}
      />,
    );
    const unknown = await render(
      <SignInCard
        state={{ status: "unknown" }}
        onRefresh={noop}
        onSignIn={noop}
        onSignOut={noop}
        onOpenSignInPage={noop}
        onCancelSignIn={noop}
      />,
    );

    expect(badgeOf(signedIn.container, "signed in").className).toContain("text-success");
    expect(badgeOf(signedOut.container, "signed out").className).toContain("text-warning");
    expect(badgeOf(noExchange.container, "signed in · Exchange unavailable").className).toContain("text-warning");
    expect(badgeOf(unknown.container, "sign-in unknown").className).toContain("text-danger");
  });
});

describe("the results of an applied plan", () => {
  it("mark an applied change green and a failed one red", async () => {
    const applied: PlanApplied = {
      plan: {
        ...plan(["add", "add"]),
        mode: "applied",
        changes: plan(["add", "remove"]).changes.map((change, index) => ({
          ...change,
          result: index === 0 ? { status: "applied" } : { status: "failed" },
        })),
      },
    };
    const { container } = await render(<ResultView applied={applied} />);

    expect(container.querySelector('[aria-label="applied"]')?.getAttribute("class")).toContain("text-success");
    expect(container.querySelector('[aria-label="failed"]')?.getAttribute("class")).toContain("text-danger");
    expect(container.querySelector('[role="status"]')?.className).toContain("border-warning/50");
  });
});

describe("a selected choice", () => {
  const checks = (element: Element) => element.querySelectorAll("svg.lucide-check").length;
  /** An option of a choice, a radio of base-ui's radio group (task 121), by its text. */
  const radio = (container: HTMLElement, name: string) =>
    [...container.querySelectorAll('[role="radio"]')].find((item) => item.textContent === name) as HTMLElement;

  it("carries a check on the write card's choices and rights", async () => {
    const { container } = await render(
      <MailboxAccessWriteCard client={new BridgeClient(new MockTransport())} signedIn />,
    );

    expect(checks(radio(container, "Grant"))).toBe(1);
    expect(checks(radio(container, "Revoke"))).toBe(0);
    expect(checks(button(container, "FullAccess"))).toBe(1);
    expect(checks(button(container, "Send As"))).toBe(0);

    await click(radio(container, "Revoke"));
    await click(button(container, "Send As"));

    expect(checks(radio(container, "Grant"))).toBe(0);
    expect(checks(radio(container, "Revoke"))).toBe(1);
    expect(checks(button(container, "Send As"))).toBe(1);
    expect(radio(container, "Revoke").className).toContain("forced-colors:aria-checked:bg-[color:Highlight]");
  });

  it("carries a check on the lookup's mode", async () => {
    const { container } = await render(<MailboxAccessCard client={new BridgeClient(new MockTransport())} />);

    expect(checks(radio(container, "A mailbox"))).toBe(1);
    expect(checks(radio(container, "A user"))).toBe(0);

    await click(radio(container, "A user"));

    expect(checks(radio(container, "A mailbox"))).toBe(0);
    expect(checks(radio(container, "A user"))).toBe(1);
  });
});
