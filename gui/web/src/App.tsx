import { useCallback, useEffect, useMemo, useReducer, useState } from "react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Toaster } from "@/components/ui/sonner";
import { BridgeClient, SIGN_IN_TIMEOUT_MS } from "./bridge/client";
import { type BridgeError, MessageType } from "./bridge/protocol";
import { createTransport } from "./bridge/transport";
import { GroupMembersCard } from "./components/group-members-card";
import { MailboxAccessCard } from "./components/mailbox-access-card";
import { SignInCard } from "./components/sign-in-card";
import { signInReducer } from "./components/sign-in-state";

/**
 * The window's page: it tells the host it is ready (the smoke run waits for that), shows the sign-in state and
 * signs in or out from its card (task 040), looks up a group's members (task 017) and a mailbox's access over the
 * window's Exchange Online session (task 045); an `error` reply is a toast. The task cards of M4 grow from here
 * (their design sketch, task 018, is a dev-only page: `npm run dev`, then `#/sketch/task-cards`).
 */
export function App() {
  const { transport, hosted } = useMemo(() => createTransport(), []);
  const client = useMemo(() => new BridgeClient(transport), [transport]);
  const [version, setVersion] = useState<string | null>(null);
  const [signIn, dispatch] = useReducer(signInReducer, { status: "checking" });

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

  const signInWith = useCallback(
    async (deviceCode: boolean) => {
      dispatch({ type: "login" });
      const reply = await client.call(
        MessageType.login,
        deviceCode ? { deviceCode: true } : undefined,
        SIGN_IN_TIMEOUT_MS,
      );
      dispatch({ type: "reply", reply });
      if (reply.type === MessageType.error) {
        showError(reply.data);
      }
    },
    [client, showError],
  );

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

  return (
    <main className="min-h-screen bg-background text-foreground">
      <div className="mx-auto max-w-5xl space-y-6 p-6">
        <header className="flex flex-wrap items-baseline gap-3">
          <h1 className="text-2xl font-semibold tracking-tight">m365-aegis</h1>
          <span className="text-sm text-muted-foreground" data-testid="version">
            {version ?? "…"}
          </span>
          {hosted ? null : <Badge variant="outline">browser mock: made-up data, no host</Badge>}
        </header>
        <SignInCard
          state={signIn}
          onRefresh={() => void checkSignIn()}
          onSignIn={(deviceCode) => void signInWith(deviceCode)}
          onSignOut={() => void signOut()}
          onOpenSignInPage={() => void openSignInPage()}
        />
        <GroupMembersCard client={client} onError={showError} />
        <MailboxAccessCard client={client} onError={showError} />
      </div>
      <Toaster position="bottom-right" />
    </main>
  );
}
