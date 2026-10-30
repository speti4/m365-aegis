import { type BridgeMessage, PROTOCOL_VERSION } from "../bridge/protocol";
import type { Transport } from "../bridge/transport";

/** The answer a test gives to a request: the reply's type and payload, or null to leave it unanswered. */
export type Answer = (message: BridgeMessage) => { type: string; data?: unknown } | null;

/**
 * A transport the test scripts (task 070): it records what the page sends and answers a request with what
 * `answer` returns, on the next turn of the clock, as the host would; `push` delivers a host notification.
 */
export class ScriptedTransport implements Transport {
  readonly sent: BridgeMessage[] = [];
  private handlers: Array<(message: BridgeMessage) => void> = [];

  constructor(private readonly answer: Answer = () => null) {}

  send(message: BridgeMessage): void {
    this.sent.push(message);
    const reply = message.id === undefined ? null : this.answer(message);
    if (reply) {
      setTimeout(() => this.push({ aegis: PROTOCOL_VERSION, id: message.id, ...reply }), 0);
    }
  }

  onMessage(handler: (message: BridgeMessage) => void): () => void {
    this.handlers.push(handler);
    return () => {
      this.handlers = this.handlers.filter((registered) => registered !== handler);
    };
  }

  /** Delivers a message of the host to the page. */
  push(message: BridgeMessage): void {
    for (const handler of this.handlers) {
      handler(message);
    }
  }

  /** The types of the requests and notifications the page sent, in order. */
  get types(): string[] {
    return this.sent.map((message) => message.type);
  }
}
