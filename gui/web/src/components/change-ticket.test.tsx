// @vitest-environment happy-dom
import { describe, expect, it } from "vitest";
import type { Plan } from "../bridge/protocol";
import { render } from "../test/dom";
import { ConfirmView, PlanView } from "./plan-view";

/**
 * The change ticket of D-015 (task 127): the plan and the confirmation sit on their own surface with an amber rule,
 * the after state strong; a plan with nothing to change is no ticket.
 */

const noop = () => undefined;

const plan = (changes: number): Plan => ({
  operation: "mailbox-access-grant",
  title: "Grant mailbox permissions on Reception (reception@contoso.com)",
  changes: Array.from({ length: changes }, (_, index) => ({
    action: "add" as const,
    target: "Reception (reception@contoso.com)",
    detail: `Send As for user${index}@contoso.com`,
    current: "no Send As",
    desired: "has Send As",
  })),
  unchanged: [],
  warnings: [],
  mode: "dry-run",
});

const ticket = (container: HTMLElement) => container.querySelector('[data-testid="change-ticket"]');

describe("the change ticket", () => {
  it("frames a plan with changes, the after state strong", async () => {
    const { container } = await render(<PlanView plan={plan(2)} />);

    expect(ticket(container)?.className).toContain("border-l-warning");
    expect(ticket(container)?.className).toContain("bg-ticket");
    expect(ticket(container)?.textContent).toContain(plan(2).title);
    expect([...container.querySelectorAll("span.font-semibold")].map((span) => span.textContent)).toEqual([
      "Has Send As",
      "Has Send As",
    ]);
  });

  it("is not there when there is nothing to change", async () => {
    const { container } = await render(<PlanView plan={plan(0)} />);

    expect(ticket(container)).toBeNull();
    expect(container.textContent).toContain("Nothing to do");
  });

  it("frames the confirmation's changes, with the checkbox and the buttons below it", async () => {
    const { container } = await render(<ConfirmView plan={plan(1)} onConfirm={noop} onBack={noop} onCancel={noop} />);

    const framed = ticket(container) as HTMLElement;
    expect(framed.querySelector('[data-testid="confirm-lead"]')).not.toBeNull();
    expect(framed.querySelector('input[type="checkbox"]')).toBeNull();
    expect(framed.querySelector("button")).toBeNull();
    expect(container.querySelector('input[type="checkbox"]')).not.toBeNull();
  });
});
