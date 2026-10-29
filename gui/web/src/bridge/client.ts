import {
  type BridgeMessage,
  type HostNotification,
  MessageType,
  notification,
  type Reply,
  readNotification,
  readReply,
  request,
} from "./protocol";
import type { Transport } from "./transport";

/** How long a request waits for its reply: longer than the Graph client's throttling budget of 60 s. */
export const REPLY_TIMEOUT_MS = 120_000;

/**
 * How long a sign-in waits: the account picker waits for the user, and a device code stays valid for about
 * 15 minutes, so the page waits that long before it gives up.
 */
export const SIGN_IN_TIMEOUT_MS = 16 * 60_000;

/**
 * Requests over a transport: each request gets a fresh id and resolves with the reply that repeats it, read and
 * checked (`readReply`). A reply the page cannot read, and a host that does not answer in time, resolve as an
 * `error` reply too, so the page has one error path: the toast. A message without an id is a notification of the
 * host (the device code of a sign-in in flight), handed to the `onNotification` handlers once it reads.
 */
export class BridgeClient {
  private readonly pending = new Map<string, (reply: BridgeMessage) => void>();
  private notificationHandlers: Array<(notification: HostNotification) => void> = [];

  constructor(
    private readonly transport: Transport,
    private readonly timeoutMs = REPLY_TIMEOUT_MS,
  ) {
    transport.onMessage((message) => {
      if (message.id === undefined) {
        const read = readNotification(message);
        if (read) {
          for (const handler of this.notificationHandlers) {
            handler(read);
          }
        }
        return;
      }
      const resolve = this.pending.get(message.id);
      if (resolve) {
        this.pending.delete(message.id);
        resolve(message);
      }
    });
  }

  /** Sends a request and waits for its reply, `timeoutMs` at most (the client's own by default). */
  call(type: string, data?: unknown, timeoutMs: number = this.timeoutMs): Promise<Reply> {
    const message = request(type, data);
    const id = message.id as string;
    return new Promise<Reply>((resolve) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        resolve(failure("timeout", `the window did not answer '${type}' within ${timeoutMs / 1000} s`));
      }, timeoutMs);
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

  /** Registers a handler for the host's notifications; the return value unregisters it. */
  onNotification(handler: (notification: HostNotification) => void): () => void {
    this.notificationHandlers.push(handler);
    return () => {
      this.notificationHandlers = this.notificationHandlers.filter((registered) => registered !== handler);
    };
  }
}

function failure(code: string, message: string): Reply {
  return { type: MessageType.error, data: { code, message } };
}
