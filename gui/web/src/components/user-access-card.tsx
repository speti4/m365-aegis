import { DatabaseZap, TriangleAlert } from "lucide-react";
import { type FormEvent, type ReactNode, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { BridgeClient } from "../bridge/client";
import { type AccessReport, type BridgeError, MessageType, type NoSnapshotInfo } from "../bridge/protocol";
import { rightNames } from "./mailbox-access-card";
import { Notice } from "./plan-view";
import { windowWarning } from "./plan-wording";
import { describeTaken } from "./snapshot-time";
import { UserPicker } from "./user-picker";

export { describeTaken };

/** A snapshot's age as the card writes it: hours under two days, days from then on. */
export function describeAge(hours: number): string {
  return hours < 48 ? `${hours} hour(s) ago` : `${(hours / 24).toFixed(1)} days ago`;
}

/** What a right or a permission comes through: the group, or "direct" for one granted to the user. */
const through = (group: string | undefined) => group ?? "direct";

type Result = { kind: "report"; data: AccessReport } | { kind: "none"; data: NoSnapshotInfo };

/**
 * What one user reaches (task 102), as `access <user>` answers it from the newest snapshot (task 084): the groups and
 * the member groups a nested membership comes through, the sites of those groups, the shared items, the mailbox rights
 * and the calendar permissions held directly or through a group, the counts of what reaches everyone, the snapshot's
 * time and age, and its warnings in the window's words. The user is picked from the suggestions (the directory, else the snapshot) as the colleague
 * types a name (the user picker, task 112), or typed in full. The host reads the snapshot file on this computer only: no
 * sign-in, no request to Microsoft 365, so the card works signed out too. Without a snapshot the card says so plainly
 * and names who takes one; an `error` reply is the page's toast.
 */
export function UserAccessCard({ client, onError }: { client: BridgeClient; onError: (error: BridgeError) => void }) {
  const [query, setQuery] = useState("");
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<Result | null>(null);

  async function lookUp(user: string) {
    setBusy(true);
    try {
      const reply = await client.call(MessageType.access, { user: user.trim() });
      switch (reply.type) {
        case MessageType.accessReport:
          setResult({ kind: "report", data: reply.data });
          break;
        case MessageType.noSnapshot:
          setResult({ kind: "none", data: reply.data });
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
        <CardTitle>User access</CardTitle>
        <CardDescription>
          What one user reaches: groups, sites, shared files, mailboxes and calendars, from the newest snapshot of the
          tenant on this computer. Nothing is asked of Microsoft 365, so the answer is as old as the snapshot.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <form
          className="flex items-start gap-2"
          onSubmit={(event: FormEvent<HTMLFormElement>) => {
            event.preventDefault();
            void lookUp(query);
          }}
        >
          <UserPicker
            client={client}
            value={query}
            onChange={setQuery}
            onPick={(user) => void lookUp(user)}
            disabled={busy}
          />
          <Button type="submit" disabled={busy || query.trim() === ""}>
            {busy ? "Looking up…" : "Look up"}
          </Button>
        </form>
        {result?.kind === "none" ? <NoSnapshotView info={result.data} /> : null}
        {result?.kind === "report" ? <AccessReportView report={result.data} /> : null}
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
        {title} <span className="text-muted-foreground">· {count}</span>
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
        <p className="text-sm text-muted-foreground" data-testid="snapshot">
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
            <TableCell>{mailbox.address}</TableCell>
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
      <p className="text-sm text-muted-foreground" data-testid="everyone">
        Reaches everyone, this user too: {everyone.organizationLinks} link(s) anyone in the organization can open,{" "}
        {everyone.anonymousLinks} link(s) anyone can open, {everyone.defaultCalendars} calendar(s) open beyond free/busy
        to every user and {everyone.anonymousCalendars} to anonymous.
      </p>
    </div>
  );
}
