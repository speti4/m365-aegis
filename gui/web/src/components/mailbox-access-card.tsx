import { type FormEvent, useCallback, useEffect, useId, useRef, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { focusTarget } from "@/lib/focus";
import { reveal } from "@/lib/scroll";
import { cn } from "@/lib/utils";
import type { BridgeClient } from "../bridge/client";
import {
  type ExchangeLayer,
  type MailboxAccess,
  type MailboxInfo,
  type MailboxRight,
  MessageType,
  type UserAccess,
} from "../bridge/protocol";
import { type AppliedWrites, namesUser, touches } from "./applied-writes";
import { AnswerFor, Choice, Field, LiveStatus, LookupError, SubmitButton, useLookup } from "./card-parts";
import { count } from "./plan-wording";
import { UserPicker } from "./user-picker";

export const rightNames: Record<MailboxRight, string> = {
  "full-access": "FullAccess",
  "send-as": "Send As",
  "send-on-behalf": "Send on Behalf",
};

/** The layers a grant can come from, as the card names them in its "Read through" column (task 063). */
export const layerNames: Record<ExchangeLayer, string> = {
  "admin-api": "Admin API",
  pwsh: "PowerShell",
};

/**
 * Which layer read what of a mailbox's access, as the card writes it under the mailbox (task 063): the Admin API's
 * mailbox read with the Send on Behalf delegates by name beside the session's FullAccess and Send As, or the session
 * alone; null when the reply names no layer.
 */
export function describeLayers(access: MailboxAccess): string | null {
  switch (access.mailboxLayer) {
    case "admin-api":
      return "The mailbox and its Send on Behalf delegates by name come from the Admin API, FullAccess and Send As from the PowerShell session.";
    case "pwsh":
      return "Everything comes from the PowerShell session: the Admin API did not answer for this mailbox, or the window has none.";
    default:
      return null;
  }
}

/** How long an answer took, as the card writes it under the table. */
export function describeElapsed(ms: number): string {
  return ms < 1000 ? `${Math.round(ms)} ms` : `${(ms / 1000).toFixed(1)} s`;
}

type Mode = "mailbox" | "user";

/** The writes that change what the card shows of a mailbox: its holders. */
const accessWrites: readonly string[] = [MessageType.mailboxAccessGrant, MessageType.mailboxAccessRevoke];

type Result =
  /** `refreshed` when the card read the mailbox again after a write card applied a change to it (task 091). */
  | { kind: "holders"; asked: string; data: MailboxAccess; ms: number; refreshed: boolean }
  /**
   * `stale` when a write card applied a grant or a revoke that names the user after the card read the grants (task
   * 103): the card marks the list and offers to read it again, which is the slow reverse lookup.
   */
  | { kind: "grants"; asked: string; data: UserAccess; ms: number; stale: boolean }
  | { kind: "shared"; data: MailboxInfo[]; ms: number };

/**
 * The window's first mailbox page (task 045): who holds FullAccess, Send As and Send on Behalf on a mailbox, or
 * which mailboxes a user holds them on, as `m365-aegis mailbox access` reads them, and the shared mailboxes to pick
 * one from. The user is picked from the suggestions (the directory, else the snapshot) as the colleague types a name (the user picker, task 112), or
 * typed in full. The window keeps one Exchange Online session: the first lookup starts it (a few seconds), the later
 * ones answer in the cmdlet's own time, which the line under the table shows. A mailbox's grants come over the
 * layers as on the command line (task 063): each row names the layer that read it. When the mailbox rights card applies a
 * grant or a revoke to the mailbox the card shows (`applied`, task 091), the card reads it again with one
 * `mailbox-access` request and says so; an apply to another mailbox, or a forwarding, sends nothing. A user's grants
 * are not read again on their own (the reverse lookup goes over every mailbox, task 043): when a grant or a revoke names
 * the user the card shows, the card marks the list as possibly out of date and offers "Read again" (task 103), which
 * sends one `mailbox-access` request for the user; an apply that names another user marks nothing.
 *
 * "A mailbox" or "A user" is a radio group (task 121), and the field under it is labeled by the choice. A failure
 * shows under the field and drops the old answer (task 120); the field stays usable while a lookup runs, a pick from
 * the shared mailboxes or "Read again" takes the focus back to it, and the card's status region says what is going on.
 */
export function MailboxAccessCard({ client, applied }: { client: BridgeClient; applied?: AppliedWrites }) {
  const id = useId();
  const field = useRef<HTMLInputElement>(null);
  const [mode, setMode] = useState<Mode>("mailbox");
  const [query, setQuery] = useState("");
  const [refreshing, setRefreshing] = useState(false);
  // Whether the window's Exchange Online session has answered once: the first lookup starts it.
  const [answered, setAnswered] = useState(false);
  // A pick or "Read again" removes the button that had the focus: the field takes it once the card has drawn.
  const focusField = useRef(false);
  const { busy, result, setResult, error, status, setStatus, run: runLookup } = useLookup<Result>();
  const cardRef = useRef<HTMLElement>(null);
  useEffect(() => {
    if (focusField.current) {
      focusField.current = false;
      field.current?.focus();
    }
  });

  const run = useCallback(
    async (
      type: string,
      data: Record<string, string | boolean>,
      announce: string,
      refreshed = false,
    ): Promise<boolean> => {
      setRefreshing(refreshed);
      const started = performance.now();
      const asked = String(data.mailbox ?? data.user ?? "");
      // Whether the answer is a mailbox's holders: a read again tells the write card so (task 124).
      let holders = false;
      try {
        await runLookup(
          announce,
          () => client.call(type, data),
          (reply) => {
            const ms = performance.now() - started;
            switch (reply.type) {
              case MessageType.mailboxHolders:
                setAnswered(true);
                holders = true;
                return {
                  result: { kind: "holders", asked, data: reply.data, ms, refreshed },
                  status: `${reply.data.mailbox.displayName}: ${count(reply.data.holders.length, "grant")}`,
                };
              case MessageType.userGrants:
                setAnswered(true);
                return {
                  result: { kind: "grants", asked, data: reply.data, ms, stale: false },
                  status: `${reply.data.displayName ?? reply.data.user}: ${count(reply.data.grants.length, "grant")} over ${count(reply.data.mailboxesScanned, "mailbox", "mailboxes")}`,
                };
              case MessageType.mailboxes:
                setAnswered(true);
                return {
                  result: { kind: "shared", data: reply.data.mailboxes, ms },
                  status: count(reply.data.mailboxes.length, "shared mailbox", "shared mailboxes"),
                };
              default:
                return undefined;
            }
          },
        );
        return holders;
      } finally {
        setRefreshing(false);
      }
    },
    [client, runLookup],
  );

  /** What the status region says as a lookup starts: the first one also says that it starts the session. */
  const looking = (what: string) =>
    answered
      ? `Looking up ${what}…`
      : `Looking up ${what}… The first lookup starts the Exchange Online session, which takes a few seconds.`;

  // The mailbox on the card; a grant or a revoke applied to it reads it again (task 091), and the write card is told
  // once the new answer shows, so its result step can offer to show this card (task 124).
  const shown = result?.kind === "holders" ? result.data.mailbox : null;
  useEffect(() => {
    if (!applied || !shown) {
      return;
    }
    return applied.subscribe((write) => {
      if (accessWrites.includes(write.operation) && touches(write, shown)) {
        void run(
          MessageType.mailboxAccess,
          { mailbox: shown.primarySmtpAddress },
          "A change was applied to this mailbox: reading its access again…",
          true,
        ).then((read) => {
          if (read) {
            write.onReRead?.({
              card: "Mailbox access",
              mailbox: shown.displayName,
              show: () => reveal(cardRef.current),
            });
          }
        });
      }
    });
  }, [applied, shown, run]);

  // The user on the card; a grant or a revoke that names them marks the list, and reads nothing (task 103).
  const grants = result?.kind === "grants" ? result : null;
  useEffect(() => {
    if (!applied || !grants || grants.stale) {
      return;
    }
    return applied.subscribe((write) => {
      if (accessWrites.includes(write.operation) && namesUser(write, grants.data)) {
        setResult({ ...grants, stale: true });
        setStatus(staleText);
      }
    });
  }, [applied, grants, setResult, setStatus]);

  function lookUp(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const asked = query.trim();
    if (asked !== "") {
      void run(MessageType.mailboxAccess, { [mode]: asked }, looking(asked));
    }
  }

  function pick(mailbox: MailboxInfo) {
    setMode("mailbox");
    setQuery(mailbox.primarySmtpAddress);
    focusField.current = true;
    void run(MessageType.mailboxAccess, { mailbox: mailbox.primarySmtpAddress }, looking(mailbox.displayName));
  }

  function readAgain(user: string) {
    focusField.current = true;
    void run(MessageType.mailboxAccess, { user }, looking(user));
  }

  const fieldId = `${id}-field`;
  const errorId = `${id}-error`;
  return (
    <Card ref={cardRef} tabIndex={-1} aria-labelledby={`${id}-title`} className={cn("scroll-mt-20", focusTarget)}>
      <CardHeader>
        <CardTitle id={`${id}-title`}>Mailbox access</CardTitle>
        <CardDescription>
          Who can open a mailbox or send as it, or which mailboxes a user can: direct grants only, a group is listed as
          the group.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <LiveStatus text={status} />
        <div className="flex flex-wrap items-center gap-2">
          <Choice
            label="Look up by"
            value={mode}
            options={[
              ["mailbox", "A mailbox"],
              ["user", "A user"],
            ]}
            onChange={setMode}
          />
          <Button
            variant="ghost"
            size="sm"
            disabled={busy}
            focusableWhenDisabled
            onClick={() => void run(MessageType.mailboxList, { shared: true }, "Listing the shared mailboxes…")}
          >
            List the shared mailboxes
          </Button>
        </div>
        <form onSubmit={lookUp} aria-busy={busy}>
          <Field id={fieldId} label={mode === "mailbox" ? "Mailbox" : "User"}>
            <div className="flex items-start gap-2">
              {mode === "mailbox" ? (
                <Input
                  id={fieldId}
                  ref={field}
                  placeholder="Mailbox address, name or object id"
                  value={query}
                  onChange={(event) => setQuery(event.target.value)}
                  readOnly={busy}
                  aria-invalid={error ? true : undefined}
                  aria-describedby={error ? errorId : undefined}
                />
              ) : (
                <UserPicker
                  client={client}
                  id={fieldId}
                  inputRef={field}
                  value={query}
                  onChange={setQuery}
                  onPick={(user) => void run(MessageType.mailboxAccess, { user }, looking(user))}
                  busy={busy}
                  invalid={error !== null}
                  describedBy={error ? errorId : undefined}
                  onAnnounce={setStatus}
                />
              )}
              <SubmitButton
                busy={busy}
                missing={query.trim() === "" ? `Type a ${mode === "mailbox" ? "mailbox" : "user"} first.` : null}
                hintId={`${id}-hint`}
                idle="Look up"
                working="Looking up…"
              />
            </div>
          </Field>
        </form>
        {refreshing ? (
          <p className="text-sm text-muted-foreground">
            A change was applied to this mailbox: reading its access again…
          </p>
        ) : null}
        {busy && !answered ? (
          <p className="text-sm text-muted-foreground">
            The first lookup starts the Exchange Online session, which takes a few seconds.
          </p>
        ) : null}
        {error ? <LookupError id={errorId} title="The lookup failed" error={error} /> : null}
        {result ? (
          <div className={busy ? "opacity-50" : undefined} aria-busy={busy}>
            <ResultView result={result} busy={busy} onPick={pick} onReadAgain={readAgain} />
          </div>
        ) : null}
      </CardContent>
    </Card>
  );
}

/** The stale mark's sentence, on the card and in its status region. */
const staleText =
  "A change on the Mailbox rights card named this user since this list was read, so it may be out of date. Reading it again goes over every mailbox and takes a while.";

function ResultView({
  result,
  busy,
  onPick,
  onReadAgain,
}: {
  result: Result;
  busy: boolean;
  onPick: (mailbox: MailboxInfo) => void;
  onReadAgain: (user: string) => void;
}) {
  const elapsed = <p className="text-xs text-muted-foreground">answered in {describeElapsed(result.ms)}</p>;
  switch (result.kind) {
    case "shared":
      return (
        <div className="space-y-2">
          <p className="text-muted-foreground">{result.data.length} shared mailbox(es); pick one to see its access.</p>
          <ul className="flex flex-wrap gap-2">
            {result.data.map((mailbox) => (
              <li key={mailbox.primarySmtpAddress}>
                <Button variant="outline" size="sm" onClick={() => onPick(mailbox)}>
                  {mailbox.displayName}
                </Button>
              </li>
            ))}
          </ul>
          {elapsed}
        </div>
      );
    case "holders": {
      const { mailbox, holders } = result.data;
      const layers = describeLayers(result.data);
      return (
        <div className="space-y-2">
          <AnswerFor query={result.asked} />
          <p className="flex flex-wrap items-center gap-2">
            <strong>{mailbox.displayName}</strong>
            <Badge variant="secondary">{mailbox.type}</Badge>
            <span className="text-muted-foreground">{mailbox.primarySmtpAddress}</span>
            <span className="text-muted-foreground">· {holders.length} grant(s)</span>
          </p>
          {result.refreshed ? (
            <p className="text-xs text-muted-foreground" data-testid="refreshed">
              Read again after a change on the Mailbox rights card.
            </p>
          ) : null}
          {layers ? <p className="text-xs text-muted-foreground">{layers}</p> : null}
          {holders.length === 0 ? (
            <p className="text-muted-foreground">Nobody holds a right on this mailbox but its own account.</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Right</TableHead>
                  <TableHead>Holder</TableHead>
                  <TableHead>Address</TableHead>
                  <TableHead>Read through</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {holders.map((holder) => (
                  <TableRow key={`${holder.right}:${holder.holder}`}>
                    <TableCell>{rightNames[holder.right]}</TableCell>
                    <TableCell>{holder.holder}</TableCell>
                    <TableCell>{holder.address ?? ""}</TableCell>
                    <TableCell>
                      {holder.layer ? <Badge variant="outline">{layerNames[holder.layer]}</Badge> : null}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
          {elapsed}
        </div>
      );
    }
    case "grants": {
      const { user, displayName, grants, mailboxesScanned, failed } = result.data;
      return (
        <div className="space-y-2">
          <AnswerFor query={result.asked} />
          <p className="flex flex-wrap items-center gap-2">
            <strong>{displayName ?? user}</strong>
            {displayName ? <span className="text-muted-foreground">{user}</span> : null}
            <span className="text-muted-foreground">
              · {grants.length} grant(s) over {mailboxesScanned} mailbox(es)
            </span>
            {failed.length > 0 ? <Badge variant="warning">partial</Badge> : null}
          </p>
          {result.stale ? (
            <div
              className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-warning/50 p-3 text-sm"
              data-testid="stale"
            >
              <span>{staleText}</span>
              <Button
                size="sm"
                variant="outline"
                disabled={busy}
                focusableWhenDisabled
                onClick={() => onReadAgain(user)}
              >
                Read again
              </Button>
            </div>
          ) : null}
          {failed.length > 0 ? (
            <div className="rounded-lg border border-danger/50 p-3 text-sm">
              <p className="font-medium">
                {failed.length} list(s) could not be read; the grants below may be incomplete.
              </p>
              <ul className="list-disc pl-5 text-muted-foreground">
                {failed.map((line) => (
                  <li key={line}>{line}</li>
                ))}
              </ul>
            </div>
          ) : null}
          {grants.length === 0 ? (
            <p className="text-muted-foreground">The user holds no right on another mailbox.</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Mailbox</TableHead>
                  <TableHead>Address</TableHead>
                  <TableHead>Type</TableHead>
                  <TableHead>Right</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {grants.map((grant) => (
                  <TableRow key={`${grant.mailbox.primarySmtpAddress}:${grant.right}`}>
                    <TableCell>{grant.mailbox.displayName}</TableCell>
                    <TableCell>{grant.mailbox.primarySmtpAddress}</TableCell>
                    <TableCell>{grant.mailbox.type}</TableCell>
                    <TableCell>{rightNames[grant.right]}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
          {elapsed}
        </div>
      );
    }
  }
}
