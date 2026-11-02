import { type Ref, useCallback } from "react";
import type { BridgeClient } from "../bridge/client";
import { type AccessUser, MessageType, type UserMatches } from "../bridge/protocol";
import { describeTaken } from "./snapshot-time";
import { DEBOUNCE_MS, type Suggested, SuggestField } from "./suggest-field";
import { plural } from "./vocabulary";

export { DEBOUNCE_MS };

/** How many letters the picker waits for before it suggests: fewer match too many users to help. */
export const SUGGEST_AFTER = 3;

/** What a picked user goes to the card's request as: the user principal name, else the address, else the object id. */
export function pickedAddress(user: AccessUser): string {
  return user.userPrincipalName ?? user.mail ?? user.objectId ?? "";
}

/** What the user search found, as the lines under the list say it. */
type Found = { kind: "users"; text: string; matches: UserMatches } | { kind: "no-snapshot" };

/**
 * The user picker of the access cards (task 112), on the write cards and the access changes card too (task 125): a
 * text field (`SuggestField`) that suggests the users whose name, a word of it, user principal name or address starts
 * with what the colleague types, once three letters are typed (`user-search`): from the directory, live, when the
 * window is signed in, else from the newest snapshot on this computer, and a line under the list says which one
 * answered. A pick hands the user's address to the card (`onPick`); a full address typed by hand, or a user the
 * suggestions do not know, still works. A failed search shows no list: the card's own request reports the error.
 * `multiple` and `enterPicksFirst` are the field's (the Users list of the mailbox rights card, a form of several
 * fields).
 */
export function UserPicker({
  client,
  placeholder = "Start typing a name, or enter a full email address",
  ...field
}: {
  client: BridgeClient;
  id: string;
  inputRef?: Ref<HTMLInputElement>;
  value: string;
  onChange: (value: string) => void;
  onPick?: (address: string) => void;
  busy?: boolean;
  invalid?: boolean;
  describedBy?: string;
  onAnnounce?: (text: string) => void;
  placeholder?: string;
  multiple?: boolean;
  enterPicksFirst?: boolean;
}) {
  const suggest = useCallback(
    async (text: string): Promise<Suggested | null> => {
      const reply = await client.call(MessageType.userSearch, { text });
      switch (reply.type) {
        case MessageType.userMatches:
          return suggested({ kind: "users", text, matches: reply.data });
        case MessageType.noSnapshot:
          return suggested({ kind: "no-snapshot" });
        default:
          return null;
      }
    },
    [client],
  );
  return (
    <SuggestField
      {...field}
      placeholder={placeholder}
      suggest={suggest}
      listLabel="Matching users"
      minLength={SUGGEST_AFTER}
    />
  );
}

/** The users as the list shows them, and its lines: the count for the status region, the rest under the list. */
function suggested(found: Found): Suggested {
  const lines = suggestionLines(found);
  const users = found.kind === "users" ? found.matches.users : [];
  return {
    options: users.map((user) => ({
      value: pickedAddress(user),
      name: user.displayName ?? pickedAddress(user),
      detail: user.displayName ? pickedAddress(user) : undefined,
    })),
    announce: lines[0],
    // The list itself shows how many match.
    note: users.length > 0 ? lines.slice(1) : lines,
  };
}

/** Where the suggestions came from, as the line under the list says it. */
export function describeSource(matches: UserMatches): string {
  if (matches.source === "graph") {
    return "From the directory.";
  }
  const taken = matches.taken ? ` taken ${describeTaken(matches.taken)}` : "";
  return `From the snapshot${taken}: the directory cannot be searched now (signed out, or it did not answer).`;
}

/**
 * The lines under the list: no match, or how many match (for the status region only) and more than shown, then where
 * the users came from; or no source at all. The first line is what the card announces.
 */
function suggestionLines(found: Found): string[] {
  if (found.kind === "no-snapshot") {
    return [
      "No suggestions: the directory cannot be searched now and this computer has no snapshot. Enter the full email address.",
    ];
  }
  const { matches, text } = found;
  const total = matches.users.length;
  const lines = [
    total === 0
      ? `No user starts with "${text}": a full email address still works.`
      : `${plural(total, "matching user")}${matches.more ? ", and more" : ""}: use the arrow keys to choose one.`,
  ];
  if (total > 0 && matches.more) {
    lines.push("More users match: type more of the name.");
  }
  lines.push(describeSource(matches));
  return lines;
}
