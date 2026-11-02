import { ArrowLeft, CircleCheck, CircleMinus, CircleX, LoaderCircle, TriangleAlert } from "lucide-react";
import { type ReactNode, type Ref, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { focusTarget } from "@/lib/focus";
import { cn } from "@/lib/utils";
import type { BridgeError, Plan, PlanApplied, PlanChange } from "../bridge/protocol";
import { confirmAction, count, windowWarning } from "./plan-wording";
import { outcome, type WriteState, writeStepIndex, writeStepNames } from "./write-flow";

/**
 * The parts of a write card (task 082), as the task card sketch of task 018 drew them, over the core's `Plan`: the
 * steps, the plan (what changes from what to what, what is left as it is, the warnings), the confirmation that
 * stands for `--yes`, and the result of each change with the plan record's file. What the card writes names the card's
 * own controls, never a flag of the command line (task 090).
 */

/**
 * The steps above the card's body: Pick › Plan › Confirm › Result. The current step's item carries `aria-current`,
 * and each step says whether it is done or current in words a screen reader reads (task 121); the separators are
 * drawn only.
 */
export function Stepper({ state }: { state: WriteState }) {
  const index = writeStepIndex(state);
  return (
    <ol className="flex flex-wrap items-center gap-2 text-xs" aria-label="Steps">
      {writeStepNames.map((name, position) => (
        <li key={name} className="flex items-center gap-2" aria-current={position === index ? "step" : undefined}>
          <span
            className={cn(
              "flex size-5 items-center justify-center rounded-full border text-xs",
              position < index &&
                "border-transparent bg-muted text-muted-foreground forced-colors:bg-[color:CanvasText] forced-colors:text-[color:Canvas] forced-colors:forced-color-adjust-none",
              position === index && "border-primary font-semibold",
              position > index && "text-muted-foreground",
            )}
          >
            {position + 1}
          </span>
          <span className={cn(position === index ? "font-medium" : "text-muted-foreground")}>
            {name}
            {position < index ? <span className="sr-only"> (done)</span> : null}
            {position === index ? <span className="sr-only"> (current)</span> : null}
          </span>
          {position < writeStepNames.length - 1 ? (
            <span className="text-muted-foreground" aria-hidden>
              ›
            </span>
          ) : null}
        </li>
      ))}
    </ol>
  );
}

/** A line that something is under way; `ref` makes it the card's focus target while it waits (task 121). */
export function Working({ text, ref }: { text: string; ref?: Ref<HTMLParagraphElement> }) {
  return (
    <p
      ref={ref}
      tabIndex={-1}
      className={cn("flex items-center gap-2 text-muted-foreground", focusTarget)}
      role="status"
    >
      <LoaderCircle className="animate-spin" aria-hidden /> {text}
    </p>
  );
}

/**
 * A framed message with an icon: an error is an alert, the rest a status. `id` lets a field name it as its
 * description; `ref` makes it a focus target of the write card (task 121).
 */
export function Notice({
  id,
  ref,
  tone,
  icon,
  title,
  children,
}: {
  id?: string;
  ref?: Ref<HTMLDivElement>;
  tone: "info" | "success" | "warning" | "error";
  icon: ReactNode;
  title: string;
  children: ReactNode;
}) {
  return (
    <div
      id={id}
      ref={ref}
      tabIndex={ref ? -1 : undefined}
      className={cn(
        "flex gap-3 rounded-lg border p-3 text-sm [&>svg]:mt-0.5 [&>svg]:size-4 [&>svg]:shrink-0",
        focusTarget,
        tone === "error" && "border-danger/50 [&>svg]:text-danger",
        tone === "warning" && "border-warning/50 [&>svg]:text-warning",
        tone === "success" && "border-success/50 [&>svg]:text-success",
      )}
      role={tone === "error" ? "alert" : "status"}
    >
      {icon}
      <div>
        <p className="font-medium">{title}</p>
        <div>{children}</div>
      </div>
    </div>
  );
}

/** A failure's sentence, what to do about it, and its code. */
export function ErrorText({ error }: { error: BridgeError }) {
  return (
    <>
      <p>{error.message}</p>
      {error.hint ? <p className="mt-1 text-muted-foreground">What to do: {error.hint}</p> : null}
      <p className="mt-1 font-mono text-xs text-muted-foreground">{error.code}</p>
    </>
  );
}

/** A change's badge by what it does: green adds, red takes away, amber changes a value. */
function actionVariant(action: PlanChange["action"]): "success" | "danger" | "warning" {
  if (action === "add") {
    return "success";
  }
  return action === "remove" || action === "clear" ? "danger" : "warning";
}

/** One change: the action's badge, the target, what changes, and the state now → after, the after state strong. */
function ChangeItem({ change }: { change: PlanChange }) {
  return (
    <li className="flex items-start gap-2 py-2 first:pt-0 last:pb-0">
      <Badge variant={actionVariant(change.action)}>{change.action}</Badge>
      <span>
        <strong>{change.target}</strong>: {change.detail}
        <span className="block text-sm text-muted-foreground">
          {change.current} → <span className="font-semibold text-foreground">{change.desired}</span>
        </span>
      </span>
    </li>
  );
}

/**
 * The change ticket of D-015 (task 127): the plan and the confirmation, the one moment the window changes the tenant,
 * on a warm surface with an amber rule on the left; everything else on the page stays quiet.
 */
function ChangeTicket({ children }: { children: ReactNode }) {
  return (
    <div className="space-y-3 rounded-lg border border-l-4 border-l-warning bg-ticket p-4" data-testid="change-ticket">
      {children}
    </div>
  );
}

/**
 * The plan as the command line prints it: the title, each change, what is left as it is, and the warnings, a warning
 * that names a flag in the window's words (`windowWarning`, task 090).
 */
export function PlanView({ plan, titleRef }: { plan: Plan; titleRef?: Ref<HTMLParagraphElement> }) {
  const Surface = plan.changes.length === 0 ? PlainSurface : ChangeTicket;
  return (
    <Surface>
      <p ref={titleRef} tabIndex={-1} className={cn("text-base font-medium", focusTarget)}>
        {plan.title}
      </p>
      {plan.changes.length === 0 ? (
        <Notice tone="info" icon={<CircleCheck aria-hidden />} title="Nothing to do">
          Everything is already as asked, so there is nothing to confirm.
        </Notice>
      ) : (
        <div className="space-y-1">
          <p className="text-sm text-muted-foreground">
            {plan.changes.length} change(s) when you confirm, and none before:
          </p>
          <ul className="divide-y">
            {plan.changes.map((change) => (
              <ChangeItem key={`${change.target}:${change.detail}`} change={change} />
            ))}
          </ul>
        </div>
      )}
      {plan.unchanged.length > 0 ? (
        <div className="space-y-1">
          <p className="text-sm text-muted-foreground">Left as it is:</p>
          <ul className="list-disc pl-5 text-sm text-muted-foreground">
            {plan.unchanged.map((line) => (
              <li key={`${line.target}:${line.detail}`}>
                {line.target}: {line.detail} ({line.state})
              </li>
            ))}
          </ul>
        </div>
      ) : null}
      {plan.warnings.map((warning) => (
        <Notice key={warning} tone="warning" icon={<TriangleAlert aria-hidden />} title="Good to know">
          {windowWarning(warning)}
        </Notice>
      ))}
    </Surface>
  );
}

/** A plan with nothing to change is no ticket. */
function PlainSurface({ children }: { children: ReactNode }) {
  return <div className="space-y-3">{children}</div>;
}

/** The account a change runs as, from the sign-in card's state. */
export interface RunAs {
  account: string;
  profile: string;
}

/**
 * The confirmation (task 120): what the plan showed, once more: each change with its action and its from → to, the
 * warnings in short, and the account and profile the change runs as; a checkbox, and the one button that sends a
 * write, named by what it does (`confirmAction`: "Grant permissions", "Stop forwarding"…), destructive for a removal.
 * It stands for `--yes`. "Back to the plan" and "Cancel" (back to the picks) send nothing. `leadRef` is the card's
 * focus target on this step (task 121).
 */
export function ConfirmView({
  plan,
  runAs,
  onConfirm,
  onBack,
  onCancel,
  leadRef,
}: {
  plan: Plan;
  runAs?: RunAs;
  onConfirm: () => void;
  onBack: () => void;
  onCancel: () => void;
  leadRef?: Ref<HTMLParagraphElement>;
}) {
  const [checked, setChecked] = useState(false);
  const action = confirmAction(plan.operation);
  const changes = plan.changes.length === 1 ? "this change" : `these ${count(plan.changes.length, "change")}`;
  return (
    <div className="space-y-4">
      <ChangeTicket>
        <div className="space-y-1">
          <p ref={leadRef} tabIndex={-1} className={focusTarget} data-testid="confirm-lead">
            You're about to make {changes}
            {runAs ? (
              <>
                {" "}
                as <strong>{runAs.account}</strong> (profile {runAs.profile})
              </>
            ) : null}
            :
          </p>
          <p className="text-base font-medium">{plan.title}</p>
        </div>
        <ul className="divide-y">
          {plan.changes.map((change) => (
            <ChangeItem key={`${change.target}:${change.detail}`} change={change} />
          ))}
        </ul>
        {plan.warnings.length > 0 ? (
          <div className="rounded-lg border border-warning/50 p-3 text-sm" data-testid="confirm-warnings">
            <p className="flex items-center gap-2 font-medium">
              <TriangleAlert className="size-4 text-warning" aria-hidden /> Good to know
            </p>
            <ul className="mt-1 list-disc space-y-1 pl-5">
              {plan.warnings.map((warning) => (
                <li key={warning}>{windowWarning(warning)}</li>
              ))}
            </ul>
          </div>
        ) : null}
      </ChangeTicket>
      <label className="flex items-center gap-2">
        <input
          type="checkbox"
          className="size-4 accent-primary"
          checked={checked}
          onChange={(event) => setChecked(event.target.checked)}
        />
        I've reviewed these changes.
      </label>
      <div className="flex flex-wrap gap-2">
        <Button variant={action.destructive ? "destructive" : "default"} disabled={!checked} onClick={onConfirm}>
          {action.label}
        </Button>
        <Button variant="outline" onClick={onBack}>
          <ArrowLeft aria-hidden /> Back to the plan
        </Button>
        <Button variant="ghost" onClick={onCancel}>
          Cancel
        </Button>
      </div>
      <p className="text-xs text-muted-foreground">
        Going back or canceling changes nothing: the window sends no change until you confirm.
      </p>
    </div>
  );
}

function ResultIcon({ change }: { change: PlanChange }) {
  switch (change.result?.status) {
    case "applied":
      return <CircleCheck className="mt-0.5 size-4 shrink-0 text-success" aria-label="applied" />;
    case "failed":
      return <CircleX className="mt-0.5 size-4 shrink-0 text-danger" aria-label="failed" />;
    default:
      return <CircleMinus className="mt-0.5 size-4 shrink-0 text-muted-foreground" aria-label="skipped" />;
  }
}

/**
 * The applied plan: how it ended, each change's result with the service's message, and the plan record's file;
 * `noticeRef` makes how it ended the card's focus target (task 121).
 */
export function ResultView({ applied, noticeRef }: { applied: PlanApplied; noticeRef?: Ref<HTMLDivElement> }) {
  const { plan, error } = applied;
  const total = plan.changes.length;
  const done = plan.changes.filter((change) => change.result?.status === "applied").length;
  const ended = outcome(applied);
  return (
    <div className="space-y-3">
      {ended === "done" ? (
        <Notice ref={noticeRef} tone="success" icon={<CircleCheck aria-hidden />} title="Done">
          Every change was made.
        </Notice>
      ) : (
        <Notice
          ref={noticeRef}
          tone={ended === "partial" ? "warning" : "error"}
          icon={<TriangleAlert aria-hidden />}
          title={ended === "partial" ? "Partly done" : "Not done"}
        >
          <p>
            {done} of {total} change(s) were made.
          </p>
          {error ? <ErrorText error={error} /> : null}
        </Notice>
      )}
      <ul className="space-y-2">
        {plan.changes.map((change) => (
          <li key={`${change.target}:${change.detail}`} className="flex items-start gap-2">
            <ResultIcon change={change} />
            <span>
              <strong>{change.target}</strong>: {change.detail}
              {change.result?.error ? (
                <span className="block text-sm text-muted-foreground">
                  {change.result.error.message}
                  {change.result.error.hint ? ` (${change.result.error.hint})` : ""}
                </span>
              ) : null}
            </span>
          </li>
        ))}
      </ul>
      {plan.record ? (
        <p className="text-xs text-muted-foreground">
          The plan record: <span className="font-mono">{plan.record}</span>
        </p>
      ) : null}
    </div>
  );
}
