import { type KeyboardEvent, type Ref, useEffect, useId, useRef, useState } from "react";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import type { BridgeClient } from "../bridge/client";
import { type AccessUser, MessageType, type UserMatches } from "../bridge/protocol";
import { describeTaken } from "./snapshot-time";

/** How many letters the picker waits for before it suggests: fewer match too many users to help. */
export const SUGGEST_AFTER = 3;

/** How long the picker waits after a keystroke before it asks: a quick typist sends one request for a word. */
export const DEBOUNCE_MS = 250;

/** What a picked user goes to the card's request as: the user principal name, else the address, else the object id. */
export function pickedAddress(user: AccessUser): string {
  return user.userPrincipalName ?? user.mail ?? user.objectId ?? "";
}

/** What the list under the field shows: nothing, the matching users, or a line why there is none. */
type Suggestions = { kind: "none" } | { kind: "users"; text: string; matches: UserMatches } | { kind: "no-snapshot" };

/**
 * The user picker of the access cards (task 112): a text field that suggests the users whose name, a word of it, user
 * principal name or address starts with what the colleague types, once three letters are typed (`user-search`): from
 * the directory, live, when the window is signed in, else from the newest snapshot on this computer, and a line under
 * the list says which one answered. A keystroke starts a wait of `DEBOUNCE_MS`, and only
 * the last one asks, so a word typed at speed sends one request; an answer to an older text is dropped. The arrow keys
 * move through the list, Enter or a click picks a user and hands the user's address to the card (`onPick`); Escape
 * closes the list. Enter without a highlighted user submits the card's form with the text as typed, so a full address
 * still works, and so does a user the suggestions do not know. A failed search shows no list: the card's own lookup
 * reports the error.
 *
 * The field is the card's (tasks 120 and 121): its `id` for the card's label, `invalid` and `describedBy` for the
 * card's error, `inputRef` for the card's focus. While the card looks up (`busy`) the field is read-only, not
 * disabled, so the focus stays in it. What the list says (how many users match, none, no source) goes to the card's
 * status region through `onAnnounce`; the field is `expanded` only while the list has a user to pick.
 */
export function UserPicker({
  client,
  id,
  inputRef,
  value,
  onChange,
  onPick,
  busy = false,
  invalid = false,
  describedBy,
  onAnnounce,
  placeholder = "Start typing a name, or a full address",
}: {
  client: BridgeClient;
  id: string;
  inputRef?: Ref<HTMLInputElement>;
  value: string;
  onChange: (value: string) => void;
  onPick: (address: string) => void;
  busy?: boolean;
  invalid?: boolean;
  describedBy?: string;
  onAnnounce?: (text: string) => void;
  placeholder?: string;
}) {
  const listId = useId();
  const [suggestions, setSuggestions] = useState<Suggestions>({ kind: "none" });
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(-1);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  /** The text the latest search asked for: an answer for another text is an old one. */
  const latest = useRef("");

  // A card that starts its lookup cancels the wait and closes the list; leaving the page cancels the wait too.
  useEffect(() => {
    if (busy) {
      clearTimeout(timer.current);
      latest.current = "";
      setOpen(false);
    }
  }, [busy]);
  useEffect(() => () => clearTimeout(timer.current), []);

  async function search(text: string) {
    latest.current = text;
    const reply = await client.call(MessageType.userSearch, { text });
    if (latest.current !== text) {
      return;
    }
    switch (reply.type) {
      case MessageType.userMatches:
        setSuggestions({ kind: "users", text, matches: reply.data });
        onAnnounce?.(suggestionLines({ kind: "users", text, matches: reply.data })[0]);
        break;
      case MessageType.noSnapshot:
        setSuggestions({ kind: "no-snapshot" });
        onAnnounce?.(suggestionLines({ kind: "no-snapshot" })[0]);
        break;
      default:
        setSuggestions({ kind: "none" });
        break;
    }
    setActive(-1);
    setOpen(true);
  }

  function type(text: string) {
    onChange(text);
    clearTimeout(timer.current);
    const wanted = text.trim();
    if (wanted.length < SUGGEST_AFTER) {
      latest.current = "";
      setSuggestions({ kind: "none" });
      setOpen(false);
      return;
    }
    timer.current = setTimeout(() => void search(wanted), DEBOUNCE_MS);
  }

  function pick(user: AccessUser) {
    clearTimeout(timer.current);
    latest.current = "";
    setOpen(false);
    setSuggestions({ kind: "none" });
    const address = pickedAddress(user);
    onChange(address);
    onPick(address);
  }

  const users = open && suggestions.kind === "users" ? suggestions.matches.users : [];
  const shown = open && suggestions.kind !== "none";

  function key(event: KeyboardEvent<HTMLInputElement>) {
    switch (event.key) {
      case "ArrowDown":
      case "ArrowUp": {
        if (suggestions.kind !== "users" || suggestions.matches.users.length === 0) {
          return;
        }
        event.preventDefault();
        const count = suggestions.matches.users.length;
        // The list opens on the first press with nothing highlighted, and wraps at either end.
        const from = open ? active : -1;
        setActive(event.key === "ArrowDown" ? (from + 1) % count : (from - 1 + count) % count);
        setOpen(true);
        return;
      }
      case "Enter":
        if (users[active]) {
          event.preventDefault();
          pick(users[active]);
        }
        return;
      case "Escape":
        setOpen(false);
        return;
    }
  }

  return (
    <div className="w-full">
      <Input
        id={id}
        ref={inputRef}
        role="combobox"
        aria-autocomplete="list"
        aria-expanded={users.length > 0}
        aria-controls={users.length > 0 ? listId : undefined}
        aria-activedescendant={users[active] ? `${listId}-${active}` : undefined}
        aria-invalid={invalid ? true : undefined}
        aria-describedby={describedBy}
        autoComplete="off"
        placeholder={placeholder}
        value={value}
        onChange={(event) => type(event.target.value)}
        onKeyDown={key}
        onBlur={() => setOpen(false)}
        readOnly={busy}
      />
      {shown ? (
        // In the flow under the field, not floating over the card: the card clips what overflows it.
        <div className="mt-1 rounded-lg border bg-popover text-popover-foreground shadow-sm">
          {users.length > 0 ? (
            <div id={listId} role="listbox" aria-label="Matching users" className="max-h-72 overflow-auto py-1">
              {users.map((user, index) => (
                // The keys are the field's (the combobox pattern): the arrows and Enter act on the list from there.
                // biome-ignore lint/a11y/useKeyWithClickEvents: the field handles the keys for every option.
                <div
                  key={pickedAddress(user)}
                  id={`${listId}-${index}`}
                  role="option"
                  tabIndex={-1}
                  aria-selected={index === active}
                  className={cn("cursor-pointer px-2.5 py-1.5 text-sm", index === active && "bg-accent")}
                  // The field keeps the focus, so the list stays open until the click picks.
                  onMouseDown={(event) => event.preventDefault()}
                  onMouseEnter={() => setActive(index)}
                  onClick={() => pick(user)}
                >
                  <span className="font-medium">{user.displayName ?? pickedAddress(user)}</span>
                  {user.displayName ? <span className="text-muted-foreground"> {pickedAddress(user)}</span> : null}
                </div>
              ))}
            </div>
          ) : null}
          <SuggestionNote suggestions={suggestions} />
        </div>
      ) : null}
    </div>
  );
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
function suggestionLines(suggestions: Exclude<Suggestions, { kind: "none" }>): string[] {
  if (suggestions.kind === "no-snapshot") {
    return [
      "No suggestions: the directory cannot be searched now and this computer has no snapshot. Type the full address.",
    ];
  }
  const { matches, text } = suggestions;
  const found = matches.users.length;
  const lines = [
    found === 0
      ? `No user starts with "${text}": a full address still works.`
      : `${found} matching user${found === 1 ? "" : "s"}${matches.more ? ", and more" : ""}: use the arrow keys to pick one.`,
  ];
  if (found > 0 && matches.more) {
    lines.push("More users match: type more of the name.");
  }
  lines.push(describeSource(matches));
  return lines;
}

/** The note under the list, its lines but the count of matches, which the list itself shows. */
function SuggestionNote({ suggestions }: { suggestions: Exclude<Suggestions, { kind: "none" }> }) {
  const all = suggestionLines(suggestions);
  const lines = suggestions.kind === "users" && suggestions.matches.users.length > 0 ? all.slice(1) : all;
  return (
    <div className="border-t px-2.5 py-1.5 text-xs text-muted-foreground" data-testid="suggestion-note">
      {lines.map((line) => (
        <p key={line}>{line}</p>
      ))}
    </div>
  );
}
