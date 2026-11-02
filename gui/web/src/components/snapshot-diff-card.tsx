import { History, TriangleAlert } from "lucide-react";
import { type FormEvent, useId, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { BridgeClient } from "../bridge/client";
import {
  MessageType,
  type SnapshotDiffReport,
  type SnapshotDiffSide,
  type TooFewSnapshotsInfo,
} from "../bridge/protocol";
import { Field, LiveStatus, LookupError, SubmitButton, useLookup } from "./card-parts";
import { Notice } from "./plan-view";
import { diffPart, windowWarning } from "./plan-wording";
import { describeAge, describeTaken } from "./user-access-card";
import { UserPicker } from "./user-picker";
import { diffHow, sentence } from "./vocabulary";

type Result = { kind: "report"; data: SnapshotDiffReport } | { kind: "too-few"; data: TooFewSnapshotsInfo };

/**
 * Who gained or lost access between two snapshots (task 115), as `snapshot diff` answers it (task 098): the newest
 * snapshot and the one before it unless the colleague names them (a name, `latest`, `yesterday` or a date), every
 * membership, owner, shared file, mailbox permission, calendar permission and forwarding that appeared or disappeared, by
 * part of the tenant; with a user, that user's changes only, nested memberships and what comes through a group
 * included (task 114). The host reads the snapshot files on this computer only: no sign-in, no request to Microsoft
 * 365. With fewer than two snapshots the card says so plainly. A failure shows under the fields and drops the old
 * answer (task 120); the fields stay usable while the host compares, and the status region says what is going on
 * (task 121). A line under the fields says what "Compare from" and "Compare to" take, and the user field is the user picker (task 126):
 * a pick fills it, Enter on a closed list compares.
 */
export function SnapshotDiffCard({ client }: { client: BridgeClient }) {
  const id = useId();
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [user, setUser] = useState("");
  const { busy, result, error, status, setStatus, run } = useLookup<Result>();

  function compare(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const picks = Object.entries({ from, to, user })
      .map(([name, value]) => [name, value.trim()] as const)
      .filter(([, value]) => value !== "");
    void run(
      "Comparing the snapshots…",
      () => client.call(MessageType.snapshotDiff, Object.fromEntries(picks)),
      (reply) => {
        switch (reply.type) {
          case MessageType.snapshotDiffReport:
            return {
              result: { kind: "report", data: reply.data },
              status: `${reply.data.added} added, ${reply.data.removed} removed between the two snapshots`,
            };
          case MessageType.tooFewSnapshots:
            return { result: { kind: "too-few", data: reply.data }, status: "Nothing to compare yet" };
          default:
            return undefined;
        }
      },
    );
  }

  const sides = [
    { name: "from", label: "Compare from", placeholder: "Snapshot before the latest", value: from, set: setFrom },
    { name: "to", label: "Compare to", placeholder: "Latest snapshot", value: to, set: setTo },
  ];
  const errorId = `${id}-error`;
  const hintId = `${id}-sides-hint`;
  return (
    <Card aria-labelledby={`${id}-title`}>
      <CardHeader>
        <CardTitle id={`${id}-title`}>Access changes</CardTitle>
        <CardDescription>
          See who gained or lost access between two snapshots on this computer, for everyone or for one user.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <LiveStatus text={status} />
        <form className="space-y-3" onSubmit={compare} aria-busy={busy}>
          {/* Aligned at the top: the user picker's list opens under its field, in the flow. */}
          <div className="grid items-start gap-2 sm:grid-cols-[1fr_1fr_1.5fr]">
            {sides.map((side) => (
              <Field key={side.name} id={`${id}-${side.name}`} label={side.label}>
                <Input
                  id={`${id}-${side.name}`}
                  placeholder={side.placeholder}
                  value={side.value}
                  onChange={(event) => side.set(event.target.value)}
                  readOnly={busy}
                  aria-invalid={error ? true : undefined}
                  aria-describedby={error ? `${hintId} ${errorId}` : hintId}
                />
              </Field>
            ))}
            <Field id={`${id}-user`} label="Only this user">
              <UserPicker
                client={client}
                id={`${id}-user`}
                placeholder="Optional: start typing a name"
                value={user}
                onChange={setUser}
                busy={busy}
                invalid={error !== null}
                describedBy={error ? errorId : undefined}
                onAnnounce={setStatus}
                enterPicksFirst
              />
            </Field>
          </div>
          <p id={hintId} className="text-xs text-muted-foreground" data-testid="sides-hint">
            Leave both empty to compare the latest snapshot with the one before it. Each also takes <code>latest</code>,{" "}
            <code>yesterday</code>, a date such as <code>2026-10-31</code>, or a snapshot's name.
          </p>
          <SubmitButton busy={busy} missing={null} hintId={`${id}-hint`} idle="Compare" working="Comparing…" />
        </form>
        {error ? <LookupError id={errorId} title="The comparison failed" error={error} /> : null}
        {result ? (
          <div className={busy ? "opacity-50" : undefined} aria-busy={busy}>
            {result.kind === "too-few" ? (
              <TooFewSnapshotsView info={result.data} />
            ) : (
              <SnapshotDiffView report={result.data} />
            )}
          </div>
        ) : null}
      </CardContent>
    </Card>
  );
}

/** The card without two snapshots to compare: a plain state, not an error. */
export function TooFewSnapshotsView({ info }: { info: TooFewSnapshotsInfo }) {
  return (
    <Notice tone="info" icon={<History aria-hidden />} title="Nothing to compare yet">
      <p>{sentence(info.message)}</p>
      <p className="text-muted-foreground">{sentence(info.hint)}</p>
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
        <p className="text-sm text-muted-foreground tabular-nums" data-testid="snapshots">
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
            <span className="text-muted-foreground tabular-nums">
              · {domain.added} added, {domain.removed} removed
            </span>
          </h3>
          {/* One fixed column layout for every part, so the columns line up from one table to the next (task 126). */}
          <Table className="table-fixed [&_td]:break-words [&_td]:whitespace-normal">
            <colgroup>
              <col className="w-24" />
              <col />
              <col className="w-[28%]" />
              <col className="w-[18%]" />
              {user ? <col className="w-[18%]" /> : null}
            </colgroup>
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
                      <Badge variant={entry.change === "added" ? "success" : "danger"}>
                        {entry.change === "added" ? "Added" : "Removed"}
                      </Badge>
                    </TableCell>
                    <TableCell>{entry.what}</TableCell>
                    <TableCell>{entry.who}</TableCell>
                    <TableCell>{diffHow(entry.domain, entry.how)}</TableCell>
                    {user ? <TableCell>{entry.through ?? "Direct"}</TableCell> : null}
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
