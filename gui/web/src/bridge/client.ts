import { type BridgeMessage, MessageType, notification, type Reply, readReply, request } from "./protocol";
import type { Transport } from "./transport";

/** How long a request waits for its reply: longer than the Graph client's throttling budget of 60 s. */
export const REPLY_TIMEOUT_MS = 120_000;

/**
 * Requests over a transport: each request gets a fresh id and resolves with the reply that repeats it, read and
 * checked (`readReply`). A reply the page cannot read, and a host that does not answer in time, resolve as an
 * `error` reply too, so the page has one error path: the toast.
 */
export class BridgeClient {
  private readonly pending = new Map<string, (reply: BridgeMessage) => void>();

  constructor(
    private readonly transport: Transport,
    private readonly timeoutMs = REPLY_TIMEOUT_MS,
  ) {
    transport.onMessage((message) => {
      const resolve = message.id === undefined ? undefined : this.pending.get(message.id);
      if (message.id !== undefined && resolve) {
        this.pending.delete(message.id);
        resolve(message);
      }
    });
  }

  /** Sends a request and waits for its reply. */
  call(type: string, data?: unknown): Promise<Reply> {
    const message = request(type, data);
    const id = message.id as string;
    return new Promise<Reply>((resolve) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        resolve(failure("timeout", `the window did not answer '${type}' within ${this.timeoutMs / 1000} s`));
      }, this.timeoutMs);
      this.pending.set(id, (reply) => {
        clearTimeout(timer);
        resolve(
          readReply(reply) ??
            failure("bad-reply", `the window answered '${type}' with a '${reply.type}' the page cannot read`),
        );
      });
      this.transport.send(message);
    });
  }

  /** Sends a notification; nothing comes back. */
  notify(type: string, data?: unknown): void {
    this.transport.send(notification(type, data));
  }
}

function failure(code: string, message: string): Reply {
  return { type: MessageType.error, data: { code, message } };
}
