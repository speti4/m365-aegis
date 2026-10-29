import { type FormEvent, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { BridgeClient } from "../bridge/client";
import {
  type BridgeError,
  type MailboxAccess,
  type MailboxInfo,
  type MailboxRight,
  MessageType,
  type UserAccess,
} from "../bridge/protocol";

export const rightNames: Record<MailboxRight, string> = {
  "full-access": "FullAccess",
  "send-as": "Send As",
  "send-on-behalf": "Send on Behalf",
};

/** How long an answer took, as the card writes it under the table. */
export function describeElapsed(ms: number): string {
  return ms < 1000 ? `${Math.round(ms)} ms` : `${(ms / 1000).toFixed(1)} s`;
}

type Mode = "mailbox" | "user";

type Result =
  | { kind: "holders"; data: MailboxAccess; ms: number }
  | { kind: "grants"; data: UserAccess; ms: number }
  | { kind: "shared"; data: MailboxInfo[]; ms: number };

/**
 * The window's first mailbox page (task 045): who holds FullAccess, Send As and Send on Behalf on a mailbox, or
 * which mailboxes a user holds them on, as `m365-aegis mailbox access` reads them, and the shared mailboxes to pick
 * one from. The window keeps one Exchange Online session: the first lookup starts it (a few seconds), the later
 * ones answer in the cmdlet's own time, which the line under the table shows.
 */
export function MailboxAccessCard({
  client,
  onError,
}: {
  client: BridgeClient;
  onError: (error: BridgeError) => void;
}) {
  const [mode, setMode] = useState<Mode>("mailbox");
  const [query, setQuery] = useState("");
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<Result | null>(null);

  async function run(type: string, data: unknown): Promise<void> {
    setBusy(true);
    const started = performance.now();
    try {
      const reply = await client.call(type, data);
      const ms = performance.now() - started;
      switch (reply.type) {
        case MessageType.mailboxHolders:
          setResult({ kind: "holders", data: reply.data, ms });
          break;
        case MessageType.userGrants:
          setResult({ kind: "grants", data: reply.data, ms });
          break;
        case MessageType.mailboxes:
          setResult({ kind: "shared", data: reply.data.mailboxes, ms });
          break;
        case MessageType.error:
          onError(reply.data);
          break;
      }
    } finally {
      setBusy(false);
    }
  }

  function lookUp(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    void run(MessageType.mailboxAccess, { [mode]: query.trim() });
  }

  function pick(mailbox: MailboxInfo) {
    setMode("mailbox");
    setQuery(mailbox.primarySmtpAddress);
    void run(MessageType.mailboxAccess, { mailbox: mailbox.primarySmtpAddress });
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Mailbox access</CardTitle>
        <CardDescription>
          Who can open a mailbox or send as it, or which mailboxes a user can, as `m365-aegis mailbox access` reads
          them: direct grants only, a group is listed as the group.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="flex flex-wrap gap-2" role="radiogroup" aria-label="Look up by">
          <Button
            variant={mode === "mailbox" ? "default" : "outline"}
            size="sm"
            role="radio"
            aria-checked={mode === "mailbox"}
            onClick={() => setMode("mailbox")}
          >
            A mailbox
          </Button>
          <Button
            variant={mode === "user" ? "default" : "outline"}
            size="sm"
            role="radio"
            aria-checked={mode === "user"}
            onClick={() => setMode("user")}
          >
            A user
          </Button>
          <Button
            variant="ghost"
            size="sm"
            disabled={busy}
            onClick={() => void run(MessageType.mailboxList, { shared: true })}
          >
            List the shared mailboxes
          </Button>
        </div>
        <form className="flex gap-2" onSubmit={lookUp}>
          <Input
            aria-label={mode === "mailbox" ? "Mailbox" : "User"}
            placeholder={mode === "mailbox" ? "Mailbox address, name or object id" : "User principal name"}
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            disabled={busy}
          />
          <Button type="submit" disabled={busy || query.trim() === ""}>
            {busy ? "Looking up…" : "Look up"}
          </Button>
        </form>
        {busy && result === null ? (
          <p className="text-sm text-muted-foreground">
            The first lookup starts the Exchange Online session, which takes a few seconds.
          </p>
        ) : null}
        {result ? <ResultView result={result} onPick={pick} /> : null}
      </CardContent>
    </Card>
  );
}

function ResultView({ result, onPick }: { result: Result; onPick: (mailbox: MailboxInfo) => void }) {
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
      return (
        <div className="space-y-2">
          <p className="flex flex-wrap items-center gap-2">
            <strong>{mailbox.displayName}</strong>
            <Badge variant="secondary">{mailbox.type}</Badge>
            <span className="text-muted-foreground">{mailbox.primarySmtpAddress}</span>
            <span className="text-muted-foreground">· {holders.length} grant(s)</span>
          </p>
          {holders.length === 0 ? (
            <p className="text-muted-foreground">Nobody holds a right on this mailbox but its own account.</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Right</TableHead>
                  <TableHead>Holder</TableHead>
                  <TableHead>Address</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {holders.map((holder) => (
                  <TableRow key={`${holder.right}:${holder.holder}`}>
                    <TableCell>{rightNames[holder.right]}</TableCell>
                    <TableCell>{holder.holder}</TableCell>
                    <TableCell>{holder.address ?? ""}</TableCell>
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
          <p className="flex flex-wrap items-center gap-2">
            <strong>{displayName ?? user}</strong>
            {displayName ? <span className="text-muted-foreground">{user}</span> : null}
            <span className="text-muted-foreground">
              · {grants.length} grant(s) over {mailboxesScanned} mailbox(es)
            </span>
            {failed.length > 0 ? <Badge variant="destructive">partial</Badge> : null}
          </p>
          {failed.length > 0 ? (
            <div className="rounded-lg border border-destructive/50 p-3 text-sm">
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
