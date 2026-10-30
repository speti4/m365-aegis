import { describe, expect, it } from "vitest";
import { cardById, isRefusal, labelOf, planFor } from "./sketch-data";
import {
  currentStepIndex,
  initialSketchState,
  type SketchPlan,
  type SketchState,
  sketchReducer,
} from "./task-card-flow";

const plan = (card: Parameters<typeof planFor>[0], picks: Parameters<typeof planFor>[1]): SketchPlan => {
  const answer = planFor(card, picks, "normal");
  if (isRefusal(answer)) {
    throw new Error("a refusal");
  }
  return answer;
};

/** The sketch's made-up data at its edges (task 070): what the flow tests leave. */
describe("the sketch's made-up data", () => {
  it("knows its three cards and nothing else", () => {
    expect(cardById("check-user-access").writes).toBe(false);
    expect(() => cardById("delete-everything" as Parameters<typeof cardById>[0])).toThrow("no card delete-everything");
  });

  it("names a value it has no label for by the value", () => {
    expect(labelOf("grant-full-access", "users", "dave@contoso.com")).toBe("dave@contoso.com");
    expect(labelOf("grant-full-access", "nothing", "x")).toBe("x");
  });

  it("plans nothing and warns of nothing when every colleague already holds the right", () => {
    const grant = plan("grant-full-access", { mailbox: ["finance@contoso.com"], users: ["alice@contoso.com"] });
    expect(grant.changes).toEqual([]);
    expect(grant.warnings).toEqual([]);
    expect(grant.unchanged).toEqual(["Alice Example already holds FullAccess on Finance"]);

    const owner = plan("add-group-owner", { group: ["finance"], owners: ["alice@contoso.com"] });
    expect(owner.changes).toEqual([]);
    expect(owner.warnings).toEqual([]);
    expect(owner.unchanged).toEqual(["Alice Example already owns Finance"]);
  });

  it("plans over picks that are missing or unknown", () => {
    expect(plan("grant-full-access", {})).toMatchObject({ title: "Grant FullAccess on ", changes: [], unchanged: [] });
    expect(
      plan("grant-full-access", { mailbox: ["archive@contoso.com"], users: ["bob@contoso.com"] }).changes,
    ).toHaveLength(1);
    expect(plan("add-group-owner", {})).toMatchObject({ title: "Add owners to ", changes: [], unchanged: [] });
    expect(plan("add-group-owner", { group: ["sales"], owners: ["bob@contoso.com"] }).changes).toHaveLength(1);
    expect(plan("check-user-access", {}).answer).toEqual(["No shared mailbox and no group ownership."]);
  });

  it("answers a check with the owned group, and says so when there is nothing", () => {
    expect(plan("check-user-access", { user: ["alice@contoso.com"] }).answer).toEqual([
      "FullAccess on Finance",
      "FullAccess on Reception",
      "Owner of Finance",
    ]);
    expect(plan("check-user-access", { user: ["carol@contoso.com"] }).answer).toEqual([
      "No shared mailbox and no group ownership.",
    ]);
  });

  it("refuses a mailbox card for the missing Exchange role", () => {
    const refused = planFor("grant-full-access", {}, "refused");
    expect(isRefusal(refused) && refused.hint).toBe("ask an administrator for the Recipient Management role");
  });
});

describe("the sketch's flow at its edges", () => {
  const list: SketchState = initialSketchState;
  const pick = sketchReducer(list, { type: "open", card: "check-user-access" });

  it("ignores the steps the current one does not take", () => {
    expect(sketchReducer(list, { type: "pick", field: "user", values: ["bob@contoso.com"] })).toBe(list);
    expect(sketchReducer(pick, { type: "plan-refused", refusal: { code: "forbidden", message: "no" } })).toBe(pick);
    expect(sketchReducer(list, { type: "back" })).toBe(list);
    expect(sketchReducer(pick, { type: "again" })).toBe(pick);
  });

  it("is on the first step in the list", () => {
    expect(currentStepIndex(list)).toBe(0);
  });
});
