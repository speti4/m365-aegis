import { useCallback, useEffect, useMemo, useState } from "react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Toaster } from "@/components/ui/sonner";
import { BridgeClient } from "./bridge/client";
import { type BridgeError, MessageType } from "./bridge/protocol";
import { createTransport } from "./bridge/transport";
import { GroupMembersCard } from "./components/group-members-card";
import { SignInCard, type SignInState } from "./components/sign-in-card";

/**
 * The window's first page (task 017): it tells the host it is ready (the smoke run waits for that), shows the
 * sign-in state and looks up a group's members; an `error` reply is a toast. The task cards of M4 grow from here.
 */
export function App() {
  const { transport, hosted } = useMemo(() => createTransport(), []);
  const client = useMemo(() => new BridgeClient(transport), [transport]);
  const [version, setVersion] = useState<string | null>(null);
  const [signIn, setSignIn] = useState<SignInState>({ status: "checking" });

  const showError = useCallback((error: BridgeError) => {
    toast.error(error.message, { description: error.hint });
  }, []);

  const checkSignIn = useCallback(async () => {
    setSignIn({ status: "checking" });
    const reply = await client.call(MessageType.whoami);
    switch (reply.type) {
      case MessageType.signedIn:
        setSignIn({ status: "signed-in", info: reply.data });
        break;
      case MessageType.signedOut:
        setSignIn({ status: "signed-out", info: reply.data });
        break;
      case MessageType.error:
        setSignIn({ status: "unknown" });
        showError(reply.data);
        break;
      default:
        setSignIn({ status: "unknown" });
    }
  }, [client, showError]);

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
        <SignInCard state={signIn} onRefresh={() => void checkSignIn()} />
        <GroupMembersCard client={client} onError={showError} />
      </div>
      <Toaster position="bottom-right" />
    </main>
  );
}
