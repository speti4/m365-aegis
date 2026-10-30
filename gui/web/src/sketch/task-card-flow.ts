/**
 * The task card design sketch (task 018): the flow of one card as a state machine, over made-up data. Nothing here
 * talks to the bridge or the core.
 *
 * SKETCH ONLY: the plan shape below is made up for the review with the support team. The window's mailbox write cards
 * (task 082, `components/write-flow.ts`) follow this flow over the core's real `Plan` (the host computes it as
 * `--dry-run` prints it, the confirmation applies it, D-006).
 */

export type CardId = "grant-full-access" | "add-group-owner" | "check-user-access";

/** What the user picked on a card: per field, the chosen values (one for a single choice). */
export type Picks = Readonly<Record<string, readonly string[]>>;

/** One change of a made-up plan. */
export interface SketchChange {
  /** What the change does to the target. */
  action: "add" | "remove";
  /** The object that changes (a mailbox, a group). */
  target: string;
  /** What changes, in the team's words. */
  detail: string;
}

/** A made-up plan (SKETCH ONLY, see the file's comment). */
export interface SketchPlan {
  /** One line: what the card is about to do. */
  title: string;
  /** What would change. */
  changes: SketchChange[];
  /** What is already as asked and is left alone. */
  unchanged: string[];
  /** What the user should know before confirming. */
  warnings: string[];
  /** A read-only card: the "plan" is the answer, there is nothing to confirm. */
  readOnly: boolean;
  /** For a read-only card, the lines of the answer. */
  answer?: string[];
}

/** One applied change. */
export interface SketchItemResult {
  target: string;
  detail: string;
  ok: boolean;
  /** Why it failed, when it did. */
  message?: string;
}

/** What applying a plan did: every change done, or some failed (a partial result, exit code 6 on the command line). */
export interface SketchOutcome {
  status: "done" | "partial";
  items: SketchItemResult[];
}

/** Why the core would not plan (the contract's code, one sentence, what to do). */
export interface SketchRefusal {
  code: string;
  message: string;
  hint?: string;
}

/** Where a card's flow is. */
export type FlowStep =
  | { step: "list" }
  | { step: "pick"; card: CardId; picks: Picks }
  | { step: "planning"; card: CardId; picks: Picks }
  | { step: "plan"; card: CardId; picks: Picks; plan: SketchPlan }
  | { step: "refused"; card: CardId; picks: Picks; refusal: SketchRefusal }
  | { step: "confirming"; card: CardId; picks: Picks; plan: SketchPlan }
  | { step: "applying"; card: CardId; picks: Picks; plan: SketchPlan }
  | { step: "result"; card: CardId; picks: Picks; plan: SketchPlan; outcome: SketchOutcome };

/** The sketch's whole state: whether the window is signed in, and the flow. */
export type SketchState = { signedIn: boolean } & FlowStep;

export type SketchEvent =
  | { type: "sign-in" }
  | { type: "sign-out" }
  | { type: "open"; card: CardId }
  | { type: "close" }
  | { type: "pick"; field: string; values: readonly string[] }
  | { type: "request-plan" }
  | { type: "plan-ready"; plan: SketchPlan }
  | { type: "plan-refused"; refusal: SketchRefusal }
  | { type: "back" }
  | { type: "proceed" }
  | { type: "confirm" }
  | { type: "applied"; outcome: SketchOutcome }
  | { type: "again" };

export const initialSketchState: SketchState = { signedIn: true, step: "list" };

/**
 * The flow of a card: open it from the list, pick the objects, ask for the plan (working), see it (or the core's
 * refusal), go on to the confirmation and confirm it (working), see the result (done or partial). A read-only card has no confirmation: its plan
 * is the answer. Signed out, no card opens and an open one closes. An event the current step does not take leaves
 * the state as it is.
 */
export function sketchReducer(state: SketchState, event: SketchEvent): SketchState {
  const { signedIn } = state;
  switch (event.type) {
    case "sign-in":
      return { ...state, signedIn: true };
    case "sign-out":
      return { signedIn: false, step: "list" };
    case "open":
      return signedIn && state.step === "list" ? { signedIn, step: "pick", card: event.card, picks: {} } : state;
    case "close":
      return { signedIn, step: "list" };
    case "pick":
      return state.step === "pick" ? { ...state, picks: { ...state.picks, [event.field]: event.values } } : state;
    case "request-plan":
      return state.step === "pick" && isComplete(state.card, state.picks)
        ? { signedIn, step: "planning", card: state.card, picks: state.picks }
        : state;
    case "plan-ready":
      return state.step === "planning"
        ? { signedIn, step: "plan", card: state.card, picks: state.picks, plan: event.plan }
        : state;
    case "plan-refused":
      return state.step === "planning"
        ? { signedIn, step: "refused", card: state.card, picks: state.picks, refusal: event.refusal }
        : state;
    case "back":
      if (state.step === "confirming") {
        return { signedIn, step: "plan", card: state.card, picks: state.picks, plan: state.plan };
      }
      return state.step === "plan" || state.step === "refused"
        ? { signedIn, step: "pick", card: state.card, picks: state.picks }
        : state;
    case "proceed":
      return state.step === "plan" && canConfirm(state.plan)
        ? { signedIn, step: "confirming", card: state.card, picks: state.picks, plan: state.plan }
        : state;
    case "confirm":
      return state.step === "confirming"
        ? { signedIn, step: "applying", card: state.card, picks: state.picks, plan: state.plan }
        : state;
    case "applied":
      return state.step === "applying"
        ? { signedIn, step: "result", card: state.card, picks: state.picks, plan: state.plan, outcome: event.outcome }
        : state;
    case "again":
      return state.step === "result" || state.step === "plan"
        ? { signedIn, step: "pick", card: state.card, picks: {} }
        : state;
  }
}

/** The fields each card asks for; a card's plan can be asked for once each has a value. */
export const requiredFields: Record<CardId, readonly string[]> = {
  "grant-full-access": ["mailbox", "users"],
  "add-group-owner": ["group", "owners"],
  "check-user-access": ["user"],
};

/** Whether every field of the card has a value. */
export function isComplete(card: CardId, picks: Picks): boolean {
  return requiredFields[card].every((field) => (picks[field]?.length ?? 0) > 0);
}

/** Whether a plan can be confirmed: a write with at least one change. */
export function canConfirm(plan: SketchPlan): boolean {
  return !plan.readOnly && plan.changes.length > 0;
}

/** The four steps the card shows above its body, and which one is current. */
export const stepNames = ["Pick", "Plan", "Confirm", "Result"] as const;

export function currentStepIndex(state: FlowStep): number {
  switch (state.step) {
    case "list":
    case "pick":
      return 0;
    case "planning":
    case "plan":
    case "refused":
      return 1;
    case "confirming":
      return 2;
    case "applying":
    case "result":
      return 3;
  }
}
