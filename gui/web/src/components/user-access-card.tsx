import { DatabaseZap, TriangleAlert } from "lucide-react";
import { type FormEvent, type ReactNode, useId, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { BridgeClient } from "../bridge/client";
import { type AccessReport, MessageType, type NoSnapshotInfo } from "../bridge/protocol";
import { AnswerFor, Field, LiveStatus, LookupError, SubmitButton, useLookup } from "./card-parts";
import { rightNames } from "./mailbox-access-card";
import { Notice } from "./plan-view";
import { count, windowWarning } from "./plan-wording";
import { describeTaken } from "./snapshot-time";
import { UserPicker } from "./user-picker";

export { describeTaken };

/** A snapshot's age as the card writes it: hours under two days, days from then on. */
export function describeAge(hours: number): string {
  return hours < 48 ? `${hours} hour(s) ago` : `${(hours / 24).toFixed(1)} days ago`;
}

/** What a right or a permission comes through: the group, or "direct" for one granted to the user. */
const through = (group: string | undefined) => group ?? "direct";

type Result = { kind: "report"; asked: string; data: AccessReport } | { kind: "none"; data: NoSnapshotInfo };

/** The status line of an access view: the user as asked, and how much of each section. */
function reportStatus(asked: string, report: AccessReport): string {
  const { groups, sites, sharedItems, mailboxes, calendars } = report;
  return `${asked}: ${count(groups.length, "group")}, ${count(sites.length, "site")}, ${count(sharedItems.length, "shared file")}, ${count(mailboxes.length, "mailbox right")}, ${count(calendars.length, "calendar permission")}`;
}

/**
 * What one user reaches (task 102), as `access <user>` answers it from the newest snapshot (task 084): the groups and
 * the member groups a nested membership comes through, the sites of those groups, the shared items, the mailbox rights
 * and the calendar permissions held directly or through a group, the counts of what reaches everyone, the snapshot's
 * time and age, and its warnings in the window's words. The user is picked from the suggestions (the directory, else the snapshot) as the colleague
 * types a name (the user picker, task 112), or typed in full. The host reads the snapshot file on this computer only: no
 * sign-in, no request to Microsoft 365, so the card works signed out too. Without a snapshot the card says so plainly
 * and names who takes one. A failure shows under the field and drops the old answer (task 120); the field stays usable
 * while the lookup runs and the status region says what is going on (task 121).
 */
export function UserAccessCard({ client }: { client: BridgeClient }) {
  const id = useId();
  const [query, setQuery] = useState("");
  const lookup = useLookup<Result>();
  const { busy, result, error, status, setStatus, run } = lookup;

  function lookUp(user: string) {
    const asked = user.trim();
    if (asked === "") {
      return;
    }
    void run(
      `Looking up ${asked}…`,
      () => client.call(MessageType.access, { user: asked }),
      (reply) => {
        switch (reply.type) {
          case MessageType.accessReport:
            return { result: { kind: "report", asked, data: reply.data }, status: reportStatus(asked, reply.data) };
          case MessageType.noSnapshot:
            return { result: { kind: "none", data: reply.data }, status: "No snapshot yet" };
          default:
            return undefined;
        }
      },
    );
  }

  return (
    <Card aria-labelledby={`${id}-title`}>
      <CardHeader>
        <CardTitle id={`${id}-title`}>User access</CardTitle>
        <CardDescription>
          What one user reaches: groups, sites, shared files, mailboxes and calendars, from the newest snapshot of the
          tenant on this computer. Nothing is asked of Microsoft 365, so the answer is as old as the snapshot.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <LiveStatus text={status} />
        <form
          aria-busy={busy}
          onSubmit={(event: FormEvent<HTMLFormElement>) => {
            event.preventDefault();
            lookUp(query);
          }}
        >
          <Field id={`${id}-user`} label="User">
            <div className="flex items-start gap-2">
              <UserPicker
                client={client}
                id={`${id}-user`}
                value={query}
                onChange={setQuery}
                onPick={lookUp}
                busy={busy}
                invalid={error !== null}
                describedBy={error ? `${id}-error` : undefined}
                onAnnounce={setStatus}
              />
              <SubmitButton
                busy={busy}
                missing={query.trim() === "" ? "Type a user's name or address first." : null}
                hintId={`${id}-hint`}
                idle="Look up"
                working="Looking up…"
              />
            </div>
          </Field>
        </form>
        {error ? <LookupError id={`${id}-error`} title="The lookup failed" error={error} /> : null}
        {result ? (
          <div className={busy ? "opacity-50" : undefined} aria-busy={busy}>
            {result.kind === "none" ? (
              <NoSnapshotView info={result.data} />
            ) : (
              <div className="space-y-2">
                <AnswerFor query={result.asked} />
                <AccessReportView report={result.data} />
              </div>
            )}
          </div>
        ) : null}
      </CardContent>
    </Card>
  );
}

/** The card without a snapshot to answer from: a plain state, not an error. */
export function NoSnapshotView({ info }: { info: NoSnapshotInfo }) {
  return (
    <Notice tone="info" icon={<DatabaseZap aria-hidden />} title="No snapshot yet">
      <p>The card answers from a snapshot of the tenant, and this computer has none for the profile {info.profile}.</p>
      <p className="text-muted-foreground">{info.hint}</p>
    </Notice>
  );
}

/** One section of the view: its title with the count, the table, or a line when there is nothing. */
function Section({
  title,
  count,
  empty,
  headers,
  children,
}: {
  title: string;
  count: number;
  empty: string;
  headers: string[];
  children: ReactNode;
}) {
  return (
    <section className="space-y-1">
      <h3 className="font-medium">
        {title} <span className="text-muted-foreground tabular-nums">· {count}</span>
      </h3>
      {count === 0 ? (
        <p className="text-sm text-muted-foreground">{empty}</p>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              {headers.map((header) => (
                <TableHead key={header}>{header}</TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>{children}</TableBody>
        </Table>
      )}
    </section>
  );
}

/** The access view of one user: who, from which snapshot, the five sections, what reaches everyone, the warnings. */
export function AccessReportView({ report }: { report: AccessReport }) {
  const { user, groups, sites, sharedItems, mailboxes, calendars, everyone } = report;
  const name = user.userPrincipalName ?? user.mail ?? user.objectId;
  return (
    <div className="space-y-4">
      <div className="space-y-1">
        <p className="flex flex-wrap items-center gap-2">
          <strong>{user.displayName ?? name}</strong>
          {user.displayName && name ? <span className="text-muted-foreground">{name}</span> : null}
          {user.objectId ? null : <Badge variant="outline">no directory account</Badge>}
        </p>
        <p className="text-sm text-muted-foreground tabular-nums" data-testid="snapshot">
          From the snapshot taken {describeTaken(report.taken)}, {describeAge(report.ageHours)}.
        </p>
      </div>
      {report.warnings.map((warning) => (
        <Notice key={warning} tone="warning" icon={<TriangleAlert aria-hidden />} title="Good to know">
          {windowWarning(warning)}
        </Notice>
      ))}
      <Section title="Groups" count={groups.length} empty="In no group." headers={["Group", "Type", "Membership"]}>
        {groups.map((group) => (
          <TableRow key={`${group.id}:${group.displayName}`}>
            <TableCell>{group.displayName ?? group.id}</TableCell>
            <TableCell>{group.kind ?? ""}</TableCell>
            <TableCell>{group.direct ? "direct" : `through ${group.via.join(", ")}`}</TableCell>
          </TableRow>
        ))}
      </Section>
      <Section
        title="Sites"
        count={sites.length}
        empty="No site through a group."
        headers={["Site", "Address", "Through"]}
      >
        {sites.map((site) => (
          <TableRow key={`${site.name}:${site.role}`}>
            <TableCell>{site.name}</TableCell>
            <TableCell>{site.webUrl ?? ""}</TableCell>
            <TableCell>
              {site.role} of {site.group}
            </TableCell>
          </TableRow>
        ))}
      </Section>
      <Section
        title="Shared files"
        count={sharedItems.length}
        empty="No file or folder shared with the user or the groups."
        headers={["Item", "Access", "Through"]}
      >
        {sharedItems.map((item, index) => (
          // The same item can be shared twice, as a link and directly: the place in the list tells the rows apart.
          // biome-ignore lint/suspicious/noArrayIndexKey: the rows have no id of their own.
          <TableRow key={index}>
            <TableCell>
              {item.site}: {(item.path ?? "").replace(/\/$/, "")}/{item.item}
            </TableCell>
            <TableCell>
              {[item.kind, item.scope, item.roles, item.expires ? `until ${item.expires}` : undefined]
                .filter((part) => part !== undefined)
                .join(" ")}
            </TableCell>
            <TableCell>{through(item.through)}</TableCell>
          </TableRow>
        ))}
      </Section>
      <Section
        title="Mailboxes"
        count={mailboxes.length}
        empty="No right on another mailbox."
        headers={["Mailbox", "Address", "Right", "Through"]}
      >
        {mailboxes.map((mailbox) => (
          <TableRow key={`${mailbox.address}:${mailbox.right}:${mailbox.through}`}>
            <TableCell>{mailbox.mailbox}</TableCell>
            <TableCell className="font-mono text-xs">{mailbox.address}</TableCell>
            <TableCell>{rightNames[mailbox.right]}</TableCell>
            <TableCell>{through(mailbox.through)}</TableCell>
          </TableRow>
        ))}
      </Section>
      <Section
        title="Calendars"
        count={calendars.length}
        empty="No permission on another calendar."
        headers={["Mailbox", "Address", "Permission", "Through"]}
      >
        {calendars.map((calendar) => (
          <TableRow key={`${calendar.address}:${calendar.folder}:${calendar.through}`}>
            <TableCell>{calendar.mailbox}</TableCell>
            <TableCell>{calendar.address}</TableCell>
            <TableCell>
              {calendar.folder}: {calendar.rights}
            </TableCell>
            <TableCell>{through(calendar.through)}</TableCell>
          </TableRow>
        ))}
      </Section>
      <p className="text-sm text-muted-foreground tabular-nums" data-testid="everyone">
        Reaches everyone, this user too: {everyone.organizationLinks} link(s) anyone in the organization can open,{" "}
        {everyone.anonymousLinks} link(s) anyone can open, {everyone.defaultCalendars} calendar(s) open beyond free/busy
        to every user and {everyone.anonymousCalendars} to anonymous.
      </p>
    </div>
  );
}
