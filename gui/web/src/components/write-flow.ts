import { type BridgeError, MessageType, type PlanApplied, type PlanReady, type Reply } from "../bridge/protocol";

/**
 * The flow of a write card (task 082), the sketch's flow of task 018 over the core's real `Plan`: pick the objects,
 * ask for the plan (the host plans it as `--dry-run` prints it; nothing is sent), see it or the refusal, go on to the
 * confirmation and confirm it (the `apply-plan` with `confirm: true`, which stands for `--yes`), see the result. Going
 * back, at any step before the confirmation, sends nothing: the host's plan waits until its time runs out. Signed
 * out, the card is back at its picks. A reply the current step does not wait for leaves the state as it is.
 */
export type WriteState =
  | { step: "pick" }
  | { step: "planning" }
  /** The host would not plan, or not apply; `applying` when the error answered the confirmed apply. */
  | { step: "refused"; error: BridgeError; applying: boolean }
  | { step: "plan"; ready: PlanReady }
  | { step: "confirming"; ready: PlanReady }
  | { step: "applying"; ready: PlanReady }
  | { step: "result"; applied: PlanApplied };

export type WriteEvent =
  | { type: "request-plan" }
  | { type: "reply"; reply: Reply }
  | { type: "proceed" }
  | { type: "back" }
  | { type: "confirm" }
  | { type: "reset" };

export const initialWriteState: WriteState = { step: "pick" };

export function writeReducer(state: WriteState, event: WriteEvent): WriteState {
  switch (event.type) {
    case "request-plan":
      return state.step === "pick" || state.step === "refused" ? { step: "planning" } : state;
    case "reply":
      return onReply(state, event.reply);
    case "proceed":
      return state.step === "plan" && canConfirm(state.ready) ? { step: "confirming", ready: state.ready } : state;
    case "back":
      if (state.step === "confirming") {
        return { step: "plan", ready: state.ready };
      }
      return state.step === "plan" || state.step === "refused" ? { step: "pick" } : state;
    case "confirm":
      return state.step === "confirming" ? { step: "applying", ready: state.ready } : state;
    case "reset":
      return initialWriteState;
  }
}

function onReply(state: WriteState, reply: Reply): WriteState {
  if (state.step === "planning") {
    if (reply.type === MessageType.plan) {
      return { step: "plan", ready: reply.data };
    }
    return reply.type === MessageType.error ? { step: "refused", error: reply.data, applying: false } : state;
  }
  if (state.step === "applying") {
    if (reply.type === MessageType.planApplied) {
      return { step: "result", applied: reply.data };
    }
    return reply.type === MessageType.error ? { step: "refused", error: reply.data, applying: true } : state;
  }
  return state;
}

/** Whether a plan can be confirmed: it has a change to send and the id the host holds it under. */
export function canConfirm(ready: PlanReady): boolean {
  return ready.planId !== undefined && ready.plan.changes.length > 0;
}

/** The four steps the card shows above its body, as the sketch names them. */
export const writeStepNames = ["Pick", "Plan", "Confirm", "Result"] as const;

/** Which of the four steps is current. */
export function writeStepIndex(state: WriteState): number {
  switch (state.step) {
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

/** How an applied plan ended: every change applied, some, or none. */
export function outcome(applied: PlanApplied): "done" | "partial" | "failed" {
  const done = applied.plan.changes.filter((change) => change.result?.status === "applied").length;
  if (done === applied.plan.changes.length) {
    return "done";
  }
  return done === 0 ? "failed" : "partial";
}
