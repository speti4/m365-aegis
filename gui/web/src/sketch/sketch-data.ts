import type { CardId, Picks, SketchChange, SketchOutcome, SketchPlan, SketchRefusal } from "./task-card-flow";

/**
 * The sketch's made-up data (task 018): three cards, the objects to pick, and the plans and results a core might
 * give. Every name is made up (contoso.com); nothing comes from a tenant or the bridge.
 */

/** Which answer the made-up core gives: the plan and a full result, the core's refusal, or a partial result. */
export type Scenario = "normal" | "refused" | "partial";

export interface FieldOption {
  value: string;
  label: string;
}

export interface CardField {
  name: string;
  label: string;
  /** Several values (checkboxes) or one (a list). */
  multiple: boolean;
  options: readonly FieldOption[];
}

export interface CardDefinition {
  id: CardId;
  title: string;
  /** One sentence: what the card does, for whom. */
  description: string;
  /** The domain, as a badge. */
  domain: "Exchange Online" | "Groups";
  /** A write goes through the plan and a confirmation; a check only reads. */
  writes: boolean;
  fields: readonly CardField[];
}

const users: readonly FieldOption[] = [
  { value: "alice@contoso.com", label: "Alice Example" },
  { value: "bob@contoso.com", label: "Bob Example" },
  { value: "carol@contoso.com", label: "Carol Example" },
];

export const cards: readonly CardDefinition[] = [
  {
    id: "grant-full-access",
    title: "Grant FullAccess on a shared mailbox",
    description: "Let colleagues open a shared mailbox in Outlook; it appears in their folder list.",
    domain: "Exchange Online",
    writes: true,
    fields: [
      {
        name: "mailbox",
        label: "Shared mailbox",
        multiple: false,
        options: [
          { value: "finance@contoso.com", label: "Finance" },
          { value: "reception@contoso.com", label: "Reception" },
        ],
      },
      { name: "users", label: "Colleagues", multiple: true, options: users },
    ],
  },
  {
    id: "add-group-owner",
    title: "Add a group owner",
    description: "Make colleagues owners of a Microsoft 365 group, so they can manage its members.",
    domain: "Groups",
    writes: true,
    fields: [
      {
        name: "group",
        label: "Group",
        multiple: false,
        options: [
          { value: "finance", label: "Finance" },
          { value: "finance-eu", label: "Finance EU" },
        ],
      },
      { name: "owners", label: "New owners", multiple: true, options: users },
    ],
  },
  {
    id: "check-user-access",
    title: "Check a user's access",
    description: "See which shared mailboxes and groups a colleague can use, before or after a change.",
    domain: "Exchange Online",
    writes: false,
    fields: [{ name: "user", label: "Colleague", multiple: false, options: users }],
  },
];

export function cardById(id: CardId): CardDefinition {
  const card = cards.find((candidate) => candidate.id === id);
  if (!card) {
    throw new Error(`no card ${id}`);
  }
  return card;
}

/** The label of a picked value, as the plan names it. */
export function labelOf(card: CardId, field: string, value: string): string {
  return (
    cardById(card)
      .fields.find((candidate) => candidate.name === field)
      ?.options.find((option) => option.value === value)?.label ?? value
  );
}

/** Who already holds FullAccess (made up): alice on Finance, alice and bob on Reception. */
const fullAccess: Record<string, readonly string[]> = {
  "finance@contoso.com": ["alice@contoso.com"],
  "reception@contoso.com": ["alice@contoso.com", "bob@contoso.com"],
};

/** Who already owns a group (made up): alice owns Finance. */
const owners: Record<string, readonly string[]> = { finance: ["alice@contoso.com"], "finance-eu": [] };

/** The plan a core might compute for the picks; with the `refused` scenario, the refusal instead. */
export function planFor(card: CardId, picks: Picks, scenario: Scenario): SketchPlan | SketchRefusal {
  if (scenario === "refused") {
    return refusalFor(card);
  }
  switch (card) {
    case "grant-full-access": {
      const mailbox = picks.mailbox?.[0] ?? "";
      const mailboxName = labelOf(card, "mailbox", mailbox);
      const already = fullAccess[mailbox] ?? [];
      const wanted = picks.users ?? [];
      const changes: SketchChange[] = wanted
        .filter((user) => !already.includes(user))
        .map((user) => ({
          action: "add",
          target: mailboxName,
          detail: `FullAccess for ${labelOf(card, "users", user)}, shown in their Outlook (automapping)`,
        }));
      return {
        title: `Grant FullAccess on ${mailboxName}`,
        changes,
        unchanged: wanted
          .filter((user) => already.includes(user))
          .map((user) => `${labelOf(card, "users", user)} already holds FullAccess on ${mailboxName}`),
        warnings:
          changes.length > 0
            ? ["Outlook shows the mailbox within about an hour; Send As is not part of this card."]
            : [],
        readOnly: false,
      };
    }
    case "add-group-owner": {
      const group = picks.group?.[0] ?? "";
      const groupName = labelOf(card, "group", group);
      const already = owners[group] ?? [];
      const wanted = picks.owners ?? [];
      const changes: SketchChange[] = wanted
        .filter((user) => !already.includes(user))
        .map((user) => ({ action: "add", target: groupName, detail: `${labelOf(card, "owners", user)} as an owner` }));
      return {
        title: `Add owners to ${groupName}`,
        changes,
        unchanged: wanted
          .filter((user) => already.includes(user))
          .map((user) => `${labelOf(card, "owners", user)} already owns ${groupName}`),
        warnings: changes.length > 0 ? ["An owner who is not a member is not added as a member."] : [],
        readOnly: false,
      };
    }
    case "check-user-access": {
      const user = picks.user?.[0] ?? "";
      const mailboxes = Object.entries(fullAccess)
        .filter(([, holders]) => holders.includes(user))
        .map(([mailbox]) => `FullAccess on ${labelOf("grant-full-access", "mailbox", mailbox)}`);
      const groups = Object.entries(owners)
        .filter(([, holders]) => holders.includes(user))
        .map(([group]) => `Owner of ${labelOf("add-group-owner", "group", group)}`);
      const answer = [...mailboxes, ...groups];
      return {
        title: `What ${labelOf(card, "user", user)} can use`,
        changes: [],
        unchanged: [],
        warnings: [],
        readOnly: true,
        answer: answer.length > 0 ? answer : ["No shared mailbox and no group ownership."],
      };
    }
  }
}

function refusalFor(card: CardId): SketchRefusal {
  return card === "add-group-owner"
    ? {
        code: "forbidden",
        message: "the service refused: the signed-in account may not change this group's owners (a made-up refusal)",
        hint: "ask an administrator for the Groups Administrator role, or for the change itself",
      }
    : {
        code: "forbidden",
        message:
          "the service refused: the signed-in account has no Exchange role that reads or grants mailbox permissions (a made-up refusal)",
        hint: "ask an administrator for the Recipient Management role",
      };
}

/** What applying the plan might do: every change done, or (the `partial` scenario) the last one failed. */
export function outcomeFor(plan: SketchPlan, scenario: Scenario): SketchOutcome {
  const items = plan.changes.map((change, index) => {
    const fails = scenario === "partial" && index === plan.changes.length - 1;
    return fails
      ? {
          target: change.target,
          detail: change.detail,
          ok: false,
          message: "the service was busy and the retries ran out (a made-up failure); try this one again later",
        }
      : { target: change.target, detail: change.detail, ok: true };
  });
  return { status: items.every((item) => item.ok) ? "done" : "partial", items };
}

export function isRefusal(value: SketchPlan | SketchRefusal): value is SketchRefusal {
  return "code" in value;
}
