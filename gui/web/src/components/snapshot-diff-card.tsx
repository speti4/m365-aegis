import { History, TriangleAlert } from "lucide-react";
import { type FormEvent, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { BridgeClient } from "../bridge/client";
import {
  type BridgeError,
  MessageType,
  type SnapshotDiffEntry,
  type SnapshotDiffReport,
  type SnapshotDiffSide,
  type TooFewSnapshotsInfo,
} from "../bridge/protocol";
import { rightNames } from "./mailbox-access-card";
import { Notice } from "./plan-view";
import { diffPart, windowWarning } from "./plan-wording";
import { describeAge, describeTaken } from "./user-access-card";

/** How a row holds what it holds, a mailbox right in the window's words. */
const howShown = (entry: SnapshotDiffEntry) => (rightNames as Record<string, string>)[entry.how] ?? entry.how;

/** A sentence from the core's lower-case one. */
const sentence = (text: string) => `${text.charAt(0).toUpperCase()}${text.slice(1)}.`;

type Result = { kind: "report"; data: SnapshotDiffReport } | { kind: "too-few"; data: TooFewSnapshotsInfo };

/**
 * Who gained or lost access between two snapshots (task 115), as `snapshot diff` answers it (task 098): the newest
 * snapshot and the one before it unless the colleague names them (a name, `latest`, `yesterday` or a date), every
 * membership, owner, shared file, mailbox right, calendar permission and forwarding that appeared or disappeared, by
 * part of the tenant; with a user, that user's changes only, nested memberships and what comes through a group
 * included (task 114). The host reads the snapshot files on this computer only: no sign-in, no request to Microsoft
 * 365. With fewer than two snapshots the card says so plainly; an `error` reply is the page's toast.
 */
export function SnapshotDiffCard({ client, onError }: { client: BridgeClient; onError: (error: BridgeError) => void }) {
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [user, setUser] = useState("");
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<Result | null>(null);

  async function compare(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    try {
      const picks = Object.entries({ from, to, user })
        .map(([name, value]) => [name, value.trim()] as const)
        .filter(([, value]) => value !== "");
      const reply = await client.call(MessageType.snapshotDiff, Object.fromEntries(picks));
      switch (reply.type) {
        case MessageType.snapshotDiffReport:
          setResult({ kind: "report", data: reply.data });
          break;
        case MessageType.tooFewSnapshots:
          setResult({ kind: "too-few", data: reply.data });
          break;
        case MessageType.error:
          onError(reply.data);
          break;
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Access changes</CardTitle>
        <CardDescription>
          Who gained or lost access between two snapshots of the tenant on this computer: the newest and the one before
          it, unless you name them. A user narrows it to that person, groups within groups included. Nothing is asked of
          Microsoft 365.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <form className="grid gap-2 sm:grid-cols-[1fr_1fr_1.5fr_auto]" onSubmit={(event) => void compare(event)}>
          <Input
            aria-label="From"
            placeholder="From: the one before"
            value={from}
            onChange={(event) => setFrom(event.target.value)}
            disabled={busy}
          />
          <Input
            aria-label="To"
            placeholder="To: the newest"
            value={to}
            onChange={(event) => setTo(event.target.value)}
            disabled={busy}
          />
          <Input
            aria-label="Only this user"
            placeholder="Only this user (optional)"
            value={user}
            onChange={(event) => setUser(event.target.value)}
            disabled={busy}
          />
          <Button type="submit" disabled={busy}>
            {busy ? "Comparing…" : "Compare"}
          </Button>
        </form>
        {result?.kind === "too-few" ? <TooFewSnapshotsView info={result.data} /> : null}
        {result?.kind === "report" ? <SnapshotDiffView report={result.data} /> : null}
      </CardContent>
    </Card>
  );
}

/** The card without two snapshots to compare: a plain state, not an error. */
export function TooFewSnapshotsView({ info }: { info: TooFewSnapshotsInfo }) {
  return (
    <Notice tone="info" icon={<History aria-hidden />} title="Nothing to compare yet">
      <p>{sentence(info.message)}</p>
      <p className="text-muted-foreground">{info.hint}</p>
    </Notice>
  );
}

/** One snapshot as the card names it: its time and its age. */
const side = (snapshot: SnapshotDiffSide) => `${describeTaken(snapshot.taken)} (${describeAge(snapshot.ageHours)})`;

/**
 * The changes between two snapshots: the two snapshots, whose changes when a user was named, the warnings in the
 * window's words, a table per part of the tenant that changed, and the parts without a change or left out.
 */
export function SnapshotDiffView({ report }: { report: SnapshotDiffReport }) {
  const { user, domains, changes } = report;
  const changed = domains.filter((domain) => domain.compared && domain.added + domain.removed > 0);
  const unchanged = domains.filter((domain) => domain.compared && domain.added + domain.removed === 0);
  const leftOut = domains.filter((domain) => !domain.compared);
  const name = user?.userPrincipalName ?? user?.mail ?? user?.objectId;
  return (
    <div className="space-y-4">
      <div className="space-y-1">
        {user ? (
          <p className="flex flex-wrap items-center gap-2" data-testid="user">
            <strong>{user.displayName ?? name}</strong>
            {user.displayName && name ? <span className="text-muted-foreground">{name}</span> : null}
          </p>
        ) : null}
        <p className="text-sm text-muted-foreground" data-testid="snapshots">
          From the snapshot taken {side(report.from)} to the one taken {side(report.to)}: {report.added} added,{" "}
          {report.removed} removed.
        </p>
      </div>
      {report.warnings.map((warning) => (
        <Notice key={warning} tone="warning" icon={<TriangleAlert aria-hidden />} title="Good to know">
          {windowWarning(warning)}
        </Notice>
      ))}
      {changed.map((domain) => (
        <section key={domain.name} className="space-y-1">
          <h3 className="font-medium">
            {diffPart(domain.name)}{" "}
            <span className="text-muted-foreground">
              · {domain.added} added, {domain.removed} removed
            </span>
          </h3>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Change</TableHead>
                <TableHead>What</TableHead>
                <TableHead>Who</TableHead>
                <TableHead>How</TableHead>
                {user ? <TableHead>Through</TableHead> : null}
              </TableRow>
            </TableHeader>
            <TableBody>
              {changes
                .filter((entry) => entry.domain === domain.name)
                .map((entry) => (
                  <TableRow key={`${entry.change}:${entry.what}:${entry.who}:${entry.how}:${entry.through}`}>
                    <TableCell>
                      <Badge variant={entry.change === "added" ? "secondary" : "outline"}>{entry.change}</Badge>
                    </TableCell>
                    <TableCell>{entry.what}</TableCell>
                    <TableCell>{entry.who}</TableCell>
                    <TableCell>{howShown(entry)}</TableCell>
                    {user ? <TableCell>{entry.through ?? "direct"}</TableCell> : null}
                  </TableRow>
                ))}
            </TableBody>
          </Table>
        </section>
      ))}
      {unchanged.length > 0 ? (
        <p className="text-sm text-muted-foreground" data-testid="unchanged">
          No change in: {unchanged.map((domain) => diffPart(domain.name)).join(", ")}.
        </p>
      ) : null}
      {leftOut.length > 0 ? (
        <p className="text-sm text-muted-foreground" data-testid="left-out">
          Not compared: {leftOut.map((domain) => diffPart(domain.name)).join(", ")} (see "Good to know").
        </p>
      ) : null}
    </div>
  );
}
