import { ArrowLeft, CircleX, LogIn, TriangleAlert } from "lucide-react";
import { type ReactNode, useEffect, useReducer, useRef, useState } from "react";
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
import { type AppliedWrite, targetAddresses } from "./applied-writes";
import { rightNames } from "./mailbox-access-card";
import { ConfirmView, ErrorText, Notice, PlanView, ResultView, Stepper, Working } from "./plan-view";
import { canConfirm, initialWriteState, type WriteState, writeReducer } from "./write-flow";

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

/**
 * A write card (task 082): the picks, then the plan the host computed (`--dry-run`'s, nothing sent), the confirmation
 * (the `apply-plan` with `confirm: true`, which stands for `--yes`) and the result of each change, as the task card
 * sketch of task 018 drew it. Going back before the confirmation sends nothing. Signed out, the card waits for a
 * sign-in and an open plan goes back to the picks. An apply that may have changed the mailbox (a `plan-applied`, or an
 * apply that broke off on the way) is told to `onApplied` (task 091), so the access card can read the mailbox again.
 */
export function WriteCard({
  title,
  description,
  client,
  signedIn,
  request,
  picks,
  onApplied,
}: {
  title: string;
  description: string;
  client: BridgeClient;
  signedIn: boolean;
  request: WriteRequest | null;
  picks: ReactNode;
  onApplied?: (write: AppliedWrite) => void;
}) {
  const [state, dispatch] = useReducer(writeReducer, initialWriteState);
  // The mailbox of the request the plan on the card answers: the one an apply touches.
  const plannedMailbox = useRef("");
  // A plan was read for the account signed in then; signed out, the card starts over.
  useEffect(() => {
    if (!signedIn) {
      dispatch({ type: "reset" });
    }
  }, [signedIn]);

  async function seePlan(asked: WriteRequest): Promise<void> {
    plannedMailbox.current = asked.data.mailbox;
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
    if (mayHaveChanged) {
      const plan = reply.type === MessageType.planApplied ? reply.data.plan : ready.plan;
      onApplied?.({ operation: plan.operation, mailbox: plannedMailbox.current, addresses: targetAddresses(plan) });
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>{title}</CardTitle>
        <CardDescription>{description}</CardDescription>
      </CardHeader>
      <CardContent className="space-y-5">
        <Stepper state={state} />
        <Body
          state={state}
          signedIn={signedIn}
          picks={picks}
          onSeePlan={request && signedIn ? () => void seePlan(request) : undefined}
          onApply={(ready) => void apply(ready)}
          dispatch={dispatch}
        />
      </CardContent>
    </Card>
  );
}

function Body({
  state,
  signedIn,
  picks,
  onSeePlan,
  onApply,
  dispatch,
}: {
  state: WriteState;
  signedIn: boolean;
  picks: ReactNode;
  onSeePlan: (() => void) | undefined;
  onApply: (ready: PlanReady) => void;
  dispatch: (event: Parameters<typeof writeReducer>[1]) => void;
}) {
  switch (state.step) {
    case "pick":
      return (
        <div className="space-y-4">
          {signedIn ? null : (
            <Notice tone="warning" icon={<LogIn aria-hidden />} title="Signed out">
              This card needs a sign-in: sign in on the sign-in card, then come back here.
            </Notice>
          )}
          {picks}
          <Button disabled={onSeePlan === undefined} onClick={onSeePlan}>
            See the plan
          </Button>
        </div>
      );
    case "planning":
      return <Working text="The window is computing the plan; nothing is changed yet…" />;
    case "refused":
      return <Refused error={state.error} applying={state.applying} onBack={() => dispatch({ type: "back" })} />;
    case "plan":
      return (
        <div className="space-y-4">
          <PlanView plan={state.ready.plan} />
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
          onConfirm={() => onApply(state.ready)}
          onBack={() => dispatch({ type: "back" })}
        />
      );
    case "applying":
      return <Working text="Applying the plan: one change at a time…" />;
    case "result":
      return (
        <div className="space-y-4">
          <ResultView applied={state.applied} />
          <Button onClick={() => dispatch({ type: "reset" })}>Done</Button>
        </div>
      );
  }
}

/**
 * Why the host would not plan or apply: a plan it could not compute, or an apply it refused (nothing sent); an apply
 * that broke off for any other reason (an internal error, no answer in time) may have sent some changes.
 */
function Refused({ error, applying, onBack }: { error: BridgeError; applying: boolean; onBack: () => void }) {
  const unknown = applying && !nothingSent.includes(error.code);
  return (
    <div className="space-y-4">
      <Notice
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

/** A choice of two, as the sketch's buttons. */
function Choice<T extends string>({
  label,
  value,
  options,
  onChange,
}: {
  label: string;
  value: T;
  options: ReadonlyArray<readonly [T, string]>;
  onChange: (value: T) => void;
}) {
  return (
    <div className="flex flex-wrap gap-2" role="radiogroup" aria-label={label}>
      {options.map(([option, text]) => (
        <Button
          key={option}
          size="sm"
          variant={value === option ? "default" : "outline"}
          role="radio"
          aria-checked={value === option}
          onClick={() => onChange(option)}
        >
          {text}
        </Button>
      ))}
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
  onApplied,
}: {
  client: BridgeClient;
  signedIn: boolean;
  onApplied?: (write: AppliedWrite) => void;
}) {
  const [mode, setMode] = useState<"grant" | "revoke">("grant");
  const [mailbox, setMailbox] = useState("");
  const [users, setUsers] = useState("");
  const [chosen, setChosen] = useState<MailboxRight[]>(["full-access"]);
  const [noAutoMapping, setNoAutoMapping] = useState(false);
  const grant = mode === "grant";
  const named = splitList(users);
  const automapping = grant && chosen.includes("full-access");
  const request: WriteRequest | null =
    mailbox.trim() !== "" && named.length > 0 && chosen.length > 0
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
      <Input
        aria-label="Mailbox"
        placeholder="Mailbox address, name or object id"
        value={mailbox}
        onChange={(event) => setMailbox(event.target.value)}
      />
      <Input
        aria-label="Users"
        placeholder="User principal names or addresses, separated by commas"
        value={users}
        onChange={(event) => setUsers(event.target.value)}
      />
      <fieldset className="flex flex-wrap gap-2" aria-label="Rights">
        {rights.map((right) => {
          const on = chosen.includes(right);
          return (
            <Button
              key={right}
              size="sm"
              variant={on ? "default" : "outline"}
              role="checkbox"
              aria-checked={on}
              onClick={() => setChosen(on ? chosen.filter((item) => item !== right) : [...chosen, right])}
            >
              {rightNames[right]}
            </Button>
          );
        })}
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
      description="Grant or revoke FullAccess, Send As or Send on Behalf on a mailbox, as `m365-aegis mailbox access grant|revoke` does: the plan first, then your confirmation."
      client={client}
      signedIn={signedIn}
      request={request}
      picks={picks}
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
  onApplied,
}: {
  client: BridgeClient;
  signedIn: boolean;
  onApplied?: (write: AppliedWrite) => void;
}) {
  const [mode, setMode] = useState<"set" | "clear">("set");
  const [mailbox, setMailbox] = useState("");
  const [to, setTo] = useState("");
  const [keepCopy, setKeepCopy] = useState(true);
  const set = mode === "set";
  const request: WriteRequest | null =
    mailbox.trim() !== "" && (!set || to.trim() !== "")
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
      <Input
        aria-label="Mailbox"
        placeholder="Mailbox address, name or object id"
        value={mailbox}
        onChange={(event) => setMailbox(event.target.value)}
      />
      {set ? (
        <>
          <Input
            aria-label="Forward to"
            placeholder="The address to forward to"
            value={to}
            onChange={(event) => setTo(event.target.value)}
          />
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
      description="Forward a mailbox's mail to an address, or stop it, as `m365-aegis mailbox forwarding set|clear` does: the plan first, then your confirmation."
      client={client}
      signedIn={signedIn}
      request={request}
      picks={picks}
      onApplied={onApplied}
    />
  );
}
