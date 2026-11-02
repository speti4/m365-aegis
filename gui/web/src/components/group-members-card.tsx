import { type FormEvent, useId, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { BridgeClient } from "../bridge/client";
import { type GroupMembers, MessageType } from "../bridge/protocol";
import { AnswerFor, Field, LiveStatus, LookupError, SubmitButton, useLookup } from "./card-parts";
import { DetailsLine } from "./details-line";
import { groupKindName, objectTypeName, plural } from "./vocabulary";

/**
 * `group members <group>`: the group's direct members, by name, in a table, under the name it was asked for; the
 * object IDs under a collapsed "Details" line (task 126); the type and the kind in the window's words (task 122). A failure
 * shows under the field, which it marks invalid, and drops the old table (task 120); the field stays usable while the
 * lookup runs, so the focus stays in it, and the card's status region says what is going on (task 121).
 */
export function GroupMembersCard({ client }: { client: BridgeClient }) {
  const id = useId();
  const [group, setGroup] = useState("");
  const { busy, result, error, status, run } = useLookup<{ asked: string; data: GroupMembers }>();

  function lookUp(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const asked = group.trim();
    if (asked === "") {
      return;
    }
    void run(
      `Looking up ${asked}…`,
      () => client.call(MessageType.groupMembers, { group: asked }),
      (reply) =>
        reply.type === MessageType.members
          ? {
              result: { asked, data: reply.data },
              status: `${reply.data.group.displayName}: ${plural(reply.data.members.length, "direct member")}`,
            }
          : undefined,
    );
  }

  return (
    <Card aria-labelledby={`${id}-title`}>
      <CardHeader>
        <CardTitle id={`${id}-title`}>Group members</CardTitle>
        <CardDescription>
          List a group's direct members. Search by name, email address, object ID, or one word of the name.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <LiveStatus text={status} />
        <form onSubmit={lookUp} aria-busy={busy}>
          <Field id={`${id}-group`} label="Group">
            <div className="flex items-start gap-2">
              <Input
                id={`${id}-group`}
                placeholder="Group name, email address or object ID"
                value={group}
                onChange={(event) => setGroup(event.target.value)}
                readOnly={busy}
                aria-invalid={error ? true : undefined}
                aria-describedby={error ? `${id}-error` : undefined}
              />
              <SubmitButton
                busy={busy}
                missing={group.trim() === "" ? "Enter a group's name, email address or object ID first." : null}
                hintId={`${id}-hint`}
                idle="Look up"
                working="Looking up…"
              />
            </div>
          </Field>
        </form>
        {error ? <LookupError id={`${id}-error`} title="The lookup failed" error={error} /> : null}
        {result ? (
          <div className={busy ? "space-y-2 opacity-50" : "space-y-2"} aria-busy={busy}>
            <AnswerFor query={result.asked} />
            <MembersTable result={result.data} />
          </div>
        ) : null}
      </CardContent>
    </Card>
  );
}

function MembersTable({ result }: { result: GroupMembers }) {
  const { group, members } = result;
  return (
    <div className="space-y-2">
      <p className="flex flex-wrap items-center gap-2">
        <strong>{group.displayName}</strong>
        <Badge variant="secondary">{groupKindName(group.kind)}</Badge>
        {group.synced ? <Badge variant="outline">Synced from on-premises</Badge> : null}
        {group.mail ? <span className="text-muted-foreground">{group.mail}</span> : null}
        <span className="text-muted-foreground tabular-nums">· {plural(members.length, "direct member")}</span>
      </p>
      {members.length === 0 ? (
        <p className="text-muted-foreground">The group has no direct members.</p>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Type</TableHead>
              <TableHead>Name</TableHead>
              <TableHead>Sign-in name or email</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {members.map((member) => (
              <TableRow key={member.id}>
                <TableCell>{objectTypeName(member.type)}</TableCell>
                <TableCell>{member.displayName ?? ""}</TableCell>
                <TableCell className="font-mono text-xs">{member.userPrincipalName ?? member.mail ?? ""}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      <DetailsLine>
        <p>The group's object ID: {idText(group.id)}</p>
        {members.length > 0 ? (
          <ul data-testid="member-ids">
            {members.map((member) => (
              <li key={member.id}>
                {member.displayName ?? member.userPrincipalName ?? member.mail ?? objectTypeName(member.type)}:{" "}
                {idText(member.id)}
              </li>
            ))}
          </ul>
        ) : null}
      </DetailsLine>
    </div>
  );
}

const idText = (id: string) => <span className="font-mono select-all">{id}</span>;
