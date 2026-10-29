import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardAction, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import type { ResourceToken, SignedOutInfo, SignInInfo } from "../bridge/protocol";

/** What the page knows about the sign-in. */
export type SignInState =
  | { status: "checking" }
  | { status: "signed-in"; info: SignInInfo }
  | { status: "signed-out"; info: SignedOutInfo }
  | { status: "unknown" };

/** The sign-in state, as `m365-aegis whoami` reads it: silently, never a prompt. */
export function SignInCard({ state, onRefresh }: { state: SignInState; onRefresh: () => void }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          Sign-in <StatusBadge state={state} />
        </CardTitle>
        <CardDescription>The account the window works as, read silently as `m365-aegis whoami` does.</CardDescription>
        <CardAction>
          <Button variant="outline" size="sm" onClick={onRefresh} disabled={state.status === "checking"}>
            Refresh
          </Button>
        </CardAction>
      </CardHeader>
      <CardContent>
        <SignInDetails state={state} />
      </CardContent>
    </Card>
  );
}

function StatusBadge({ state }: { state: SignInState }) {
  switch (state.status) {
    case "signed-in":
      return <Badge>signed in</Badge>;
    case "signed-out":
      return <Badge variant="destructive">signed out</Badge>;
    default:
      return null;
  }
}

function SignInDetails({ state }: { state: SignInState }) {
  switch (state.status) {
    case "checking":
      return <p className="text-muted-foreground">Checking the sign-in…</p>;
    case "unknown":
      return <p className="text-muted-foreground">The sign-in state could not be read.</p>;
    case "signed-out":
      return (
        <div className="space-y-2">
          {state.info.profile ? (
            <p>
              Profile <strong>{state.info.profile}</strong>
              {state.info.account ? ` (${state.info.account})` : null} has no usable sign-in.
            </p>
          ) : null}
          <p className="text-muted-foreground">{state.info.message}</p>
          {state.info.hint ? <p className="font-mono text-xs">{state.info.hint}</p> : null}
        </div>
      );
    case "signed-in": {
      const { info } = state;
      return (
        <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-1.5">
          <dt className="text-muted-foreground">Account</dt>
          <dd data-testid="account">
            {info.displayName ? `${info.displayName} · ` : null}
            {info.account}
          </dd>
          <dt className="text-muted-foreground">Profile</dt>
          <dd>{info.profile}</dd>
          {info.tenantId ? (
            <>
              <dt className="text-muted-foreground">Tenant</dt>
              <dd className="font-mono text-xs leading-5">{info.tenantId}</dd>
            </>
          ) : null}
          {info.tokens.map((token) => (
            <TokenRow key={token.resource} token={token} />
          ))}
        </dl>
      );
    }
  }
}

const resourceNames: Record<string, string> = { graph: "Microsoft Graph", exchange: "Exchange Online" };

function TokenRow({ token }: { token: ResourceToken }) {
  return (
    <>
      <dt className="text-muted-foreground">{resourceNames[token.resource] ?? token.resource}</dt>
      <dd>
        {token.error ? (
          <span className="text-destructive">no token: {token.error}</span>
        ) : (
          <span>token until {token.expiresOn ? new Date(token.expiresOn).toLocaleString() : "?"}</span>
        )}
      </dd>
    </>
  );
}
