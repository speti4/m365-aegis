import type { BridgeError, ExchangeLayer, MailboxRight } from "../bridge/protocol";
import { describeTaken } from "./snapshot-time";

/**
 * The window's vocabulary (task 122): the one place where a value of the core becomes the words a support colleague
 * reads, so two cards never name one thing two ways. Microsoft's own terms where it has them (Full Access, Send As,
 * Send on Behalf; Shared mailbox; Microsoft 365, Security, Mail-enabled security, Distribution list; Free/busy only), a
 * permission never a "right" or a "grant", sign-in name, email address and object ID for people and objects, the card's
 * verbs from the choice to the plan's badge and the confirmation's button (Grant, Revoke, Forward, Stop forwarding).
 * A value a map does not know is shown as the core wrote it, so a new one still reaches the card.
 */

/** A count with its noun, singular for one by the English plural rules: "1 change", "4 changes", "2 mailboxes". */
export function plural(n: number, one: string, many = pluralOf(one)): string {
  return `${n} ${englishPlurals.select(n) === "one" ? one : many}`;
}

const englishPlurals = new Intl.PluralRules("en-US");

/** The regular plural of a noun: "mailbox" → "mailboxes", "change" → "changes". */
function pluralOf(noun: string): string {
  return /(s|x|z|ch|sh)$/.test(noun) ? `${noun}es` : `${noun}s`;
}

/**
 * A sentence from the core's lower-case fragment: the first letter up and a closing period. A fragment that opens on
 * an address, a path or a quoted name keeps its first letter ("bob@contoso.com is…", "'Fin' matches…").
 */
export function sentence(text: string): string {
  const trimmed = text.trim();
  if (trimmed === "") {
    return trimmed;
  }
  const ended = /[.!?…]$/.test(trimmed) ? trimmed : `${trimmed}.`;
  return capitalize(ended);
}

/** The first letter up, unless the text opens on an address, a path or a quoted name. */
export function capitalize(text: string): string {
  const first = text.split(/\s/, 1)[0];
  return /[@/]/.test(first) || /^['"`]/.test(first) ? text : `${text.charAt(0).toUpperCase()}${text.slice(1)}`;
}

/** A value by its map, or as the core wrote it when the map does not know it. */
function shown(names: Readonly<Record<string, string>>, value: string): string {
  return Object.hasOwn(names, value) ? names[value] : value;
}

/** The mailbox permissions as Microsoft Learn and the Exchange admin center write them. */
export const rightNames: Readonly<Record<MailboxRight, string>> = {
  "full-access": "Full Access",
  "send-as": "Send As",
  "send-on-behalf": "Send on Behalf",
};

/** A mailbox permission by the bridge's word for it, or the core's word when it is none. */
export function rightName(right: string): string {
  return shown(rightNames, right);
}

/** The layers a mailbox permission can be read through, under the access card's "Details" line (tasks 063 and 126). */
export const layerNames: Readonly<Record<ExchangeLayer, string>> = {
  "admin-api": "the Exchange admin API",
  pwsh: "Exchange Online PowerShell",
};

/** The mailbox types (`RecipientTypeDetails`) as the Exchange admin center names them. */
const mailboxTypeNames: Readonly<Record<string, string>> = {
  UserMailbox: "User mailbox",
  SharedMailbox: "Shared mailbox",
  RoomMailbox: "Room mailbox",
  EquipmentMailbox: "Equipment mailbox",
  SchedulingMailbox: "Scheduling mailbox",
  LinkedMailbox: "Linked mailbox",
};

export function mailboxTypeName(type: string): string {
  return shown(mailboxTypeNames, type);
}

/**
 * The group types as the admin centers name them, by the bridge's word (`microsoft365`, the group lookup's) and the
 * snapshot's (`Microsoft365`, the access view's), so the two cards agree.
 */
const groupKindNames: Readonly<Record<string, string>> = {
  microsoft365: "Microsoft 365",
  Microsoft365: "Microsoft 365",
  security: "Security",
  Security: "Security",
  "mail-enabled-security": "Mail-enabled security",
  MailEnabledSecurity: "Mail-enabled security",
  distribution: "Distribution list",
  Distribution: "Distribution list",
};

export function groupKindName(kind: string): string {
  return shown(groupKindNames, kind);
}

/** A directory object's type (a group member's, Graph's `@odata.type` without its namespace). */
const objectTypeNames: Readonly<Record<string, string>> = {
  user: "User",
  group: "Group",
  device: "Device",
  orgContact: "Contact",
  servicePrincipal: "App",
  external: "Guest",
  recipient: "Recipient",
};

export function objectTypeName(type: string): string {
  return shown(objectTypeNames, type);
}

/** How a user holds a group or the site behind it. */
const roleNames: Readonly<Record<string, string>> = {
  member: "Member",
  owner: "Owner",
};

export function roleName(role: string): string {
  return shown(roleNames, role);
}

/** A sharing link's audience, as SharePoint's share dialog names it. */
const sharingScopes: Readonly<Record<string, string>> = {
  anonymous: "anyone with the link",
  organization: "people in your organization",
  users: "specific people",
  existingAccess: "people with existing access",
};

/** A sharing role, as SharePoint names it. */
const sharingRoles: Readonly<Record<string, string>> = {
  read: "View",
  write: "Edit",
  owner: "Owner",
  review: "Review",
};

/** A shared item's permission: a link or a direct grant, its audience, roles, expiry and password. */
export interface SharingAccess {
  kind: string;
  scope?: string;
  roles?: string;
  expires?: string;
  password?: boolean;
}

/**
 * A shared item's permission in SharePoint's words: "Link: people in your organization · View", "Shared directly ·
 * Edit", with the expiry and the password after it.
 */
export function sharingAccess({ kind, scope, roles, expires, password }: SharingAccess): string {
  const how =
    kind === "link"
      ? scope
        ? `Link: ${shown(sharingScopes, scope)}`
        : "Link"
      : kind === "direct"
        ? "Shared directly"
        : kind;
  const parts = [
    how,
    ...(roles
      ? [
          roles
            .split(",")
            .map((role) => shown(sharingRoles, role.trim()))
            .join(", "),
        ]
      : []),
    ...(expires ? [`until ${expires}`] : []),
    ...(password ? ["with a password"] : []),
  ];
  return parts.join(" · ");
}

/**
 * The calendar roles as Outlook names them; a role Outlook names in one word (Owner, Editor, Author, Reviewer,
 * Contributor, None) stays as it is.
 */
const calendarRoles: Readonly<Record<string, string>> = {
  AvailabilityOnly: "Free/busy only",
  LimitedDetails: "Free/busy, titles and locations",
  PublishingEditor: "Publishing editor",
  PublishingAuthor: "Publishing author",
  NonEditingAuthor: "Nonediting author",
};

/** A calendar permission's roles (the core writes several with commas, a delegate's flag among them). */
export function calendarRole(rights: string): string {
  return rights
    .split(",")
    .map((right) => shown(calendarRoles, right.trim()))
    .join(", ");
}

/** A forwarding's kind and flags as a snapshot diff writes them (`smtp-address, external, keeps a copy`). */
const forwardingWords: Readonly<Record<string, string>> = {
  "smtp-address": "To an email address",
  recipient: "To a recipient",
  external: "outside your organization",
  "keeps a copy": "keeps a copy",
};

/**
 * How a row of a snapshot diff holds what it holds, in the window's words: the core writes it for the command line
 * ("member (device)", "owner (graph)", "link organization read", "full-access", "AvailabilityOnly", "smtp-address,
 * external"), the card reads it by the part the row belongs to. A value it cannot read is shown as the core wrote it.
 */
export function diffHow(part: string, how: string): string {
  switch (part) {
    case "members":
    case "groups":
    case "owners":
    case "sites": {
      const member = /^(member|owner)(?: \((.+)\))?$/.exec(how);
      if (!member) {
        return how;
      }
      // An owner's source (`graph`, `exchange`) is where the take read it, not how the user holds the group.
      return member[1] === "member" && member[2]
        ? `${roleName(member[1])} (${objectTypeName(member[2]).toLowerCase()})`
        : roleName(member[1]);
    }
    case "sharing":
    case "sharedItems":
      return sharingHow(how);
    case "grants":
    case "mailboxes":
      return rightName(how);
    case "folderPermissions":
    case "calendars":
      return calendarRole(how);
    case "forwarding":
      return how
        .split(", ")
        .map((word) => shown(forwardingWords, word))
        .join(", ");
    default:
      return how;
  }
}

/** A sharing row of a snapshot diff (`link organization read until 2026-12-31 with a password`) as `sharingAccess`. */
function sharingHow(how: string): string {
  const parsed =
    /^(link|direct)(?: (anonymous|organization|users|existingAccess))?(?: ([\w,.]+))?(?: until (\S+))?( with a password)?$/.exec(
      how,
    );
  if (!parsed) {
    return how;
  }
  const [, kind, scope, roles, expires, password] = parsed;
  return sharingAccess({ kind, scope, roles, expires, password: password !== undefined });
}

/**
 * A plan change's badge by the plan's operation (task 122): the card's own verb, the one its choice and its
 * confirmation's button use. An operation the list does not know names the change's action in a word.
 */
const changeVerbs: Readonly<Record<string, string>> = {
  "mailbox-access-grant": "Grant",
  "mailbox-access-revoke": "Revoke",
  "mailbox-forwarding-set": "Forward",
  "mailbox-forwarding-clear": "Stop forwarding",
};

const actionWords: Readonly<Record<string, string>> = {
  add: "Add",
  remove: "Remove",
  set: "Set",
  clear: "Clear",
  send: "Send",
};

export function changeVerb(operation: string, action: string): string {
  return Object.hasOwn(changeVerbs, operation) ? changeVerbs[operation] : shown(actionWords, action);
}

/**
 * A snapshot by the time in its file name (`2026-10-31T060000Z.sqlite`, the store's), as the cards name a snapshot:
 * "2026-10-31 06:00 UTC"; a name that is no time stays as it is.
 */
export function snapshotTime(name: string): string {
  const time = /^(\d{4}-\d{2}-\d{2})T(\d{2})(\d{2})(\d{2})Z(?:\.sqlite)?$/.exec(name);
  return time ? describeTaken(`${time[1]}T${time[2]}:${time[3]}:${time[4]}Z`) : name;
}

interface ErrorRewording {
  /** The core's sentence and hint, their variable parts captured in order (the message's first). */
  message: RegExp;
  hint?: RegExp;
  window: (...parts: string[]) => { message: string; hint?: string };
}

/**
 * The core's errors that the command line shares with the window, in the window's words (task 122): the command line
 * keeps its own (its tests pin them, and a group's object id is what a command line takes), the card says what to
 * enter in its field. An error this list does not know is shown as the core wrote it, with the command line's
 * terms swapped for the window's (`windowError`).
 */
const errorRewordings: readonly ErrorRewording[] = [
  {
    // GroupLookups, a name that finds no group.
    message: /^no group named '(.+)'$/s,
    window: (name) => ({
      message: `No group named '${name}'`,
      hint: "Check the spelling, or enter the group's email address, its object ID, or one word of its name.",
    }),
  },
  {
    // GroupLookups, a word that names several groups: the hint lists them by name and object id.
    message: /^'(.+)' names (\d+) groups(, and maybe more)?$/s,
    hint: /^give the object id of one: (.+)$/s,
    window: (name, n, more, list) => ({
      message: `'${name}' matches ${more ? `${n} or more` : n} groups: ${list.replace(/ \([0-9a-fA-F-]{36}\)/g, "")}.`,
      hint: "Enter the full name or the email address of the one you want.",
    }),
  },
  {
    // MailboxWrites, a holder no recipient answers to.
    message: /^recipient '(.+)' not found(.*)$/s,
    window: (name) => ({
      message: `No one named '${name}' was found`,
      hint: "Check the spelling, or enter the person's sign-in name or email address. Nothing was sent.",
    }),
  },
  {
    // MailboxWrites, a forwarding address that is none.
    message: /^the forwarding address: '(.+)' is not an address$/s,
    window: (value) => ({
      message: `'${value}' is not an email address`,
      hint: "Enter the email address to forward to, such as bob@fabrikam.com. Nothing was sent.",
    }),
  },
];

/**
 * The command line's terms in a sentence of the core, as the window says them: "enter" for "give", the sign-in name,
 * the object ID, Full Access, and a snapshot by its time rather than its file.
 */
function windowTerms(text: string): string {
  return text
    .replace(/^give /, "enter ")
    .replace(
      /\bsnapshot (\d{4}-\d{2}-\d{2}T\d{6}Z(?:\.sqlite)?)/g,
      (_, name: string) => `snapshot of ${snapshotTime(name)}`,
    )
    .replace(/\b\d{4}-\d{2}-\d{2}T\d{6}Z\.sqlite\b/g, (name) => `the snapshot of ${snapshotTime(name)}`)
    .replace(/\buser principal name(s?)\b/g, "sign-in name$1")
    .replace(/\bobject id(s?)\b/g, "object ID$1")
    .replace(/\bFullAccess\b/g, "Full Access");
}

/**
 * An error as a card or a toast shows it: the window's sentence and hint for one the command line shares, else the
 * core's with the window's terms; each a sentence (`sentence`).
 */
export function windowError(error: BridgeError): { message: string; hint?: string } {
  for (const rewording of errorRewordings) {
    const message = rewording.message.exec(error.message);
    const hint = rewording.hint ? rewording.hint.exec(error.hint ?? "") : null;
    if (message && (!rewording.hint || hint)) {
      const parts = [...message.slice(1), ...(hint ? hint.slice(1) : [])].map((part) => part ?? "");
      const worded = rewording.window(...parts);
      return { message: sentence(worded.message), hint: worded.hint };
    }
  }
  return {
    message: sentence(windowTerms(error.message)),
    hint: error.hint ? sentence(windowTerms(error.hint)) : undefined,
  };
}
