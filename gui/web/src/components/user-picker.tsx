import { type KeyboardEvent, useEffect, useId, useRef, useState } from "react";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import type { BridgeClient } from "../bridge/client";
import { type AccessUser, MessageType } from "../bridge/protocol";

/** How many letters the picker waits for before it suggests: fewer match too many users to help. */
export const SUGGEST_AFTER = 3;

/** How long the picker waits after a keystroke before it asks: a quick typist sends one request for a word. */
export const DEBOUNCE_MS = 250;

/** What a picked user goes to the card's request as: the user principal name, else the address, else the object id. */
export function pickedAddress(user: AccessUser): string {
  return user.userPrincipalName ?? user.mail ?? user.objectId ?? "";
}

/** What the list under the field shows: nothing, the matching users, or a line why there is none. */
type Suggestions =
  | { kind: "none" }
  | { kind: "users"; text: string; users: AccessUser[]; more: boolean }
  | { kind: "no-snapshot" };

/**
 * The user picker of the access cards (task 112): a text field that suggests the users of the newest snapshot whose
 * name, a word of it, user principal name or address starts with what the colleague types, once three letters are
 * typed (`user-search`, the snapshot on this computer, no sign-in). A keystroke starts a wait of `DEBOUNCE_MS`, and only
 * the last one asks, so a word typed at speed sends one request; an answer to an older text is dropped. The arrow keys
 * move through the list, Enter or a click picks a user and hands the user's address to the card (`onPick`); Escape
 * closes the list. Enter without a highlighted user submits the card's form with the text as typed, so a full address
 * still works, and so does a user the snapshot does not know yet. A failed search shows no list: the card's own lookup
 * reports the error.
 */
export function UserPicker({
  client,
  value,
  onChange,
  onPick,
  disabled = false,
  placeholder = "Start typing a name, or a full address",
}: {
  client: BridgeClient;
  value: string;
  onChange: (value: string) => void;
  onPick: (address: string) => void;
  disabled?: boolean;
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
    if (disabled) {
      clearTimeout(timer.current);
      latest.current = "";
      setOpen(false);
    }
  }, [disabled]);
  useEffect(() => () => clearTimeout(timer.current), []);

  async function search(text: string) {
    latest.current = text;
    const reply = await client.call(MessageType.userSearch, { text });
    if (latest.current !== text) {
      return;
    }
    switch (reply.type) {
      case MessageType.userMatches:
        setSuggestions({ kind: "users", text, users: reply.data.users, more: reply.data.more });
        break;
      case MessageType.noSnapshot:
        setSuggestions({ kind: "no-snapshot" });
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

  const users = open && suggestions.kind === "users" ? suggestions.users : [];
  const shown = open && suggestions.kind !== "none";

  function key(event: KeyboardEvent<HTMLInputElement>) {
    switch (event.key) {
      case "ArrowDown":
      case "ArrowUp": {
        if (suggestions.kind !== "users" || suggestions.users.length === 0) {
          return;
        }
        event.preventDefault();
        const count = suggestions.users.length;
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
        aria-label="User"
        role="combobox"
        aria-autocomplete="list"
        aria-expanded={shown}
        aria-controls={listId}
        aria-activedescendant={users[active] ? `${listId}-${active}` : undefined}
        autoComplete="off"
        placeholder={placeholder}
        value={value}
        onChange={(event) => type(event.target.value)}
        onKeyDown={key}
        onBlur={() => setOpen(false)}
        disabled={disabled}
      />
      {shown ? (
        // In the flow under the field, not floating over the card: the card clips what overflows it.
        <div className="mt-1 rounded-lg border bg-popover text-popover-foreground shadow-sm">
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
                {user.displayName ? <span className="ml-2 text-muted-foreground">{pickedAddress(user)}</span> : null}
              </div>
            ))}
          </div>
          <SuggestionNote suggestions={suggestions} />
        </div>
      ) : null}
    </div>
  );
}

/** The line under the list: no match, more matches than shown, or no snapshot to suggest from. */
function SuggestionNote({ suggestions }: { suggestions: Suggestions }) {
  let note: string | null = null;
  if (suggestions.kind === "no-snapshot") {
    note = "No snapshot on this computer to suggest from: type the full address.";
  } else if (suggestions.kind === "users" && suggestions.users.length === 0) {
    note = `No user in the snapshot starts with "${suggestions.text}": a full address still works.`;
  } else if (suggestions.kind === "users" && suggestions.more) {
    note = "More users match: type more of the name.";
  }
  return note ? <p className="border-t px-2.5 py-1.5 text-xs text-muted-foreground">{note}</p> : null;
}
