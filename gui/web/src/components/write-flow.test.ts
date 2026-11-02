import { describe, expect, it } from "vitest";
import { MessageType, type Plan, type PlanApplied, type PlanReady, type Reply } from "../bridge/protocol";
import {
  canConfirm,
  initialWriteState,
  outcome,
  type WriteEvent,
  type WriteState,
  writeReducer,
  writeStatus,
  writeStepIndex,
} from "./write-flow";

const plan: Plan = {
  operation: "mailbox-access-grant",
  title: "Grant mailbox permissions on Finance (finance@contoso.com)",
  changes: [
    {
      action: "add",
      target: "Finance (finance@contoso.com)",
      detail: "Full Access for Bob Example (bob@contoso.com)",
      current: "no Full Access",
      desired: "has Full Access",
    },
  ],
  unchanged: [],
  warnings: [],
  mode: "dry-run",
};
const ready: PlanReady = { planId: "p1", plan };
const planReply: Reply = { type: MessageType.plan, data: ready };
const error: Reply = { type: MessageType.error, data: { code: "not-found", message: "mailbox 'x' not found" } };
const pong: Reply = { type: MessageType.pong, data: { product: "m365-aegis", version: "1" } };
const applied = (statuses: Array<"applied" | "failed" | "skipped">): PlanApplied => ({
  plan: {
    ...plan,
    mode: "applied",
    changes: statuses.map((status, index) => ({ ...plan.changes[0], detail: `change ${index}`, result: { status } })),
  },
});

const run = (events: WriteEvent[], from: WriteState = initialWriteState) => events.reduce(writeReducer, from);

describe("the write card's flow", () => {
  it("goes from the picks to the plan, the confirmation and the result, a step each", () => {
    const states: WriteState[] = [];
    let state = initialWriteState;
    for (const event of [
      { type: "request-plan" },
      { type: "reply", reply: planReply },
      { type: "proceed" },
      { type: "confirm" },
      { type: "reply", reply: { type: MessageType.planApplied, data: applied(["applied"]) } },
    ] as WriteEvent[]) {
      state = writeReducer(state, event);
      states.push(state);
    }

    expect(states.map((step) => step.step)).toEqual(["planning", "plan", "confirming", "applying", "result"]);
    expect(states.map(writeStepIndex)).toEqual([1, 1, 2, 3, 3]);
    expect(writeStepIndex(initialWriteState)).toBe(0);
    expect(run([{ type: "reset" }], state)).toEqual(initialWriteState);
  });

  it("goes back without sending: from the confirmation to the plan, from the plan to the picks", () => {
    const confirming = run([{ type: "request-plan" }, { type: "reply", reply: planReply }, { type: "proceed" }]);

    expect(run([{ type: "back" }], confirming).step).toBe("plan");
    expect(run([{ type: "back" }, { type: "back" }], confirming).step).toBe("pick");
  });

  it("shows a refusal of the plan and of the apply, and goes back to the picks from it", () => {
    const refused = run([{ type: "request-plan" }, { type: "reply", reply: error }]);
    const applyRefused = run([
      { type: "request-plan" },
      { type: "reply", reply: planReply },
      { type: "proceed" },
      { type: "confirm" },
      { type: "reply", reply: error },
    ]);

    expect(refused).toEqual({ step: "refused", error: error.data, applying: false });
    expect(applyRefused).toEqual({ step: "refused", error: error.data, applying: true });
    expect(run([{ type: "back" }], refused).step).toBe("pick");
    expect(writeStepIndex(refused)).toBe(1);
    expect(run([{ type: "request-plan" }], refused).step).toBe("planning");
  });

  it("leaves the state as it is for an event or a reply its step does not take", () => {
    const planning = run([{ type: "request-plan" }]);
    const plan = run([{ type: "reply", reply: planReply }], planning);
    const applying = run([{ type: "proceed" }, { type: "confirm" }], plan);

    expect(run([{ type: "proceed" }, { type: "confirm" }, { type: "back" }, { type: "request-plan" }], planning)).toBe(
      planning,
    );
    expect(run([{ type: "reply", reply: pong }], planning)).toBe(planning);
    expect(run([{ type: "reply", reply: pong }], applying)).toBe(applying);
    expect(run([{ type: "reply", reply: planReply }], plan)).toBe(plan);
    expect(run([{ type: "confirm" }], plan)).toBe(plan);
    expect(run([{ type: "back" }, { type: "request-plan" }], applying)).toBe(applying);
    expect(run([{ type: "reply", reply: planReply }], initialWriteState)).toBe(initialWriteState);
  });

  it("confirms only a plan with a change and an id", () => {
    const upToDate: PlanReady = { plan: { ...plan, changes: [], mode: "up-to-date" } };
    const withoutId: PlanReady = { plan };

    expect(canConfirm(ready)).toBe(true);
    expect(canConfirm(upToDate)).toBe(false);
    expect(canConfirm(withoutId)).toBe(false);
    const shown = run([{ type: "request-plan" }, { type: "reply", reply: { type: MessageType.plan, data: upToDate } }]);
    expect(run([{ type: "proceed" }], shown)).toBe(shown);
  });

  it("tells a full, a partial and a failed apply apart", () => {
    expect(outcome(applied(["applied", "applied"]))).toBe("done");
    expect(outcome(applied(["applied", "failed"]))).toBe("partial");
    expect(outcome(applied(["failed", "skipped"]))).toBe("failed");
  });
});

describe("what a write card's status region says (task 121)", () => {
  const applied = (statuses: Array<"applied" | "failed" | "skipped">): PlanApplied => ({
    plan: {
      ...plan,
      mode: "applied",
      changes: statuses.map((status, index) => ({ ...plan.changes[0], detail: `d${index}`, result: { status } })),
    },
  });

  it("names each step, and nothing on the picks or a refusal", () => {
    const two = { ...ready, plan: { ...plan, changes: [plan.changes[0], { ...plan.changes[0], detail: "d" }] } };
    expect(writeStatus({ step: "pick" })).toBe("");
    expect(writeStatus({ step: "refused", error: { code: "usage", message: "m" }, applying: false })).toBe("");
    expect(writeStatus({ step: "planning" })).toBe("Preparing the plan…");
    expect(writeStatus({ step: "plan", ready })).toBe("Plan: 1 change");
    expect(writeStatus({ step: "plan", ready: two })).toBe("Plan: 2 changes");
    expect(writeStatus({ step: "plan", ready: { ...ready, plan: { ...plan, changes: [] } } })).toBe(
      "Plan: nothing to change",
    );
    expect(writeStatus({ step: "confirming", ready: two })).toBe("Confirm: 2 changes");
    expect(writeStatus({ step: "applying", ready })).toBe("Making the changes…");
  });

  it("says how an apply ended", () => {
    expect(writeStatus({ step: "result", applied: applied(["applied", "applied"]) })).toBe(
      "Done: every change was made",
    );
    expect(writeStatus({ step: "result", applied: applied(["applied", "failed", "skipped"]) })).toBe(
      "Partly done: 1 of 3 changes made",
    );
    expect(writeStatus({ step: "result", applied: applied(["failed"]) })).toBe("Not done: no change was made");
  });
});
