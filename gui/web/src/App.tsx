import { useEffect, useMemo, useState } from "react";
import { notification, request } from "./bridge/protocol";
import { createTransport } from "./bridge/transport";

interface Pong {
  product: string;
  version: string;
}

/**
 * The scaffold's page: it tells the host it is ready (the smoke test waits for that), pings it, and shows the
 * answer. The task cards of M4 replace this.
 */
export function App() {
  const { transport, hosted } = useMemo(() => createTransport(), []);
  const [pong, setPong] = useState<Pong | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    transport.onMessage((message) => {
      if (message.type === "pong") {
        setPong(message.data as Pong);
      } else if (message.type === "error") {
        const data = message.data as { message?: string } | undefined;
        setError(data?.message ?? "unknown error");
      }
    });
    transport.send(notification("ready"));
    transport.send(request("ping"));
  }, [transport]);

  return (
    <main className="min-h-screen bg-background text-foreground flex items-center justify-center p-8">
      <section className="w-full max-w-md rounded-xl border border-border bg-card p-6 shadow-sm">
        <h1 className="text-2xl font-semibold tracking-tight">m365-aegis</h1>
        <p className="mt-2 text-sm text-muted-foreground">
          {hosted ? "Connected to the host." : "No host: running in a browser with the mock transport."}
        </p>
        <dl className="mt-6 grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm">
          <dt className="text-muted-foreground">product</dt>
          <dd data-testid="product">{pong?.product ?? "…"}</dd>
          <dt className="text-muted-foreground">version</dt>
          <dd data-testid="version">{pong?.version ?? "…"}</dd>
        </dl>
        {error ? <p className="mt-4 text-sm text-destructive">{error}</p> : null}
      </section>
    </main>
  );
}
