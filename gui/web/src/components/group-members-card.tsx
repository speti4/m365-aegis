import { type FormEvent, useId, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { BridgeClient } from "../bridge/client";
import { type GroupKind, type GroupMembers, MessageType } from "../bridge/protocol";
import { AnswerFor, Field, LiveStatus, LookupError, SubmitButton, useLookup } from "./card-parts";
import { count } from "./plan-wording";

const kindNames: Record<GroupKind, string> = {
  microsoft365: "Microsoft 365",
  security: "Security",
  "mail-enabled-security": "Mail-enabled security",
  distribution: "Distribution",
};

/**
 * `group members <group>`: the group's direct members, by name, in a table, under the name it was asked for. A failure
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
              status: `${reply.data.group.displayName}: ${count(reply.data.members.length, "direct member")}`,
            }
          : undefined,
    );
  }

  return (
    <Card aria-labelledby={`${id}-title`}>
      <CardHeader>
        <CardTitle id={`${id}-title`}>Group members</CardTitle>
        <CardDescription>
          A group's direct members, by the group's object id, its exact name or address, or a word of its name.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <LiveStatus text={status} />
        <form onSubmit={lookUp} aria-busy={busy}>
          <Field id={`${id}-group`} label="Group">
            <div className="flex items-start gap-2">
              <Input
                id={`${id}-group`}
                placeholder="Group name, address or object id"
                value={group}
                onChange={(event) => setGroup(event.target.value)}
                readOnly={busy}
                aria-invalid={error ? true : undefined}
                aria-describedby={error ? `${id}-error` : undefined}
              />
              <SubmitButton
                busy={busy}
                missing={group.trim() === "" ? "Type a group's name, address or object id first." : null}
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
        <Badge variant="secondary">{kindNames[group.kind]}</Badge>
        {group.synced ? <Badge variant="outline">synced</Badge> : null}
        {group.mail ? <span className="text-muted-foreground">{group.mail}</span> : null}
        <span className="text-muted-foreground">· {members.length} direct member(s)</span>
      </p>
      {members.length === 0 ? (
        <p className="text-muted-foreground">The group has no direct members.</p>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Type</TableHead>
              <TableHead>Name</TableHead>
              <TableHead>Sign-in name or address</TableHead>
              <TableHead>Id</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {members.map((member) => (
              <TableRow key={member.id}>
                <TableCell>{member.type}</TableCell>
                <TableCell>{member.displayName ?? ""}</TableCell>
                <TableCell className="font-mono text-xs">{member.userPrincipalName ?? member.mail ?? ""}</TableCell>
                <TableCell className="font-mono text-xs">{member.id}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </div>
  );
}
