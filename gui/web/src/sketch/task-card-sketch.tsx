import {
  ArrowLeft,
  CircleCheck,
  CircleX,
  Info,
  LoaderCircle,
  LogIn,
  Mail,
  Search,
  TriangleAlert,
  UsersRound,
} from "lucide-react";
import { type ReactNode, useEffect, useReducer, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { cn } from "@/lib/utils";
import { type CardDefinition, cardById, cards, isRefusal, outcomeFor, planFor, type Scenario } from "./sketch-data";
import {
  canConfirm,
  currentStepIndex,
  type FlowStep,
  initialSketchState,
  isComplete,
  type SketchOutcome,
  type SketchPlan,
  type SketchState,
  sketchReducer,
  stepNames,
} from "./task-card-flow";

/** How long the made-up core "works", so the working state shows. */
const WORK_MS = 900;

type Theme = "system" | "light" | "dark";

/**
 * The task card design sketch (task 018), for the review with the support team: the card list, one card's steps
 * (pick, plan, confirm, result) and the states (signed out, working, refused, partial), light and dark, over
 * made-up data. A dev-only page (`npm run dev`, `#/sketch/task-cards`): no bridge request, nothing in the window.
 */
export default function TaskCardSketch() {
  const [state, dispatch] = useReducer(sketchReducer, initialSketchState);
  const [scenario, setScenario] = useState<Scenario>("normal");
  const [theme, setTheme] = useState<Theme>("system");

  // The made-up core: answers the plan request and the confirmation after a moment.
  useEffect(() => {
    if (state.step === "planning") {
      const timer = setTimeout(() => {
        const answer = planFor(state.card, state.picks, scenario);
        dispatch(isRefusal(answer) ? { type: "plan-refused", refusal: answer } : { type: "plan-ready", plan: answer });
      }, WORK_MS);
      return () => clearTimeout(timer);
    }
    if (state.step === "applying") {
      const timer = setTimeout(() => dispatch({ type: "applied", outcome: outcomeFor(state.plan, scenario) }), WORK_MS);
      return () => clearTimeout(timer);
    }
    return undefined;
  }, [state, scenario]);

  return (
    <div className={cn("min-h-screen bg-background text-foreground", theme === "system" ? null : theme)}>
      <div className="mx-auto max-w-5xl space-y-6 p-6">
        <header className="space-y-3">
          <div className="flex flex-wrap items-baseline gap-3">
            <h1 className="text-2xl font-semibold tracking-tight">Task cards</h1>
            <Badge variant="outline">design sketch · made-up data · not in the window</Badge>
          </div>
          <p className="max-w-3xl text-sm text-muted-foreground">
            How the support team would run a task from the window: pick a card, pick the objects, read the plan the core
            computed, confirm it, see the result. Nothing here reaches a tenant; the plan's shape is made up, and the
            window's mailbox write cards follow these steps over the core's real plan (task 082).
          </p>
          <Toolbar
            signedIn={state.signedIn}
            onSignedIn={(value) => dispatch({ type: value ? "sign-in" : "sign-out" })}
            scenario={scenario}
            onScenario={setScenario}
            theme={theme}
            onTheme={setTheme}
          />
        </header>
        {state.step === "list" ? (
          <CardList signedIn={state.signedIn} onOpen={(card) => dispatch({ type: "open", card })} />
        ) : (
          <OpenCard state={state} dispatch={dispatch} />
        )}
      </div>
    </div>
  );
}

function Toolbar(props: {
  signedIn: boolean;
  onSignedIn: (value: boolean) => void;
  scenario: Scenario;
  onScenario: (value: Scenario) => void;
  theme: Theme;
  onTheme: (value: Theme) => void;
}) {
  return (
    <div className="flex flex-wrap items-center gap-x-6 gap-y-2 rounded-lg border border-dashed p-3 text-sm">
      <span className="text-muted-foreground">Sketch controls:</span>
      <Segmented
        label="Sign-in"
        value={props.signedIn ? "in" : "out"}
        options={[
          ["in", "signed in"],
          ["out", "signed out"],
        ]}
        onChange={(value) => props.onSignedIn(value === "in")}
      />
      <Segmented
        label="The core answers"
        value={props.scenario}
        options={[
          ["normal", "normally"],
          ["refused", "with a refusal"],
          ["partial", "with a partial result"],
        ]}
        onChange={(value) => props.onScenario(value as Scenario)}
      />
      <Segmented
        label="Theme"
        value={props.theme}
        options={[
          ["system", "system"],
          ["light", "light"],
          ["dark", "dark"],
        ]}
        onChange={(value) => props.onTheme(value as Theme)}
      />
    </div>
  );
}

function Segmented(props: {
  label: string;
  value: string;
  options: ReadonlyArray<readonly [string, string]>;
  onChange: (value: string) => void;
}) {
  return (
    <div className="flex items-center gap-2" role="radiogroup" aria-label={props.label}>
      <span>{props.label}</span>
      {props.options.map(([value, text]) => (
        <Button
          key={value}
          size="xs"
          variant={props.value === value ? "default" : "outline"}
          role="radio"
          aria-checked={props.value === value}
          onClick={() => props.onChange(value)}
        >
          {text}
        </Button>
      ))}
    </div>
  );
}

function CardIcon({ card }: { card: CardDefinition }) {
  if (!card.writes) {
    return <Search aria-hidden />;
  }
  return card.domain === "Groups" ? <UsersRound aria-hidden /> : <Mail aria-hidden />;
}

function CardList({ signedIn, onOpen }: { signedIn: boolean; onOpen: (card: CardDefinition["id"]) => void }) {
  return (
    <section className="space-y-4">
      {signedIn ? null : (
        <Notice tone="warning" icon={<LogIn aria-hidden />} title="Signed out">
          The cards need a sign-in. Sign in from the sign-in card at the top of the window, then come back here.
        </Notice>
      )}
      <div className="grid gap-4 md:grid-cols-3">
        {cards.map((card) => (
          <Card key={card.id} className={cn("flex flex-col", !signedIn && "opacity-60")}>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <CardIcon card={card} />
                {card.title}
              </CardTitle>
              <CardDescription>{card.description}</CardDescription>
            </CardHeader>
            <CardContent className="flex flex-wrap gap-2">
              <Badge variant="secondary">{card.domain}</Badge>
              <Badge variant="outline">{card.writes ? "changes the tenant: plan, then confirm" : "reads only"}</Badge>
            </CardContent>
            <CardFooter className="mt-auto">
              <Button disabled={!signedIn} onClick={() => onOpen(card.id)}>
                Start
              </Button>
            </CardFooter>
          </Card>
        ))}
      </div>
    </section>
  );
}

function Stepper({ state, writes }: { state: FlowStep; writes: boolean }) {
  const current = currentStepIndex(state);
  const names = writes ? stepNames : (["Pick", "Answer"] as const);
  const index = writes ? current : Math.min(current, 1);
  return (
    <ol className="flex flex-wrap items-center gap-2 text-sm" aria-label="Steps">
      {names.map((name, position) => (
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
          {position < names.length - 1 ? <span className="text-muted-foreground">›</span> : null}
        </li>
      ))}
    </ol>
  );
}

function OpenCard({
  state,
  dispatch,
}: {
  state: Exclude<SketchState, { step: "list" }>;
  dispatch: (event: Parameters<typeof sketchReducer>[1]) => void;
}) {
  const card = cardById(state.card);
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <CardIcon card={card} />
          {card.title}
        </CardTitle>
        <CardDescription>{card.description}</CardDescription>
        <CardAction>
          <Button variant="ghost" size="sm" onClick={() => dispatch({ type: "close" })}>
            <ArrowLeft aria-hidden /> All cards
          </Button>
        </CardAction>
      </CardHeader>
      <CardContent className="space-y-5">
        <Stepper state={state} writes={card.writes} />
        <CardBody state={state} card={card} dispatch={dispatch} />
      </CardContent>
    </Card>
  );
}

function CardBody({
  state,
  card,
  dispatch,
}: {
  state: Exclude<SketchState, { step: "list" }>;
  card: CardDefinition;
  dispatch: (event: Parameters<typeof sketchReducer>[1]) => void;
}) {
  switch (state.step) {
    case "pick":
      return (
        <div className="space-y-5">
          {card.fields.map((field) => (
            <fieldset key={field.name} className="space-y-2">
              <legend className="text-sm font-medium">{field.label}</legend>
              <div className="flex flex-wrap gap-2">
                {field.options.map((option) => {
                  const picked = state.picks[field.name] ?? [];
                  const chosen = picked.includes(option.value);
                  const values = field.multiple
                    ? chosen
                      ? picked.filter((value) => value !== option.value)
                      : [...picked, option.value]
                    : [option.value];
                  return (
                    <Button
                      key={option.value}
                      variant={chosen ? "default" : "outline"}
                      size="sm"
                      role={field.multiple ? "checkbox" : "radio"}
                      aria-checked={chosen}
                      onClick={() => dispatch({ type: "pick", field: field.name, values })}
                    >
                      {chosen ? <CircleCheck aria-hidden /> : null}
                      {option.label}
                      <span className="text-xs opacity-70">{option.value.includes("@") ? option.value : ""}</span>
                    </Button>
                  );
                })}
              </div>
            </fieldset>
          ))}
          <Button disabled={!isComplete(state.card, state.picks)} onClick={() => dispatch({ type: "request-plan" })}>
            {card.writes ? "See the plan" : "Check"}
          </Button>
        </div>
      );
    case "planning":
      return <Working text={card.writes ? "The core is computing the plan…" : "Reading the access…"} />;
    case "refused":
      return (
        <div className="space-y-4">
          <Notice tone="error" icon={<CircleX aria-hidden />} title="Refused: nothing was changed">
            <p>{state.refusal.message}</p>
            {state.refusal.hint ? <p className="mt-1 text-muted-foreground">What to do: {state.refusal.hint}</p> : null}
            <p className="mt-1 font-mono text-xs text-muted-foreground">{state.refusal.code}</p>
          </Notice>
          <Button variant="outline" onClick={() => dispatch({ type: "back" })}>
            <ArrowLeft aria-hidden /> Change the picks
          </Button>
        </div>
      );
    case "plan":
      return (
        <div className="space-y-4">
          <PlanView plan={state.plan} />
          <div className="flex flex-wrap gap-2">
            {canConfirm(state.plan) ? (
              <Button onClick={() => dispatch({ type: "proceed" })}>Continue to confirm</Button>
            ) : null}
            <Button variant="outline" onClick={() => dispatch({ type: "back" })}>
              <ArrowLeft aria-hidden /> Change the picks
            </Button>
            {state.plan.readOnly ? (
              <Button variant="ghost" onClick={() => dispatch({ type: "again" })}>
                Check another colleague
              </Button>
            ) : null}
          </div>
        </div>
      );
    case "confirming":
      return <Confirm plan={state.plan} dispatch={dispatch} />;
    case "applying":
      return <Working text="Applying the plan: one change at a time…" />;
    case "result":
      return (
        <div className="space-y-4">
          <OutcomeView outcome={state.outcome} />
          <div className="flex flex-wrap gap-2">
            <Button onClick={() => dispatch({ type: "close" })}>Done</Button>
            <Button variant="outline" onClick={() => dispatch({ type: "again" })}>
              Run this card again
            </Button>
          </div>
        </div>
      );
  }
}

function Working({ text }: { text: string }) {
  return (
    <p className="flex items-center gap-2 text-muted-foreground" role="status">
      <LoaderCircle className="animate-spin" aria-hidden /> {text}
    </p>
  );
}

function SketchShapeNote() {
  return (
    <p className="flex items-center gap-2 text-xs text-muted-foreground">
      <Info className="size-3.5" aria-hidden /> Sketch: this plan's shape is made up; the window's mailbox write cards
      show the core's real plan in the same steps (task 082).
    </p>
  );
}

function PlanView({ plan }: { plan: SketchPlan }) {
  if (plan.readOnly) {
    return (
      <div className="space-y-2">
        <p className="font-medium">{plan.title}</p>
        <ul className="list-disc space-y-1 pl-5">
          {(plan.answer ?? []).map((line) => (
            <li key={line}>{line}</li>
          ))}
        </ul>
        <p className="text-xs text-muted-foreground">Direct grants only; nothing was changed.</p>
      </div>
    );
  }
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
          <ul className="space-y-1">
            {plan.changes.map((change) => (
              <li key={`${change.target}:${change.detail}`} className="flex items-start gap-2">
                <Badge variant={change.action === "add" ? "default" : "destructive"}>{change.action}</Badge>
                <span>
                  <strong>{change.target}</strong>: {change.detail}
                </span>
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
              <li key={line}>{line}</li>
            ))}
          </ul>
        </div>
      ) : null}
      {plan.warnings.map((warning) => (
        <Notice key={warning} tone="warning" icon={<TriangleAlert aria-hidden />} title="Good to know">
          {warning}
        </Notice>
      ))}
      <SketchShapeNote />
    </div>
  );
}

function Confirm({
  plan,
  dispatch,
}: {
  plan: SketchPlan;
  dispatch: (event: Parameters<typeof sketchReducer>[1]) => void;
}) {
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
        <Button disabled={!checked} onClick={() => dispatch({ type: "confirm" })}>
          Confirm and apply
        </Button>
        <Button variant="outline" onClick={() => dispatch({ type: "back" })}>
          <ArrowLeft aria-hidden /> Back to the plan
        </Button>
      </div>
      <p className="text-xs text-muted-foreground">
        Cancelling here changes nothing; the command line asks for the same confirmation with `--yes`.
      </p>
    </div>
  );
}

function OutcomeView({ outcome }: { outcome: SketchOutcome }) {
  const failed = outcome.items.filter((item) => !item.ok).length;
  return (
    <div className="space-y-3">
      {outcome.status === "done" ? (
        <Notice tone="success" icon={<CircleCheck aria-hidden />} title="Done">
          Every change was made.
        </Notice>
      ) : (
        <Notice tone="warning" icon={<TriangleAlert aria-hidden />} title="Partly done">
          {outcome.items.length - failed} of {outcome.items.length} change(s) were made; {failed} failed and can be run
          again.
        </Notice>
      )}
      <ul className="space-y-1">
        {outcome.items.map((item) => (
          <li key={`${item.target}:${item.detail}`} className="flex items-start gap-2">
            {item.ok ? (
              <CircleCheck className="mt-0.5 size-4 shrink-0" aria-label="done" />
            ) : (
              <CircleX className="mt-0.5 size-4 shrink-0 text-destructive" aria-label="failed" />
            )}
            <span>
              <strong>{item.target}</strong>: {item.detail}
              {item.message ? <span className="block text-sm text-muted-foreground">{item.message}</span> : null}
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function Notice({
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
