import { History, TriangleAlert } from "lucide-react";
import { type FormEvent, useId, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { BridgeClient } from "../bridge/client";
import {
  MessageType,
  type SnapshotDiffEntry,
  type SnapshotDiffReport,
  type SnapshotDiffSide,
  type TooFewSnapshotsInfo,
} from "../bridge/protocol";
import { Field, LiveStatus, LookupError, SubmitButton, useLookup } from "./card-parts";
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
 * 365. With fewer than two snapshots the card says so plainly. A failure shows under the fields and drops the old
 * answer (task 120); the fields stay usable while the host compares, and the status region says what is going on
 * (task 121).
 */
export function SnapshotDiffCard({ client }: { client: BridgeClient }) {
  const id = useId();
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [user, setUser] = useState("");
  const { busy, result, error, status, run } = useLookup<Result>();

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

  const fields = [
    { name: "from", label: "From", placeholder: "The one before", value: from, set: setFrom },
    { name: "to", label: "To", placeholder: "The newest", value: to, set: setTo },
    {
      name: "user",
      label: "Only this user",
      placeholder: "Optional: a principal name or address",
      value: user,
      set: setUser,
    },
  ];
  return (
    <Card aria-labelledby={`${id}-title`}>
      <CardHeader>
        <CardTitle id={`${id}-title`}>Access changes</CardTitle>
        <CardDescription>
          Who gained or lost access between two snapshots of the tenant on this computer: the newest and the one before
          it, unless you name them. A user narrows it to that person, groups within groups included. Nothing is asked of
          Microsoft 365.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <LiveStatus text={status} />
        <form className="grid items-end gap-2 sm:grid-cols-[1fr_1fr_1.5fr_auto]" onSubmit={compare} aria-busy={busy}>
          {fields.map((field) => (
            <Field key={field.name} id={`${id}-${field.name}`} label={field.label}>
              <Input
                id={`${id}-${field.name}`}
                placeholder={field.placeholder}
                value={field.value}
                onChange={(event) => field.set(event.target.value)}
                readOnly={busy}
                aria-invalid={error ? true : undefined}
                aria-describedby={error ? `${id}-error` : undefined}
              />
            </Field>
          ))}
          <div>
            <SubmitButton busy={busy} missing={null} hintId={`${id}-hint`} idle="Compare" working="Comparing…" />
          </div>
        </form>
        {error ? <LookupError id={`${id}-error`} title="The comparison failed" error={error} /> : null}
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
                      <Badge variant={entry.change === "added" ? "success" : "danger"}>{entry.change}</Badge>
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
