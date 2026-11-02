import { Radio } from "@base-ui/react/radio";
import { RadioGroup } from "@base-ui/react/radio-group";
import { Check, CircleX } from "lucide-react";
import { type KeyboardEvent, type ReactNode, useCallback, useRef, useState } from "react";
import { Button, buttonVariants } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { cn } from "@/lib/utils";
import { type BridgeError, MessageType, type Reply } from "../bridge/protocol";
import { ErrorText, Notice } from "./plan-view";

/**
 * The parts every card shares (tasks 120 and 121): a field with its visible label, the card's one live region, a
 * lookup's error under its form, the line that says what an answer answers, the lookup button that stays in the Tab
 * order while it waits, the either/or choice as a keyboard radio group, and the state of a lookup card.
 */

/** A field with its label above it: the label names the field for everyone, the placeholder stays an example. */
export function Field({
  id,
  label,
  className,
  children,
}: {
  id: string;
  label: string;
  className?: string;
  children: ReactNode;
}) {
  return (
    <div className={cn("grid gap-1.5", className)}>
      <Label htmlFor={id}>{label}</Label>
      {children}
    </div>
  );
}

/**
 * The card's announcements: one status region, always in the page and visually hidden, whose text the card sets on
 * each transition ("Looking up Finance…", "Finance: 4 direct members"); a screen reader reads a region that changes,
 * not one mounted with its text already in it. A refusal or an error stays an alert of its own.
 */
export function LiveStatus({ text }: { text: string }) {
  return (
    <p role="status" className="sr-only" data-testid="live-status">
      {text}
    </p>
  );
}

/** A failed lookup, under the form whose field it describes: the error, what to do and the code. */
export function LookupError({ id, title, error }: { id: string; title: string; error: BridgeError }) {
  return (
    <Notice id={id} tone="error" icon={<CircleX aria-hidden />} title={title}>
      <ErrorText error={error} />
    </Notice>
  );
}

/** The line over an answer that names what was asked, so a field changed since does not pass for the answer's. */
export function AnswerFor({ query }: { query: string }) {
  return <p className="text-xs text-muted-foreground" data-testid="answer-for">{`Answer for "${query}"`}</p>;
}

/**
 * A card's submit button: disabled while the card waits or a field is missing, but still in the Tab order, so the
 * focus does not fall out of the card and a screen reader hears why it does nothing (`missing`). It is as wide as the
 * longer of its two labels (task 126): a hidden copy with the working label sits in the same grid cell, so "Look up" →
 * "Looking up…" moves nothing beside it.
 */
export function SubmitButton({
  busy,
  missing,
  hintId,
  idle,
  working,
}: {
  busy: boolean;
  missing: string | null;
  hintId: string;
  idle: string;
  working: string;
}) {
  const hint = busy ? null : missing;
  return (
    <>
      <span className="inline-grid shrink-0 *:col-start-1 *:row-start-1">
        <Button
          type="submit"
          disabled={busy || missing !== null}
          focusableWhenDisabled
          aria-describedby={hint ? hintId : undefined}
        >
          {busy ? working : idle}
        </Button>
        <span aria-hidden className={cn(buttonVariants(), "invisible")} data-testid="button-sizer">
          {working}
        </span>
      </span>
      {hint ? (
        <span id={hintId} className="sr-only">
          {hint}
        </span>
      ) : null}
    </>
  );
}

/** Enter on a control of a form that Enter would toggle or ignore submits the form instead, as in its text fields. */
export function submitOnEnter(event: KeyboardEvent<HTMLElement>): void {
  if (event.key === "Enter") {
    event.preventDefault();
    event.currentTarget.closest("form")?.requestSubmit();
  }
}

/**
 * An either/or choice as a radio group (base-ui's): one Tab stop, the arrow keys move the choice, each option drawn
 * as a small button; Enter submits the form the group is in.
 */
export function Choice<T extends string>({
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
    <RadioGroup
      aria-label={label}
      value={value}
      onValueChange={(next) => onChange(next as T)}
      className="flex flex-wrap gap-2"
    >
      {options.map(([option, text]) => (
        <Radio.Root
          key={option}
          value={option}
          className={cn(
            buttonVariants({ size: "sm", variant: value === option ? "default" : "outline" }),
            "cursor-default",
          )}
          onKeyDown={submitOnEnter}
        >
          {value === option ? <Check aria-hidden data-icon="inline-start" /> : null}
          {text}
        </Radio.Root>
      ))}
    </RadioGroup>
  );
}

/** What a lookup card does with a reply that is no error: the answer to keep and the status line, or nothing. */
export type ReadReply<T> = (reply: Reply) => { result: T; status: string } | undefined;

/**
 * The state of a lookup card (tasks 120 and 121): one lookup at a time (a second submit while one runs is dropped),
 * the old answer kept, dimmed, while the new one is on its way, and dropped when the new one fails, whose error the
 * card shows under its form; the status line follows each step.
 */
export function useLookup<T>() {
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<T | null>(null);
  const [error, setError] = useState<BridgeError | null>(null);
  const [status, setStatus] = useState("");
  const running = useRef(false);

  const run = useCallback(async (announce: string, call: () => Promise<Reply>, read: ReadReply<T>): Promise<void> => {
    if (running.current) {
      return;
    }
    running.current = true;
    setBusy(true);
    setError(null);
    setStatus(announce);
    try {
      const reply = await call();
      if (reply.type === MessageType.error) {
        setResult(null);
        setError(reply.data);
        setStatus("");
        return;
      }
      const answer = read(reply);
      if (answer) {
        setResult(answer.result);
      }
      setStatus(answer ? answer.status : "");
    } finally {
      running.current = false;
      setBusy(false);
    }
  }, []);

  return { busy, result, setResult, error, status, setStatus, run };
}
