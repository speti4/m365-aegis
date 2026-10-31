import { type FormEvent, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { BridgeClient } from "../bridge/client";
import { type BridgeError, type GroupKind, type GroupMembers, MessageType } from "../bridge/protocol";

const kindNames: Record<GroupKind, string> = {
  microsoft365: "Microsoft 365",
  security: "Security",
  "mail-enabled-security": "Mail-enabled security",
  distribution: "Distribution",
};

/** `group members <group>`: the group's direct members, by name, in a table; a failure goes to `onError`. */
export function GroupMembersCard({ client, onError }: { client: BridgeClient; onError: (error: BridgeError) => void }) {
  const [group, setGroup] = useState("");
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<GroupMembers | null>(null);

  async function lookUp(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    try {
      const reply = await client.call(MessageType.groupMembers, { group: group.trim() });
      if (reply.type === MessageType.members) {
        setResult(reply.data);
      } else if (reply.type === MessageType.error) {
        onError(reply.data);
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Group members</CardTitle>
        <CardDescription>
          A group's direct members, by the group's object id, its exact name or address, or a word of its name.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <form className="flex gap-2" onSubmit={lookUp}>
          <Input
            aria-label="Group"
            placeholder="Group name, address or object id"
            value={group}
            onChange={(event) => setGroup(event.target.value)}
            disabled={busy}
          />
          <Button type="submit" disabled={busy || group.trim() === ""}>
            {busy ? "Looking up…" : "Look up"}
          </Button>
        </form>
        {result ? <MembersTable result={result} /> : null}
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
                <TableCell>{member.userPrincipalName ?? member.mail ?? ""}</TableCell>
                <TableCell className="font-mono text-xs">{member.id}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </div>
  );
}
