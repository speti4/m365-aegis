import { type ReactNode, useCallback, useEffect, useMemo, useReducer, useRef, useState } from "react";
import { toast } from "sonner";
import { Toaster } from "@/components/ui/sonner";
import { focusTarget } from "@/lib/focus";
import { cn } from "@/lib/utils";
import { BridgeClient, SIGN_IN_TIMEOUT_MS } from "./bridge/client";
import { type BridgeError, MessageType } from "./bridge/protocol";
import { createTransport } from "./bridge/transport";
import { AppliedWrites } from "./components/applied-writes";
import { GroupMembersCard } from "./components/group-members-card";
import { MailboxAccessCard } from "./components/mailbox-access-card";
import { MailboxAccessWriteCard, MailboxForwardingCard } from "./components/mailbox-write-cards";
import { SignInCard } from "./components/sign-in-card";
import { exchangeError, signInReducer } from "./components/sign-in-state";
import { SnapshotDiffCard } from "./components/snapshot-diff-card";
import { pageGroups, signInSection, TopBar } from "./components/top-bar";
import { UserAccessCard } from "./components/user-access-card";

/** How long a toast stays: long enough to read a sentence and its hint; its close button ends it sooner. */
const TOAST_MS = 10_000;

/**
 * The window's page: it tells the host it is ready (the smoke run waits for that), shows the sign-in state and
 * signs in or out from its card (task 040), looks up a group's members (task 017) and a mailbox's access over the
 * window's Exchange Online session (task 045), shows what one user reaches from the newest snapshot (task 102) and who
 * gained or lost access between two snapshots (task 115), and
 * grants or revokes mailbox rights and sets or clears a mailbox's forwarding on the write cards (task 082: the plan,
 * the confirmation, the result). A card shows its own `error` reply under its form (task 120); the toast, with a close
 * button, is for the page's own requests (the sign-in, the version). The write cards' confirmation names the account
 * signed in (`runAs`). A change a write
 * card applied reaches the mailbox access card, which reads the mailbox it shows again (task 091) or marks the user's
 * list it shows as possibly out of date (task 103). The cards follow the design sketch of task 018, a dev-only page: `npm run dev`, then
 * `#/sketch/task-cards`. The top bar stays at the top with the sign-in and the links to the card groups (task 124):
 * Look up, Snapshots and Change, each a section of the one page.
 */
export function App() {
  const { transport, hosted } = useMemo(() => createTransport(), []);
  const client = useMemo(() => new BridgeClient(transport), [transport]);
  const [version, setVersion] = useState<string | null>(null);
  const [signIn, dispatch] = useReducer(signInReducer, { status: "checking" });
  const applied = useMemo(() => new AppliedWrites(), []);
  const runAs = signIn.status === "signed-in" ? signIn.info : undefined;

  const showError = useCallback((error: BridgeError) => {
    toast.error(error.message, { description: error.hint });
  }, []);

  const checkSignIn = useCallback(async () => {
    dispatch({ type: "check" });
    const reply = await client.call(MessageType.whoami);
    dispatch({ type: "reply", reply });
    if (reply.type === MessageType.error) {
      showError(reply.data);
    }
  }, [client, showError]);

  // The sign-in the page waits for: a Cancel, or a newer sign-in, leaves an older one's late reply unread (task 126).
  const signInAttempt = useRef(0);
  const signInWith = useCallback(
    async (deviceCode: boolean) => {
      signInAttempt.current += 1;
      const attempt = signInAttempt.current;
      dispatch({ type: "login" });
      const reply = await client.call(
        MessageType.login,
        deviceCode ? { deviceCode: true } : undefined,
        SIGN_IN_TIMEOUT_MS,
      );
      if (attempt !== signInAttempt.current) {
        return;
      }
      dispatch({ type: "reply", reply });
      if (reply.type === MessageType.error) {
        showError(reply.data);
      }
    },
    [client, showError],
  );

  const cancelSignIn = useCallback(() => {
    signInAttempt.current += 1;
    dispatch({ type: "cancel" });
  }, []);

  // The device-code card's "Open in browser" (task 047): the host opens the page it sent, nothing else.
  const openSignInPage = useCallback(async () => {
    const reply = await client.call(MessageType.openSignInPage);
    if (reply.type === MessageType.error) {
      showError(reply.data);
    }
  }, [client, showError]);

  const signOut = useCallback(async () => {
    dispatch({ type: "logout" });
    const reply = await client.call(MessageType.logout);
    dispatch({ type: "reply", reply });
    if (reply.type === MessageType.error) {
      showError(reply.data);
    } else {
      await checkSignIn();
    }
  }, [client, showError, checkSignIn]);

  useEffect(
    () =>
      client.onNotification((notification) => {
        if (notification.type === MessageType.deviceCode) {
          dispatch({ type: "device-code", prompt: notification.data });
        }
      }),
    [client],
  );

  useEffect(() => {
    client.notify(MessageType.ready);
    void client.call(MessageType.ping).then((reply) => {
      if (reply.type === MessageType.pong) {
        setVersion(reply.data.version);
      }
    });
    void checkSignIn();
  }, [client, checkSignIn]);

  const signedIn = signIn.status === "signed-in";
  const noExchange = exchangeError(signIn);
  return (
    <div className="min-h-screen bg-background text-foreground">
      <TopBar
        version={version}
        hosted={hosted}
        signIn={signIn}
        onSignIn={() => void signInWith(false)}
        onSignOut={() => void signOut()}
      />
      <main className="mx-auto max-w-5xl space-y-8 p-6">
        <section id={signInSection} tabIndex={-1} aria-label="Sign-in" className={cn("scroll-mt-16", focusTarget)}>
          <SignInCard
            state={signIn}
            onRefresh={() => void checkSignIn()}
            onSignIn={(deviceCode) => void signInWith(deviceCode)}
            onSignOut={() => void signOut()}
            onOpenSignInPage={() => void openSignInPage()}
            onCancelSignIn={cancelSignIn}
          />
        </section>
        <Group index={0}>
          <GroupMembersCard client={client} />
          <MailboxAccessCard client={client} applied={applied} />
        </Group>
        <Group index={1}>
          <UserAccessCard client={client} />
          <SnapshotDiffCard client={client} />
        </Group>
        <Group index={2}>
          <MailboxAccessWriteCard
            client={client}
            signedIn={signedIn}
            exchangeError={noExchange}
            runAs={runAs}
            onApplied={applied.publish}
          />
          <MailboxForwardingCard
            client={client}
            signedIn={signedIn}
            exchangeError={noExchange}
            runAs={runAs}
            onApplied={applied.publish}
          />
        </Group>
      </main>
      <Toaster position="bottom-right" closeButton duration={TOAST_MS} />
    </div>
  );
}

/** A group of cards the top bar links to (task 124): a section with the group's id and its name as the heading. */
function Group({ index, children }: { index: number; children: ReactNode }) {
  const { id, name } = pageGroups[index];
  return (
    <section
      id={id}
      tabIndex={-1}
      aria-labelledby={`${id}-heading`}
      className={cn("scroll-mt-16 space-y-4", focusTarget)}
    >
      <h2 id={`${id}-heading`} className="text-sm font-semibold text-muted-foreground">
        {name}
      </h2>
      {children}
    </section>
  );
}
