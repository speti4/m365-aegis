import { ArrowLeft, CircleCheck, CircleMinus, CircleX, LoaderCircle, TriangleAlert } from "lucide-react";
import { type ReactNode, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import type { BridgeError, Plan, PlanApplied, PlanChange } from "../bridge/protocol";
import { windowWarning } from "./plan-wording";
import { outcome, type WriteState, writeStepIndex, writeStepNames } from "./write-flow";

/**
 * The parts of a write card (task 082), as the task card sketch of task 018 drew them, over the core's `Plan`: the
 * steps, the plan (what changes from what to what, what is left as it is, the warnings), the confirmation that
 * stands for `--yes`, and the result of each change with the plan record's file. What the card writes names the card's
 * own controls, never a flag of the command line (task 090).
 */

/** The steps above the card's body: Pick › Plan › Confirm › Result. */
export function Stepper({ state }: { state: WriteState }) {
  const index = writeStepIndex(state);
  return (
    <ol className="flex flex-wrap items-center gap-2 text-sm" aria-label="Steps">
      {writeStepNames.map((name, position) => (
        <li key={name} className="flex items-center gap-2">
          <span
            className={cn(
              "flex size-6 items-center justify-center rounded-full border text-xs",
              position < index && "border-primary bg-primary text-primary-foreground",
              position === index && "border-primary font-semibold",
              position > index && "text-muted-foreground",
            )}
            aria-current={position === index ? "step" : undefined}
          >
            {position + 1}
          </span>
          <span className={cn(position === index ? "font-medium" : "text-muted-foreground")}>{name}</span>
          {position < writeStepNames.length - 1 ? <span className="text-muted-foreground">›</span> : null}
        </li>
      ))}
    </ol>
  );
}

/** A line that something is under way. */
export function Working({ text }: { text: string }) {
  return (
    <p className="flex items-center gap-2 text-muted-foreground" role="status">
      <LoaderCircle className="animate-spin" aria-hidden /> {text}
    </p>
  );
}

/** A framed message with an icon: an error is an alert, the rest a status. */
export function Notice({
  tone,
  icon,
  title,
  children,
}: {
  tone: "info" | "success" | "warning" | "error";
  icon: ReactNode;
  title: string;
  children: ReactNode;
}) {
  return (
    <div
      className={cn(
        "flex gap-3 rounded-lg border p-3 text-sm [&>svg]:mt-0.5 [&>svg]:size-4 [&>svg]:shrink-0",
        tone === "error" && "border-destructive/50 [&>svg]:text-destructive",
        tone === "warning" && "border-amber-500/50 [&>svg]:text-amber-600 dark:[&>svg]:text-amber-400",
        tone === "success" && "border-emerald-500/50 [&>svg]:text-emerald-600 dark:[&>svg]:text-emerald-400",
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

function ChangeLine({ change }: { change: PlanChange }) {
  return (
    <span>
      <strong>{change.target}</strong>: {change.detail}
      <span className="block text-sm text-muted-foreground">
        {change.current} → {change.desired}
      </span>
    </span>
  );
}

/**
 * The plan as the command line prints it: the title, each change, what is left as it is, and the warnings, a warning
 * that names a flag in the window's words (`windowWarning`, task 090).
 */
export function PlanView({ plan }: { plan: Plan }) {
  return (
    <div className="space-y-3">
      <p className="font-medium">{plan.title}</p>
      {plan.changes.length === 0 ? (
        <Notice tone="info" icon={<CircleCheck aria-hidden />} title="Nothing to do">
          Everything is already as asked, so there is nothing to confirm.
        </Notice>
      ) : (
        <div className="space-y-1">
          <p className="text-sm text-muted-foreground">
            {plan.changes.length} change(s) when you confirm, and none before:
          </p>
          <ul className="space-y-2">
            {plan.changes.map((change) => (
              <li key={`${change.target}:${change.detail}`} className="flex items-start gap-2">
                <Badge variant={change.action === "remove" || change.action === "clear" ? "destructive" : "default"}>
                  {change.action}
                </Badge>
                <ChangeLine change={change} />
              </li>
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
    </div>
  );
}

/**
 * The confirmation: the changes once more and a checkbox; "Confirm and apply" stands for `--yes` and is the one
 * button that sends a write. Going back sends nothing.
 */
export function ConfirmView({ plan, onConfirm, onBack }: { plan: Plan; onConfirm: () => void; onBack: () => void }) {
  const [checked, setChecked] = useState(false);
  return (
    <div className="space-y-4">
      <p>
        You are about to make {plan.changes.length} change(s): <strong>{plan.title}</strong>.
      </p>
      <ul className="list-disc pl-5">
        {plan.changes.map((change) => (
          <li key={`${change.target}:${change.detail}`}>
            {change.target}: {change.detail}
          </li>
        ))}
      </ul>
      <label className="flex items-center gap-2">
        <input
          type="checkbox"
          className="size-4 accent-primary"
          checked={checked}
          onChange={(event) => setChecked(event.target.checked)}
        />
        I checked the plan; make these changes.
      </label>
      <div className="flex flex-wrap gap-2">
        <Button disabled={!checked} onClick={onConfirm}>
          Confirm and apply
        </Button>
        <Button variant="outline" onClick={onBack}>
          <ArrowLeft aria-hidden /> Back to the plan
        </Button>
      </div>
      <p className="text-xs text-muted-foreground">
        Going back changes nothing: the window sends no change until you confirm.
      </p>
    </div>
  );
}

function ResultIcon({ change }: { change: PlanChange }) {
  switch (change.result?.status) {
    case "applied":
      return <CircleCheck className="mt-0.5 size-4 shrink-0" aria-label="applied" />;
    case "failed":
      return <CircleX className="mt-0.5 size-4 shrink-0 text-destructive" aria-label="failed" />;
    default:
      return <CircleMinus className="mt-0.5 size-4 shrink-0 text-muted-foreground" aria-label="skipped" />;
  }
}

/** The applied plan: how it ended, each change's result with the service's message, and the plan record's file. */
export function ResultView({ applied }: { applied: PlanApplied }) {
  const { plan, error } = applied;
  const total = plan.changes.length;
  const done = plan.changes.filter((change) => change.result?.status === "applied").length;
  const ended = outcome(applied);
  return (
    <div className="space-y-3">
      {ended === "done" ? (
        <Notice tone="success" icon={<CircleCheck aria-hidden />} title="Done">
          Every change was made.
        </Notice>
      ) : (
        <Notice
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
