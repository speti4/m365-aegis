/**
 * The message protocol between the front end and the host (M365Aegis.Core.Bridge in C#): one JSON object per
 * message, tagged with the protocol version. A request carries an id the answer repeats; a notification (such as
 * `ready`) carries none. The samples under tests/contracts/bridge are read by the C# and the Vitest tests alike.
 */

export const PROTOCOL_VERSION = 1;

export interface BridgeMessage {
  aegis: number;
  id?: string;
  type: string;
  data?: unknown;
}

/** Parses a line from the host; null when it is not a message of this protocol. */
export function parseMessage(text: string): BridgeMessage | null {
  if (!text?.trim()) {
    return null;
  }
  let value: unknown;
  try {
    value = JSON.parse(text);
  } catch {
    return null;
  }
  if (typeof value !== "object" || value === null) {
    return null;
  }
  const candidate = value as Record<string, unknown>;
  if (candidate.aegis !== PROTOCOL_VERSION || typeof candidate.type !== "string" || candidate.type === "") {
    return null;
  }
  return {
    aegis: PROTOCOL_VERSION,
    id: typeof candidate.id === "string" ? candidate.id : undefined,
    type: candidate.type,
    data: candidate.data,
  };
}

/** A request with a fresh id. */
export function request(type: string, data?: unknown, id: string = nextId()): BridgeMessage {
  return data === undefined ? { aegis: PROTOCOL_VERSION, id, type } : { aegis: PROTOCOL_VERSION, id, type, data };
}

/** A notification: no id, no answer expected. */
export function notification(type: string, data?: unknown): BridgeMessage {
  return data === undefined ? { aegis: PROTOCOL_VERSION, type } : { aegis: PROTOCOL_VERSION, type, data };
}

/** A message as the one line of JSON the host reads. */
export function writeMessage(message: BridgeMessage): string {
  return JSON.stringify(message);
}

let counter = 0;
function nextId(): string {
  counter += 1;
  return String(counter);
}
