import { useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardAction, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import type { DeviceCodePrompt, ResourceToken } from "../bridge/protocol";
import { canSignIn, type SignInState } from "./sign-in-state";

export type { SignInState } from "./sign-in-state";

/**
 * The sign-in (task 040): the state as `m365-aegis whoami` reads it, silently; "Sign in" when it is signed out (the
 * broker's account picker opens over the window), "Sign in with a code" for a device code instead, shown here while
 * the sign-in waits for the browser step; "Sign out" when it is signed in.
 */
export function SignInCard({
  state,
  onRefresh,
  onSignIn,
  onSignOut,
}: {
  state: SignInState;
  onRefresh: () => void;
  onSignIn: (deviceCode: boolean) => void;
  onSignOut: () => void;
}) {
  const busy = state.status === "checking" || state.status === "signing-in" || state.status === "signing-out";
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          Sign-in <StatusBadge state={state} />
        </CardTitle>
        <CardDescription>The account the window works as, read silently as `m365-aegis whoami` does.</CardDescription>
        <CardAction className="flex gap-2">
          {state.status === "signed-in" ? (
            <Button variant="outline" size="sm" onClick={onSignOut}>
              Sign out
            </Button>
          ) : null}
          <Button variant="outline" size="sm" onClick={onRefresh} disabled={busy}>
            Refresh
          </Button>
        </CardAction>
      </CardHeader>
      <CardContent className="space-y-4">
        <SignInDetails state={state} />
        {canSignIn(state) ? (
          <div className="flex flex-wrap gap-2">
            <Button onClick={() => onSignIn(false)}>Sign in</Button>
            <Button variant="outline" onClick={() => onSignIn(true)}>
              Sign in with a code
            </Button>
          </div>
        ) : null}
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
    case "signing-in":
      return <Badge variant="secondary">signing in…</Badge>;
    case "signing-out":
      return <Badge variant="secondary">signing out…</Badge>;
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
    case "signing-in":
      return state.deviceCode ? (
        <DeviceCode prompt={state.deviceCode} />
      ) : (
        <p className="text-muted-foreground" data-testid="signing-in">
          Waiting for the sign-in: choose the account in the Windows sign-in window that opened over this one.
        </p>
      );
    case "signed-out":
      return (
        <div className="space-y-2">
          {state.info.profile ? (
            <p>
              Profile <strong>{state.info.profile}</strong>
              {state.info.account ? ` (${state.info.account})` : null} has no usable sign-in.
            </p>
          ) : (
            <p>There is no tenant profile yet: the command line writes it once, then the window signs in with it.</p>
          )}
          <p className="text-muted-foreground">{state.info.message}</p>
          {state.info.profile ? null : state.info.hint ? <p className="font-mono text-xs">{state.info.hint}</p> : null}
        </div>
      );
    case "signing-out":
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

/** The device code: the page to open in a browser (the window opens no other site) and the code, with a copy button. */
function DeviceCode({ prompt }: { prompt: DeviceCodePrompt }) {
  const [copied, setCopied] = useState(false);
  async function copy() {
    try {
      await navigator.clipboard.writeText(prompt.userCode);
      setCopied(true);
    } catch {
      setCopied(false);
    }
  }
  return (
    <div className="space-y-3 rounded-lg border p-4" data-testid="device-code">
      <p>
        Open <span className="font-mono text-sm select-all">{prompt.verificationUri}</span> in a browser and enter the
        code:
      </p>
      <div className="flex flex-wrap items-center gap-3">
        <span className="font-mono text-2xl font-semibold tracking-widest select-all">{prompt.userCode}</span>
        <Button variant="outline" size="sm" onClick={() => void copy()}>
          {copied ? "Copied" : "Copy the code"}
        </Button>
      </div>
      <p className="text-sm text-muted-foreground">
        The code works until {new Date(prompt.expiresOn).toLocaleTimeString()}; this card updates when the sign-in
        completes.
      </p>
    </div>
  );
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
