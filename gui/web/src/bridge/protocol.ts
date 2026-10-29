/**
 * The message protocol between the front end and the host (M365Aegis.Core.Bridge in C#): one JSON object per
 * message, tagged with the protocol version. A request carries an id the answer repeats; a notification (such as
 * `ready`) carries none. The samples under tests/contracts/bridge are read by the C# and the Vitest tests alike.
 * A reply's payload is the core's result written the way the command line's `--json` envelope writes its data
 * (camelCase, nulls left out, enums as kebab-case strings).
 */

export const PROTOCOL_VERSION = 1;

/** The message types; `BridgeMessageType` in C# names the same strings. */
export const MessageType = {
  /** The page is loaded and listening (a notification; the smoke run waits for it). */
  ready: "ready",
  ping: "ping",
  pong: "pong",
  /** The sign-in state, silently: answered with `signed-in` or `signed-out`. */
  whoami: "whoami",
  signedIn: "signed-in",
  signedOut: "signed-out",
  /** Signs in for the window's profile (silently, else the account picker, else a device code): answered with `signed-in`. */
  login: "login",
  /** A notification from the host while a sign-in waits for the browser step: the page to open and the code. */
  deviceCode: "device-code",
  /** Signs the profile's account out: answered with `logged-out`. */
  logout: "logout",
  loggedOut: "logged-out",
  /** A group's direct members, `{ group }` as the payload: answered with `members`. */
  groupMembers: "group-members",
  members: "members",
  /** The mailboxes, `{ shared }` as the payload: answered with `mailboxes`. */
  mailboxList: "mailbox-list",
  mailboxes: "mailboxes",
  /** `{ mailbox }` (who holds which right on it) or `{ user }` (the mailboxes the user holds a right on). */
  mailboxAccess: "mailbox-access",
  mailboxHolders: "mailbox-holders",
  userGrants: "user-grants",
  /** A request that failed: the code, the message and the hint. */
  error: "error",
} as const;

export interface BridgeMessage {
  aegis: number;
  id?: string;
  type: string;
  data?: unknown;
}

export interface Pong {
  product: string;
  version: string;
}

/** One resource's token: the expiry and the scopes, never the token; `error` when there is none. */
export interface ResourceToken {
  resource: string;
  expiresOn?: string;
  scopes: string[];
  origin?: string;
  error?: string;
}

/** The signed-in account (`SignInInfo` of the core, as `whoami --json` shows it). */
export interface SignInInfo {
  profile: string;
  account: string;
  displayName?: string;
  tenantId?: string;
  objectId?: string;
  tokens: ResourceToken[];
}

/** No usable silent sign-in: the profile it was tried for, if there is one, why, and what to do. */
export interface SignedOutInfo {
  profile?: string;
  account?: string;
  message: string;
  hint?: string;
}

export type GroupKind = "microsoft365" | "security" | "mail-enabled-security" | "distribution";

export interface GroupInfo {
  id: string;
  displayName: string;
  kind: GroupKind;
  mail?: string;
  synced: boolean;
}

export interface GroupMember {
  type: string;
  displayName?: string;
  userPrincipalName?: string;
  mail?: string;
  id: string;
}

/** The result of `group members`. */
export interface GroupMembers {
  group: GroupInfo;
  members: GroupMember[];
}

/** The page and the code of a device-code sign-in (`DeviceCodePrompt` of the core). */
export interface DeviceCodePrompt {
  verificationUri: string;
  userCode: string;
  message: string;
  expiresOn: string;
}

/** The result of `logout`: the profile stays, the account's records are gone. */
export interface SignOutInfo {
  profile: string;
  account: string;
  removedAccounts: number;
}

export interface MailboxInfo {
  displayName: string;
  primarySmtpAddress: string;
  type: string;
  userPrincipalName?: string;
  objectId?: string;
}

/** The result of `mailbox list [--shared]`. */
export interface MailboxList {
  mailboxes: MailboxInfo[];
  sharedOnly: boolean;
}

export type MailboxRight = "full-access" | "send-as" | "send-on-behalf";

export interface MailboxHolder {
  right: MailboxRight;
  holder: string;
  address?: string;
}

/** The result of `mailbox access <mailbox>`. */
export interface MailboxAccess {
  mailbox: MailboxInfo;
  holders: MailboxHolder[];
}

export interface UserGrant {
  right: MailboxRight;
  mailbox: MailboxInfo;
}

/** The result of `mailbox access --user <upn>`; `failed` lists what could not be read (a partial answer). */
export interface UserAccess {
  user: string;
  displayName?: string;
  grants: UserGrant[];
  mailboxesScanned: number;
  failed: string[];
}

/** A failure as the envelope's `error` carries it: a stable code, one sentence, and what to do about it. */
export interface BridgeError {
  code: string;
  message: string;
  hint?: string;
}

/** A reply the page can read: its type and its payload, checked. */
export type Reply =
  | { type: typeof MessageType.pong; data: Pong }
  | { type: typeof MessageType.signedIn; data: SignInInfo }
  | { type: typeof MessageType.signedOut; data: SignedOutInfo }
  | { type: typeof MessageType.members; data: GroupMembers }
  | { type: typeof MessageType.loggedOut; data: SignOutInfo }
  | { type: typeof MessageType.mailboxes; data: MailboxList }
  | { type: typeof MessageType.mailboxHolders; data: MailboxAccess }
  | { type: typeof MessageType.userGrants; data: UserAccess }
  | { type: typeof MessageType.error; data: BridgeError };

/** A notification of the host the page can read. */
export type HostNotification = { type: typeof MessageType.deviceCode; data: DeviceCodePrompt };

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

/** The reply's type and payload when the payload has the shape its type promises; null otherwise. */
export function readReply(message: BridgeMessage): Reply | null {
  const { type, data } = message;
  switch (type) {
    case MessageType.pong:
      return isPong(data) ? { type, data } : null;
    case MessageType.signedIn:
      return isSignInInfo(data) ? { type, data } : null;
    case MessageType.signedOut:
      return isSignedOutInfo(data) ? { type, data } : null;
    case MessageType.members:
      return isGroupMembers(data) ? { type, data } : null;
    case MessageType.loggedOut:
      return isSignOutInfo(data) ? { type, data } : null;
    case MessageType.mailboxes:
      return isMailboxList(data) ? { type, data } : null;
    case MessageType.mailboxHolders:
      return isMailboxAccess(data) ? { type, data } : null;
    case MessageType.userGrants:
      return isUserAccess(data) ? { type, data } : null;
    case MessageType.error:
      return isBridgeError(data) ? { type, data } : null;
    default:
      return null;
  }
}

/** A notification of the host (no id) when its payload has the shape its type promises; null otherwise. */
export function readNotification(message: BridgeMessage): HostNotification | null {
  if (message.id !== undefined) {
    return null;
  }
  const { type, data } = message;
  return type === MessageType.deviceCode && isDeviceCodePrompt(data) ? { type, data } : null;
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

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isString(value: unknown): value is string {
  return typeof value === "string";
}

function isOptionalString(value: unknown): boolean {
  return value === undefined || typeof value === "string";
}

function isPong(value: unknown): value is Pong {
  return isRecord(value) && isString(value.product) && isString(value.version);
}

function isSignedOutInfo(value: unknown): value is SignedOutInfo {
  return (
    isRecord(value) &&
    isString(value.message) &&
    isOptionalString(value.profile) &&
    isOptionalString(value.account) &&
    isOptionalString(value.hint)
  );
}

function isBridgeError(value: unknown): value is BridgeError {
  return isRecord(value) && isString(value.code) && isString(value.message) && isOptionalString(value.hint);
}

function isSignInInfo(value: unknown): value is SignInInfo {
  return (
    isRecord(value) &&
    isString(value.profile) &&
    isString(value.account) &&
    isOptionalString(value.displayName) &&
    Array.isArray(value.tokens) &&
    value.tokens.every(
      (token) =>
        isRecord(token) && isString(token.resource) && Array.isArray(token.scopes) && isOptionalString(token.error),
    )
  );
}

const groupKinds: readonly string[] = ["microsoft365", "security", "mail-enabled-security", "distribution"];

function isGroupMembers(value: unknown): value is GroupMembers {
  if (!isRecord(value) || !isRecord(value.group) || !Array.isArray(value.members)) {
    return false;
  }
  const { group, members } = value;
  return (
    isString(group.id) &&
    isString(group.displayName) &&
    isString(group.kind) &&
    groupKinds.includes(group.kind) &&
    typeof group.synced === "boolean" &&
    members.every((member) => isRecord(member) && isString(member.type) && isString(member.id))
  );
}

function isDeviceCodePrompt(value: unknown): value is DeviceCodePrompt {
  return (
    isRecord(value) &&
    isString(value.verificationUri) &&
    isString(value.userCode) &&
    isString(value.message) &&
    isString(value.expiresOn)
  );
}

function isSignOutInfo(value: unknown): value is SignOutInfo {
  return (
    isRecord(value) && isString(value.profile) && isString(value.account) && typeof value.removedAccounts === "number"
  );
}

const rights: readonly string[] = ["full-access", "send-as", "send-on-behalf"];

function isMailboxInfo(value: unknown): value is MailboxInfo {
  return (
    isRecord(value) &&
    isString(value.displayName) &&
    isString(value.primarySmtpAddress) &&
    isString(value.type) &&
    isOptionalString(value.userPrincipalName) &&
    isOptionalString(value.objectId)
  );
}

function isMailboxList(value: unknown): value is MailboxList {
  return (
    isRecord(value) &&
    typeof value.sharedOnly === "boolean" &&
    Array.isArray(value.mailboxes) &&
    value.mailboxes.every(isMailboxInfo)
  );
}

function isMailboxAccess(value: unknown): value is MailboxAccess {
  return (
    isRecord(value) &&
    isMailboxInfo(value.mailbox) &&
    Array.isArray(value.holders) &&
    value.holders.every(
      (holder) =>
        isRecord(holder) &&
        isString(holder.right) &&
        rights.includes(holder.right) &&
        isString(holder.holder) &&
        isOptionalString(holder.address),
    )
  );
}

function isUserAccess(value: unknown): value is UserAccess {
  return (
    isRecord(value) &&
    isString(value.user) &&
    isOptionalString(value.displayName) &&
    typeof value.mailboxesScanned === "number" &&
    Array.isArray(value.failed) &&
    value.failed.every(isString) &&
    Array.isArray(value.grants) &&
    value.grants.every(
      (grant) =>
        isRecord(grant) && isString(grant.right) && rights.includes(grant.right) && isMailboxInfo(grant.mailbox),
    )
  );
}
