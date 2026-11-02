import type { MouseEvent } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { reveal } from "@/lib/scroll";
import { cn } from "@/lib/utils";
import { canSignIn, exchangeError, type SignInState, statusLabel } from "./sign-in-state";

/** The page's groups of cards, in their order, which the top bar links to: each is a section with this id. */
export const pageGroups = [
  { id: "look-up", name: "Look up" },
  { id: "snapshots", name: "Snapshots" },
  { id: "change", name: "Change" },
] as const;

/** The id of the sign-in card's section, which the bar's state links to for the details. */
export const signInSection = "sign-in";

/** A tenant id as the bar writes it: its first eight characters; the whole id is the element's title. */
export function shortTenant(tenantId: string): string {
  return tenantId.length > 9 ? `${tenantId.slice(0, 8)}…` : tenantId;
}

/** Scrolls to the section the link names instead of changing the address (the dev sketch's route is a hash). */
function follow(event: MouseEvent<HTMLAnchorElement>, id: string) {
  event.preventDefault();
  reveal(document.getElementById(id));
}

/**
 * The window's top bar (task 124): one quiet row that stays at the top while the page scrolls, with the title and
 * the version, the links to the card groups, and the sign-in as the sign-in card reads it: a status dot, the state in
 * words ("signed in · Exchange unavailable" when the Exchange Online row has no token), the account, the tenant
 * shortened and the profile, and Sign in or Sign out. The sign-in card below keeps the details: the token rows, the
 * device code, Refresh.
 */
export function TopBar({
  version,
  hosted,
  signIn,
  onSignIn,
  onSignOut,
}: {
  version: string | null;
  hosted: boolean;
  signIn: SignInState;
  onSignIn: () => void;
  onSignOut: () => void;
}) {
  return (
    <header className="sticky top-0 z-10 border-b bg-background">
      <div className="flex flex-wrap items-center gap-x-5 gap-y-1 px-6 py-2">
        <div className="flex items-baseline gap-2">
          <h1 className="text-base font-semibold tracking-tight">m365-aegis</h1>
          <span className="text-xs text-muted-foreground" data-testid="version">
            {version ?? "…"}
          </span>
          {hosted ? null : (
            <Badge variant="outline" title="The browser mock: made-up data, no host">
              mock
            </Badge>
          )}
        </div>
        <nav aria-label="Card groups">
          <ul className="flex items-center gap-1 text-sm">
            {pageGroups.map((group) => (
              <li key={group.id}>
                <a
                  href={`#${group.id}`}
                  onClick={(event) => follow(event, group.id)}
                  className="rounded-md px-2 py-1 text-muted-foreground hover:bg-muted hover:text-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-solid focus-visible:outline-ring"
                >
                  {group.name}
                </a>
              </li>
            ))}
          </ul>
        </nav>
        <Session state={signIn} onSignIn={onSignIn} onSignOut={onSignOut} />
      </div>
    </header>
  );
}

/** The dot's color per state, the sign-in badge's status token: the bar's one color. */
function dotColor(state: SignInState): string {
  switch (state.status) {
    case "signed-in":
      return exchangeError(state) === null ? "bg-success" : "bg-warning";
    case "signed-out":
      return "bg-warning";
    case "unknown":
      return "bg-danger";
    default:
      return "bg-muted-foreground/50";
  }
}

function Session({ state, onSignIn, onSignOut }: { state: SignInState; onSignIn: () => void; onSignOut: () => void }) {
  const info = state.status === "signed-in" || state.status === "signing-out" ? state.info : null;
  const account = info?.account ?? (state.status === "signed-out" ? state.info.account : undefined);
  const profile = info?.profile ?? (state.status === "signed-out" ? state.info.profile : undefined);
  return (
    <div className="ml-auto flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1 text-sm" data-testid="session">
      <a
        href={`#${signInSection}`}
        onClick={(event) => follow(event, signInSection)}
        className="flex items-center gap-2 rounded-md hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-solid focus-visible:outline-ring"
        title="The sign-in card: the profile, the tokens, Refresh"
      >
        <span className={cn("size-2 shrink-0 rounded-full", dotColor(state))} aria-hidden data-testid="status-dot" />
        <span data-testid="status">{statusLabel(state) ?? "checking the sign-in…"}</span>
      </a>
      {account ? (
        <span
          className={cn("truncate", info ? "font-medium" : "text-muted-foreground")}
          title={info?.displayName}
          data-testid="bar-account"
        >
          {account}
        </span>
      ) : null}
      {info?.tenantId ? (
        <span className="hidden text-muted-foreground lg:inline" title={info.tenantId} data-testid="bar-tenant">
          tenant {shortTenant(info.tenantId)}
        </span>
      ) : null}
      {profile ? <span className="hidden text-muted-foreground lg:inline">profile {profile}</span> : null}
      {state.status === "signed-in" ? (
        <Button variant="outline" size="sm" onClick={onSignOut}>
          Sign out
        </Button>
      ) : null}
      {canSignIn(state) ? (
        <Button size="sm" onClick={onSignIn}>
          Sign in
        </Button>
      ) : null}
    </div>
  );
}
