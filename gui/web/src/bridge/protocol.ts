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
  /** Opens that page in the default browser (no payload: the host opens only the page it sent): answered with `sign-in-page-opened`. */
  openSignInPage: "open-sign-in-page",
  signInPageOpened: "sign-in-page-opened",
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
  /** The write requests (task 082): each is answered with `plan`, the plan as `--dry-run` prints it; nothing is sent. */
  mailboxAccessGrant: "mailbox-access-grant",
  mailboxAccessRevoke: "mailbox-access-revoke",
  mailboxForwardingSet: "mailbox-forwarding-set",
  mailboxForwardingClear: "mailbox-forwarding-clear",
  plan: "plan",
  /** `{ planId, confirm: true }`: applies a plan the host holds, as `--yes` does; answered with `plan-applied`. */
  applyPlan: "apply-plan",
  planApplied: "plan-applied",
  /** A request that failed: the code, the message and the hint. */
  error: "error",
} as const;

/** The write requests, as `BridgeMessageType.Writes` marks them in C#: each one plans, only `apply-plan` sends. */
export const writeRequests: readonly string[] = [
  MessageType.mailboxAccessGrant,
  MessageType.mailboxAccessRevoke,
  MessageType.mailboxForwardingSet,
  MessageType.mailboxForwardingClear,
];

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

/** The answer to `open-sign-in-page`: the page the default browser was asked to open. */
export interface SignInPageOpened {
  verificationUri: string;
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

/** The layer of Exchange Online that read a mailbox or a grant (task 063): the Admin API or the pwsh sidecar. */
export type ExchangeLayer = "admin-api" | "pwsh";

export interface MailboxHolder {
  right: MailboxRight;
  holder: string;
  address?: string;
  /** The layer that read the grant: `admin-api` for a Send on Behalf delegate by name, `pwsh` for the rest. */
  layer?: ExchangeLayer;
}

/**
 * The result of `mailbox access <mailbox>`; `mailboxLayer` says which layer read the mailbox and its Send on Behalf
 * list: `admin-api`, or `pwsh` when the Admin API could not answer or the window has none.
 */
export interface MailboxAccess {
  mailbox: MailboxInfo;
  holders: MailboxHolder[];
  mailboxLayer?: ExchangeLayer;
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

/** The payload of `mailbox-access-grant` and `mailbox-access-revoke`: the arguments of `mailbox access grant|revoke`. */
export interface MailboxAccessWrite {
  mailbox: string;
  users: string[];
  rights: MailboxRight[];
  /** A grant's FullAccess without automapping (`--no-automapping`). */
  noAutoMapping?: boolean;
}

/** The payload of `mailbox-forwarding-set` (`to`, `keepCopy`) and `mailbox-forwarding-clear` (the mailbox alone). */
export interface MailboxForwardingWrite {
  mailbox: string;
  to?: string;
  keepCopy?: boolean;
}

export type PlanAction = "add" | "remove" | "set" | "clear" | "send";

/** Where a plan stands (`PlanMode` of the core): printed, refused, being applied, applied, or nothing to change. */
export type PlanMode = "dry-run" | "refused" | "applying" | "applied" | "up-to-date";

export type ChangeStatus = "applied" | "failed" | "skipped";

/** What became of one change of an applied plan; `error` for a failed one. */
export interface ChangeResult {
  status: ChangeStatus;
  error?: BridgeError;
}

/** One change of a plan: what changes on which object, from what to what. */
export interface PlanChange {
  action: PlanAction;
  target: string;
  detail: string;
  current: string;
  desired: string;
  row?: number;
  index?: number;
  key?: string;
  result?: ChangeResult;
}

/** Something already as asked, left alone. */
export interface PlanUnchanged {
  target: string;
  detail: string;
  state: string;
  row?: number;
  index?: number;
  key?: string;
}

/** A write as a plan (the core's `Plan`, as the command line's `--json` shows it). */
export interface Plan {
  operation: string;
  title: string;
  changes: PlanChange[];
  unchanged: PlanUnchanged[];
  warnings: string[];
  mode: PlanMode;
  /** The plan record's file, once applied. */
  record?: string;
  started?: string;
  finished?: string;
}

/** The answer to a write request: the plan, and the id `apply-plan` names (none when there is nothing to change). */
export interface PlanReady {
  planId?: string;
  plan: Plan;
}

/** The answer to `apply-plan`: the applied plan, and the failure when not every change was applied. */
export interface PlanApplied {
  plan: Plan;
  error?: BridgeError;
}

/** A reply the page can read: its type and its payload, checked. */
export type Reply =
  | { type: typeof MessageType.pong; data: Pong }
  | { type: typeof MessageType.signedIn; data: SignInInfo }
  | { type: typeof MessageType.signedOut; data: SignedOutInfo }
  | { type: typeof MessageType.members; data: GroupMembers }
  | { type: typeof MessageType.loggedOut; data: SignOutInfo }
  | { type: typeof MessageType.signInPageOpened; data: SignInPageOpened }
  | { type: typeof MessageType.mailboxes; data: MailboxList }
  | { type: typeof MessageType.mailboxHolders; data: MailboxAccess }
  | { type: typeof MessageType.userGrants; data: UserAccess }
  | { type: typeof MessageType.plan; data: PlanReady }
  | { type: typeof MessageType.planApplied; data: PlanApplied }
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
    case MessageType.signInPageOpened:
      return isRecord(data) && isString(data.verificationUri)
        ? { type, data: { verificationUri: data.verificationUri } }
        : null;
    case MessageType.mailboxes:
      return isMailboxList(data) ? { type, data } : null;
    case MessageType.mailboxHolders:
      return isMailboxAccess(data) ? { type, data } : null;
    case MessageType.userGrants:
      return isUserAccess(data) ? { type, data } : null;
    case MessageType.plan:
      return isPlanReady(data) ? { type, data } : null;
    case MessageType.planApplied:
      return isPlanApplied(data) ? { type, data } : null;
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
const layers: readonly string[] = ["admin-api", "pwsh"];

function isOptionalLayer(value: unknown): boolean {
  return value === undefined || (isString(value) && layers.includes(value));
}

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
    isOptionalLayer(value.mailboxLayer) &&
    Array.isArray(value.holders) &&
    value.holders.every(
      (holder) =>
        isRecord(holder) &&
        isString(holder.right) &&
        rights.includes(holder.right) &&
        isString(holder.holder) &&
        isOptionalString(holder.address) &&
        isOptionalLayer(holder.layer),
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

const actions: readonly string[] = ["add", "remove", "set", "clear", "send"];
const modes: readonly string[] = ["dry-run", "refused", "applying", "applied", "up-to-date"];
const statuses: readonly string[] = ["applied", "failed", "skipped"];

function isOptionalNumber(value: unknown): boolean {
  return value === undefined || typeof value === "number";
}

function isChangeResult(value: unknown): boolean {
  return (
    isRecord(value) &&
    isString(value.status) &&
    statuses.includes(value.status) &&
    (value.error === undefined || isBridgeError(value.error))
  );
}

function isPlanChange(value: unknown): boolean {
  return (
    isRecord(value) &&
    isString(value.action) &&
    actions.includes(value.action) &&
    isString(value.target) &&
    isString(value.detail) &&
    isString(value.current) &&
    isString(value.desired) &&
    isOptionalNumber(value.row) &&
    (value.result === undefined || isChangeResult(value.result))
  );
}

function isPlanUnchanged(value: unknown): boolean {
  return (
    isRecord(value) &&
    isString(value.target) &&
    isString(value.detail) &&
    isString(value.state) &&
    isOptionalNumber(value.row)
  );
}

function isPlan(value: unknown): value is Plan {
  return (
    isRecord(value) &&
    isString(value.operation) &&
    isString(value.title) &&
    isString(value.mode) &&
    modes.includes(value.mode) &&
    isOptionalString(value.record) &&
    Array.isArray(value.changes) &&
    value.changes.every(isPlanChange) &&
    Array.isArray(value.unchanged) &&
    value.unchanged.every(isPlanUnchanged) &&
    Array.isArray(value.warnings) &&
    value.warnings.every(isString)
  );
}

function isPlanReady(value: unknown): value is PlanReady {
  return isRecord(value) && isOptionalString(value.planId) && isPlan(value.plan);
}

function isPlanApplied(value: unknown): value is PlanApplied {
  return isRecord(value) && isPlan(value.plan) && (value.error === undefined || isBridgeError(value.error));
}
