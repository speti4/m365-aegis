import { ArrowLeft, Check, CircleX, LogIn, TriangleAlert, Unplug } from "lucide-react";
import {
  type FormEvent,
  type KeyboardEvent,
  type ReactNode,
  type RefObject,
  useEffect,
  useId,
  useReducer,
  useRef,
  useState,
} from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import type { BridgeClient } from "../bridge/client";
import {
  type BridgeError,
  type MailboxAccessWrite,
  type MailboxForwardingWrite,
  type MailboxRight,
  MessageType,
  type PlanReady,
} from "../bridge/protocol";
import { type AppliedWrite, detailAddresses, type ReRead, targetAddresses } from "./applied-writes";
import { Choice, Field, LiveStatus, submitOnEnter } from "./card-parts";
import { rightNames } from "./mailbox-access-card";
import { ConfirmView, ErrorText, Notice, PlanView, ResultView, type RunAs, Stepper, Working } from "./plan-view";
import { canConfirm, initialWriteState, type WriteState, writeReducer, writeStatus } from "./write-flow";

/** A write the card's picks ask for: the request's type and payload; null while a pick is missing. */
export interface WriteRequest {
  type: string;
  data: MailboxAccessWrite | MailboxForwardingWrite;
}

/** The codes of an apply's error that mean nothing was sent: the plan was not held, or not confirmed. */
const nothingSent: readonly string[] = ["usage", "refused-without-yes"];

/** The users a text names: separated by commas, semicolons or spaces. */
export function splitList(text: string): string[] {
  return text
    .split(/[,;\s]+/)
    .map((item) => item.trim())
    .filter((item) => item !== "");
}

/** The picks still missing before the card can plan, as the line beside "See the plan" lists them. */
export function describeMissing(missing: readonly string[]): string {
  return `Still needed: ${missing.join(", ")}.`;
}

/**
 * A write card (task 082): the picks, then the plan the host computed (`--dry-run`'s, nothing sent), the confirmation
 * (the `apply-plan` with `confirm: true`, which stands for `--yes`) and the result of each change, as the task card
 * sketch of task 018 drew it. Going back before the confirmation sends nothing. Signed out, the card waits for a
 * sign-in and an open plan goes back to the picks; signed in without an Exchange Online token (`exchangeError`, task
 * 124) it says so and waits too. An apply that may have changed the mailbox (a `plan-applied`, or an apply that broke
 * off on the way) is told to `onApplied` (task 091), so the access card can read the mailbox again; when it did, the
 * result step says so and "Show it" scrolls to that card (task 124).
 *
 * The keyboard (tasks 120 and 121): the picks are a form, so Enter in a field means "See the plan" (it only computes a
 * plan); Escape goes back a step from the plan, the confirmation or a refusal, never while an apply runs. "See the
 * plan" stays in the Tab order while a pick is missing, and the line beside it says which (`missing`). On every step
 * the focus moves to the step's heading or first control, `pickTarget` (the mailbox field) on the picks, and the
 * card's status region says what the step shows.
 */
export function WriteCard({
  title,
  description,
  client,
  signedIn,
  exchangeError,
  runAs,
  request,
  missing,
  picks,
  pickTarget,
  onApplied,
}: {
  title: string;
  description: string;
  client: BridgeClient;
  signedIn: boolean;
  exchangeError?: string | null;
  runAs?: RunAs;
  request: WriteRequest | null;
  missing: readonly string[];
  picks: ReactNode;
  pickTarget: RefObject<HTMLInputElement | null>;
  onApplied?: (write: AppliedWrite) => void;
}) {
  const id = useId();
  const [state, dispatch] = useReducer(writeReducer, initialWriteState);
  // The write the card told last, and a card's word that it read the mailbox again after it (task 124).
  const told = useRef<AppliedWrite | null>(null);
  const [reRead, setReRead] = useState<{ write: AppliedWrite; reRead: ReRead } | null>(null);
  const canWrite = signedIn && !exchangeError;
  // The mailbox and the users of the request the plan on the card answers: the ones an apply touches.
  const plannedMailbox = useRef("");
  const plannedUsers = useRef<string[]>([]);
  // The element the step shows first (its heading, the result, the refusal), and the step the focus was moved for.
  const stepTarget = useRef<HTMLElement>(null);
  const focusedStep = useRef(state.step);
  // A plan was read for the account signed in then; signed out, the card starts over.
  useEffect(() => {
    if (!signedIn) {
      dispatch({ type: "reset" });
    }
  }, [signedIn]);
  // A step that replaces the focused button takes the focus to its own first element; the first render and a reset
  // by a sign-out move nothing.
  useEffect(() => {
    if (focusedStep.current === state.step) {
      return;
    }
    focusedStep.current = state.step;
    if (signedIn) {
      (state.step === "pick" ? pickTarget.current : stepTarget.current)?.focus();
    }
  }, [state.step, signedIn, pickTarget]);

  async function seePlan(asked: WriteRequest): Promise<void> {
    plannedMailbox.current = asked.data.mailbox;
    plannedUsers.current = "users" in asked.data ? asked.data.users : [];
    dispatch({ type: "request-plan" });
    dispatch({ type: "reply", reply: await client.call(asked.type, asked.data) });
  }

  async function apply(ready: PlanReady): Promise<void> {
    dispatch({ type: "confirm" });
    const reply = await client.call(MessageType.applyPlan, { planId: ready.planId, confirm: true });
    dispatch({ type: "reply", reply });
    const mayHaveChanged =
      reply.type === MessageType.planApplied ||
      (reply.type === MessageType.error && !nothingSent.includes(reply.data.code));
    told.current = null;
    if (mayHaveChanged) {
      const plan = reply.type === MessageType.planApplied ? reply.data.plan : ready.plan;
      const write: AppliedWrite = {
        operation: plan.operation,
        mailbox: plannedMailbox.current,
        addresses: targetAddresses(plan),
        users: [...new Set([...plannedUsers.current, ...detailAddresses(plan)])],
        onReRead: (answer) => setReRead({ write, reRead: answer }),
      };
      told.current = write;
      onApplied?.(write);
    }
  }

  function goBackOnEscape(event: KeyboardEvent<HTMLDivElement>) {
    // The reducer goes back from the plan, the confirmation and a refusal only; an apply under way stays.
    if (event.key === "Escape") {
      dispatch({ type: "back" });
    }
  }

  const needed = [
    ...(signedIn ? [] : ["a sign-in on the Sign-in card"]),
    ...(signedIn && exchangeError ? ["an Exchange Online token"] : []),
    ...missing,
  ];
  return (
    <Card aria-labelledby={`${id}-title`}>
      <CardHeader>
        <CardTitle id={`${id}-title`}>{title}</CardTitle>
        <CardDescription>{description}</CardDescription>
      </CardHeader>
      {/* Escape is a shortcut of the step's own "Back" buttons, which the keyboard reaches too. */}
      <CardContent className="space-y-5" onKeyDown={goBackOnEscape}>
        <LiveStatus text={writeStatus(state)} />
        <Stepper state={state} />
        <Body
          state={state}
          signedIn={signedIn}
          exchangeError={exchangeError ?? null}
          runAs={runAs}
          picks={picks}
          hint={request && canWrite ? null : describeMissing(needed)}
          hintId={`${id}-hint`}
          stepTarget={stepTarget}
          onSeePlan={request && canWrite ? () => void seePlan(request) : undefined}
          onApply={(ready) => void apply(ready)}
          reRead={reRead && reRead.write === told.current ? reRead.reRead : null}
          dispatch={dispatch}
        />
      </CardContent>
    </Card>
  );
}

function Body({
  state,
  signedIn,
  exchangeError,
  runAs,
  picks,
  hint,
  hintId,
  stepTarget,
  onSeePlan,
  onApply,
  reRead,
  dispatch,
}: {
  state: WriteState;
  signedIn: boolean;
  exchangeError: string | null;
  runAs: RunAs | undefined;
  picks: ReactNode;
  hint: string | null;
  hintId: string;
  stepTarget: RefObject<HTMLElement | null>;
  onSeePlan: (() => void) | undefined;
  onApply: (ready: PlanReady) => void;
  reRead: ReRead | null;
  dispatch: (event: Parameters<typeof writeReducer>[1]) => void;
}) {
  // Each step's first element takes the ref as the element type it is.
  const target = stepTarget as RefObject<never>;
  switch (state.step) {
    case "pick":
      return (
        <form
          className="space-y-4"
          onSubmit={(event: FormEvent<HTMLFormElement>) => {
            event.preventDefault();
            onSeePlan?.();
          }}
        >
          {signedIn ? null : (
            <Notice tone="warning" icon={<LogIn aria-hidden />} title="Signed out">
              This card needs a sign-in: sign in on the sign-in card, then come back here.
            </Notice>
          )}
          {signedIn && exchangeError ? (
            <Notice tone="warning" icon={<Unplug aria-hidden />} title="Exchange Online unavailable">
              <p>The account is signed in, but the window has no Exchange Online token: {exchangeError}.</p>
              <p className="mt-1 text-muted-foreground">
                What to do: an administrator consents to the Exchange Online permission, then Refresh on the sign-in
                card.
              </p>
            </Notice>
          ) : null}
          {picks}
          <div className="flex flex-wrap items-center gap-3">
            <Button
              type="submit"
              disabled={onSeePlan === undefined}
              focusableWhenDisabled
              aria-describedby={hint ? hintId : undefined}
            >
              See the plan
            </Button>
            {hint ? (
              <p id={hintId} className="text-sm text-muted-foreground">
                {hint}
              </p>
            ) : null}
          </div>
        </form>
      );
    case "planning":
      return <Working ref={target} text="The window is computing the plan; nothing is changed yet…" />;
    case "refused":
      return (
        <Refused
          error={state.error}
          applying={state.applying}
          noticeRef={target}
          onBack={() => dispatch({ type: "back" })}
        />
      );
    case "plan":
      return (
        <div className="space-y-4">
          <PlanView plan={state.ready.plan} titleRef={target} />
          <div className="flex flex-wrap gap-2">
            {canConfirm(state.ready) ? (
              <Button onClick={() => dispatch({ type: "proceed" })}>Continue to confirm</Button>
            ) : null}
            <Button variant="outline" onClick={() => dispatch({ type: "back" })}>
              <ArrowLeft aria-hidden /> Change the picks
            </Button>
          </div>
        </div>
      );
    case "confirming":
      return (
        <ConfirmView
          plan={state.ready.plan}
          runAs={runAs}
          leadRef={target}
          onConfirm={() => onApply(state.ready)}
          onBack={() => dispatch({ type: "back" })}
          onCancel={() => dispatch({ type: "reset" })}
        />
      );
    case "applying":
      return <Working ref={target} text="Applying the plan: one change at a time…" />;
    case "result":
      return (
        <div className="space-y-4">
          <ResultView applied={state.applied} noticeRef={target} />
          {reRead ? (
            <p className="flex flex-wrap items-center gap-x-1 text-sm" role="status" data-testid="re-read">
              The {reRead.card} card read <strong>{reRead.mailbox}</strong> again after this change.
              <Button variant="link" size="sm" className="h-auto px-1" onClick={reRead.show}>
                Show it
              </Button>
            </p>
          ) : null}
          <Button onClick={() => dispatch({ type: "reset" })}>Start a new change</Button>
        </div>
      );
  }
}

/**
 * Why the host would not plan or apply: a plan it could not compute, or an apply it refused (nothing sent); an apply
 * that broke off for any other reason (an internal error, no answer in time) may have sent some changes.
 */
function Refused({
  error,
  applying,
  noticeRef,
  onBack,
}: {
  error: BridgeError;
  applying: boolean;
  noticeRef: RefObject<never>;
  onBack: () => void;
}) {
  const unknown = applying && !nothingSent.includes(error.code);
  return (
    <div className="space-y-4">
      <Notice
        ref={noticeRef}
        tone="error"
        icon={unknown ? <TriangleAlert aria-hidden /> : <CircleX aria-hidden />}
        title={unknown ? "Stopped: check the mailbox before you try again" : "Refused: nothing was changed"}
      >
        <ErrorText error={error} />
      </Notice>
      <Button variant="outline" onClick={onBack}>
        <ArrowLeft aria-hidden /> Change the picks
      </Button>
    </div>
  );
}

const rights: readonly MailboxRight[] = ["full-access", "send-as", "send-on-behalf"];

/**
 * Grant or revoke FullAccess, Send As or Send on Behalf on a mailbox, as `m365-aegis mailbox access grant|revoke`
 * does (task 082); a FullAccess grant can go without automapping (`--no-automapping`).
 */
export function MailboxAccessWriteCard({
  client,
  signedIn,
  exchangeError,
  runAs,
  onApplied,
}: {
  client: BridgeClient;
  signedIn: boolean;
  exchangeError?: string | null;
  runAs?: RunAs;
  onApplied?: (write: AppliedWrite) => void;
}) {
  const id = useId();
  const mailboxField = useRef<HTMLInputElement>(null);
  const [mode, setMode] = useState<"grant" | "revoke">("grant");
  const [mailbox, setMailbox] = useState("");
  const [users, setUsers] = useState("");
  const [chosen, setChosen] = useState<MailboxRight[]>(["full-access"]);
  const [noAutoMapping, setNoAutoMapping] = useState(false);
  const grant = mode === "grant";
  const named = splitList(users);
  const automapping = grant && chosen.includes("full-access");
  const missing = [
    ...(mailbox.trim() === "" ? ["the mailbox"] : []),
    ...(named.length === 0 ? ["at least one user"] : []),
    ...(chosen.length === 0 ? ["at least one right"] : []),
  ];
  const request: WriteRequest | null =
    missing.length === 0
      ? {
          type: grant ? MessageType.mailboxAccessGrant : MessageType.mailboxAccessRevoke,
          data: { mailbox: mailbox.trim(), users: named, rights: chosen, noAutoMapping: automapping && noAutoMapping },
        }
      : null;
  const picks = (
    <div className="space-y-4">
      <Choice
        label="Grant or revoke"
        value={mode}
        options={[
          ["grant", "Grant"],
          ["revoke", "Revoke"],
        ]}
        onChange={setMode}
      />
      <Field id={`${id}-mailbox`} label="Mailbox">
        <Input
          id={`${id}-mailbox`}
          ref={mailboxField}
          placeholder="Mailbox address, name or object id"
          value={mailbox}
          onChange={(event) => setMailbox(event.target.value)}
        />
      </Field>
      <Field id={`${id}-users`} label="Users">
        <Input
          id={`${id}-users`}
          placeholder="User principal names or addresses, separated by commas"
          value={users}
          onChange={(event) => setUsers(event.target.value)}
        />
      </Field>
      <fieldset className="space-y-1.5">
        <legend className="mb-1.5 text-sm font-medium">Rights</legend>
        <div className="flex flex-wrap gap-2">
          {rights.map((right) => {
            const on = chosen.includes(right);
            return (
              <Button
                key={right}
                size="sm"
                variant={on ? "default" : "outline"}
                role="checkbox"
                aria-checked={on}
                onKeyDown={submitOnEnter}
                onClick={() => setChosen(on ? chosen.filter((item) => item !== right) : [...chosen, right])}
              >
                {on ? <Check aria-hidden data-icon="inline-start" /> : null}
                {rightNames[right]}
              </Button>
            );
          })}
        </div>
      </fieldset>
      <label className={automapping ? "flex items-center gap-2" : "flex items-center gap-2 text-muted-foreground"}>
        <input
          type="checkbox"
          className="size-4 accent-primary"
          checked={automapping && noAutoMapping}
          disabled={!automapping}
          onChange={(event) => setNoAutoMapping(event.target.checked)}
        />
        Without automapping: Outlook does not add the mailbox for them on its own (a FullAccess grant only)
      </label>
    </div>
  );
  return (
    <WriteCard
      title="Mailbox rights"
      description="Grant or revoke FullAccess, Send As or Send on Behalf on a mailbox: the plan first, then your confirmation."
      client={client}
      signedIn={signedIn}
      exchangeError={exchangeError}
      runAs={runAs}
      request={request}
      missing={missing}
      picks={picks}
      pickTarget={mailboxField}
      onApplied={onApplied}
    />
  );
}

/**
 * Forward a mailbox's mail to an address, or stop it, as `m365-aegis mailbox forwarding set|clear` does (task 082);
 * a forwarding keeps a copy in the mailbox unless the card is told not to (`--keep-copy`).
 */
export function MailboxForwardingCard({
  client,
  signedIn,
  exchangeError,
  runAs,
  onApplied,
}: {
  client: BridgeClient;
  signedIn: boolean;
  exchangeError?: string | null;
  runAs?: RunAs;
  onApplied?: (write: AppliedWrite) => void;
}) {
  const id = useId();
  const mailboxField = useRef<HTMLInputElement>(null);
  const [mode, setMode] = useState<"set" | "clear">("set");
  const [mailbox, setMailbox] = useState("");
  const [to, setTo] = useState("");
  const [keepCopy, setKeepCopy] = useState(true);
  const set = mode === "set";
  const missing = [
    ...(mailbox.trim() === "" ? ["the mailbox"] : []),
    ...(set && to.trim() === "" ? ["the address to forward to"] : []),
  ];
  const request: WriteRequest | null =
    missing.length === 0
      ? set
        ? { type: MessageType.mailboxForwardingSet, data: { mailbox: mailbox.trim(), to: to.trim(), keepCopy } }
        : { type: MessageType.mailboxForwardingClear, data: { mailbox: mailbox.trim() } }
      : null;
  const picks = (
    <div className="space-y-4">
      <Choice
        label="Set or clear"
        value={mode}
        options={[
          ["set", "Forward"],
          ["clear", "Stop forwarding"],
        ]}
        onChange={setMode}
      />
      <Field id={`${id}-mailbox`} label="Mailbox">
        <Input
          id={`${id}-mailbox`}
          ref={mailboxField}
          placeholder="Mailbox address, name or object id"
          value={mailbox}
          onChange={(event) => setMailbox(event.target.value)}
        />
      </Field>
      {set ? (
        <>
          <Field id={`${id}-to`} label="Forward to">
            <Input
              id={`${id}-to`}
              placeholder="The address to forward to"
              value={to}
              onChange={(event) => setTo(event.target.value)}
            />
          </Field>
          <label className="flex items-center gap-2">
            <input
              type="checkbox"
              className="size-4 accent-primary"
              checked={keepCopy}
              onChange={(event) => setKeepCopy(event.target.checked)}
            />
            Keep a copy in the mailbox
          </label>
        </>
      ) : null}
    </div>
  );
  return (
    <WriteCard
      title="Mailbox forwarding"
      description="Forward a mailbox's mail to an address, or stop it: the plan first, then your confirmation."
      client={client}
      signedIn={signedIn}
      exchangeError={exchangeError}
      runAs={runAs}
      request={request}
      missing={missing}
      picks={picks}
      pickTarget={mailboxField}
      onApplied={onApplied}
    />
  );
}
