import { describe, expect, it } from "vitest";
import { cards, isRefusal, outcomeFor, planFor } from "./sketch-data";
import {
  canConfirm,
  currentStepIndex,
  initialSketchState,
  type SketchEvent,
  type SketchPlan,
  type SketchState,
  sketchReducer,
} from "./task-card-flow";

const run = (state: SketchState, ...events: SketchEvent[]) => events.reduce(sketchReducer, state);

const grantPlan: SketchPlan = {
  title: "Grant FullAccess on Finance",
  changes: [{ action: "add", target: "Finance", detail: "FullAccess for Bob Example" }],
  unchanged: [],
  warnings: [],
  readOnly: false,
};

/** The grant card with a mailbox and a colleague picked. */
const picked = run(
  initialSketchState,
  { type: "open", card: "grant-full-access" },
  { type: "pick", field: "mailbox", values: ["finance@contoso.com"] },
  { type: "pick", field: "users", values: ["bob@contoso.com"] },
);

describe("a task card's flow (the design sketch)", () => {
  it("runs pick, plan, confirm and result, the steps numbered as the card shows them", () => {
    expect(picked.step).toBe("pick");
    expect(currentStepIndex(picked)).toBe(0);

    const planning = run(picked, { type: "request-plan" });
    expect(planning.step).toBe("planning");
    expect(currentStepIndex(planning)).toBe(1);

    const plan = run(planning, { type: "plan-ready", plan: grantPlan });
    expect(plan.step).toBe("plan");

    const confirming = run(plan, { type: "proceed" });
    expect(confirming.step).toBe("confirming");
    expect(currentStepIndex(confirming)).toBe(2);

    const applying = run(confirming, { type: "confirm" });
    expect(applying.step).toBe("applying");
    expect(currentStepIndex(applying)).toBe(3);

    const result = run(applying, { type: "applied", outcome: outcomeFor(grantPlan, "normal") });
    expect(result).toMatchObject({ step: "result", outcome: { status: "done" } });

    expect(run(result, { type: "again" })).toMatchObject({ step: "pick", card: "grant-full-access", picks: {} });
    expect(run(result, { type: "close" })).toEqual({ signedIn: true, step: "list" });
  });

  it("asks for the plan only when every field has a value", () => {
    const half = run(
      initialSketchState,
      { type: "open", card: "grant-full-access" },
      { type: "pick", field: "mailbox", values: ["finance@contoso.com"] },
    );
    expect(run(half, { type: "request-plan" })).toBe(half);
    const emptied = run(picked, { type: "pick", field: "users", values: [] });
    expect(run(emptied, { type: "request-plan" })).toBe(emptied);
  });

  it("shows the core's refusal and goes back to the picks, which stay", () => {
    const refused = run(
      picked,
      { type: "request-plan" },
      { type: "plan-refused", refusal: { code: "forbidden", message: "the service refused" } },
    );
    expect(refused.step).toBe("refused");
    expect(currentStepIndex(refused)).toBe(1);
    expect(run(refused, { type: "confirm" })).toBe(refused);
    expect(run(refused, { type: "back" })).toMatchObject({ step: "pick", picks: { users: ["bob@contoso.com"] } });
  });

  it("goes back from the confirmation to the plan without a change", () => {
    const confirming = run(
      picked,
      { type: "request-plan" },
      { type: "plan-ready", plan: grantPlan },
      { type: "proceed" },
    );
    expect(run(confirming, { type: "back" })).toMatchObject({ step: "plan", plan: grantPlan });
  });

  it("never confirms a plan with nothing to do, nor a read-only answer", () => {
    const nothing = { ...grantPlan, changes: [] };
    const answer = { ...grantPlan, changes: [], readOnly: true, answer: ["FullAccess on Finance"] };
    expect(canConfirm(nothing)).toBe(false);
    expect(canConfirm(answer)).toBe(false);
    const plan = run(picked, { type: "request-plan" }, { type: "plan-ready", plan: nothing });
    expect(run(plan, { type: "proceed" })).toBe(plan);
  });

  it("opens no card signed out, and a sign-out closes an open one", () => {
    const signedOut = run(initialSketchState, { type: "sign-out" });
    expect(run(signedOut, { type: "open", card: "check-user-access" })).toBe(signedOut);
    expect(run(picked, { type: "sign-out" })).toEqual({ signedIn: false, step: "list" });
    expect(run(signedOut, { type: "sign-in" })).toEqual({ signedIn: true, step: "list" });
  });

  it("ignores an answer that arrives after the card moved on", () => {
    expect(run(picked, { type: "plan-ready", plan: grantPlan })).toBe(picked);
    expect(run(picked, { type: "applied", outcome: outcomeFor(grantPlan, "normal") })).toBe(picked);
  });
});

describe("the sketch's made-up core", () => {
  it("has the three cards, two of them writes", () => {
    expect(cards.map((card) => [card.id, card.writes])).toEqual([
      ["grant-full-access", true],
      ["add-group-owner", true],
      ["check-user-access", false],
    ]);
  });

  it("plans only what is not already so", () => {
    const plan = planFor(
      "grant-full-access",
      { mailbox: ["finance@contoso.com"], users: ["alice@contoso.com", "bob@contoso.com"] },
      "normal",
    );
    expect(isRefusal(plan)).toBe(false);
    if (isRefusal(plan)) return;
    expect(plan.changes.map((change) => change.detail)).toEqual([
      "FullAccess for Bob Example, shown in their Outlook (automapping)",
    ]);
    expect(plan.unchanged).toEqual(["Alice Example already holds FullAccess on Finance"]);
  });

  it("answers a check without a change, and refuses in the refusal scenario", () => {
    const answer = planFor("check-user-access", { user: ["bob@contoso.com"] }, "normal");
    expect(!isRefusal(answer) && answer.readOnly && answer.answer).toEqual(["FullAccess on Reception"]);
    const refused = planFor("add-group-owner", { group: ["finance"], owners: ["bob@contoso.com"] }, "refused");
    expect(isRefusal(refused) && refused.code).toBe("forbidden");
  });

  it("fails the last change in the partial scenario", () => {
    const plan = planFor(
      "add-group-owner",
      { group: ["finance-eu"], owners: ["bob@contoso.com", "carol@contoso.com"] },
      "partial",
    );
    if (isRefusal(plan)) throw new Error("no plan");
    const outcome = outcomeFor(plan, "partial");
    expect(outcome.status).toBe("partial");
    expect(outcome.items.map((item) => item.ok)).toEqual([true, false]);
    expect(outcomeFor(plan, "normal").status).toBe("done");
  });
});
