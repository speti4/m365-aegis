import { type KeyboardEvent, type Ref, useEffect, useId, useRef, useState } from "react";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";

/** How long the field waits after a keystroke before it asks: a quick typist sends one request for a word. */
export const DEBOUNCE_MS = 250;

/** One suggestion: what a pick puts in the field, and how the list shows it (a name and, beside it, the address). */
export interface Suggestion {
  value: string;
  name: string;
  detail?: string;
}

/**
 * What a search found: the suggestions, the line the card's status region says (how many match, none, no source), and
 * the lines under the list (where the suggestions came from, more than shown).
 */
export interface Suggested {
  options: Suggestion[];
  announce: string;
  note: string[];
}

/** The item of a comma-separated list the cursor is on: the text after the last comma or semicolon. */
export function currentItem(text: string): string {
  return text.slice(Math.max(text.lastIndexOf(","), text.lastIndexOf(";")) + 1);
}

/**
 * A pick added to a comma-separated list (task 125): it replaces the item being typed, an address the list already
 * names is not added twice, and a comma follows, so the next name can be typed at once.
 */
export function addToList(text: string, value: string): string {
  const before = text.slice(0, Math.max(text.lastIndexOf(","), text.lastIndexOf(";")) + 1);
  const kept = before
    .split(/[,;]/)
    .map((item) => item.trim())
    .filter((item) => item !== "");
  const named = kept.some((item) => item.toLowerCase() === value.toLowerCase());
  return `${(named ? kept : [...kept, value]).join(", ")}, `;
}

/**
 * A text field that suggests as the colleague types (the user picker of task 112, made general in task 125): once
 * `minLength` letters are typed, a keystroke starts a wait of `DEBOUNCE_MS` and only the last one asks (`suggest`); an
 * answer to an older text is dropped. The arrow keys move through the list, Enter or a click picks and hands the value
 * to the card (`onPick`); Escape closes the list. Enter without a highlighted suggestion submits the card's form with
 * the text as typed, so a full address still works; with `enterPicksFirst` (the write cards and the access changes
 * card, whose forms have more than this field) Enter on an open list picks its first suggestion instead, and only Enter
 * on a closed list submits. A `null` answer (a failed search) shows no list: the card's own request reports the error.
 *
 * With `multiple` the field holds a comma-separated list: the item being typed is what the field searches for, and a
 * pick replaces it (`addToList`).
 *
 * The field is the card's (tasks 120 and 121): its `id` for the card's label, `invalid` and `describedBy` for the
 * card's error and hints, `inputRef` for the card's focus. While the card looks up (`busy`) the field is read-only, not
 * disabled, so the focus stays in it. What the list says goes to the card's status region through `onAnnounce`; the
 * field is `expanded` only while the list has a suggestion to pick.
 */
export function SuggestField({
  id,
  inputRef,
  value,
  onChange,
  onPick,
  suggest,
  listLabel,
  minLength,
  busy = false,
  invalid = false,
  describedBy,
  onAnnounce,
  placeholder,
  multiple = false,
  enterPicksFirst = false,
}: {
  id: string;
  inputRef?: Ref<HTMLInputElement>;
  value: string;
  onChange: (value: string) => void;
  onPick?: (value: string) => void;
  suggest: (text: string) => Promise<Suggested | null>;
  listLabel: string;
  minLength: number;
  busy?: boolean;
  invalid?: boolean;
  describedBy?: string;
  onAnnounce?: (text: string) => void;
  placeholder?: string;
  multiple?: boolean;
  enterPicksFirst?: boolean;
}) {
  const listId = useId();
  const [found, setFound] = useState<Suggested | null>(null);
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
    const answer = await suggest(text);
    if (latest.current !== text) {
      return;
    }
    setFound(answer);
    if (answer) {
      onAnnounce?.(answer.announce);
    }
    setActive(-1);
    setOpen(true);
  }

  function type(text: string) {
    onChange(text);
    clearTimeout(timer.current);
    const wanted = (multiple ? currentItem(text) : text).trim();
    if (wanted.length < minLength) {
      latest.current = "";
      setFound(null);
      setOpen(false);
      return;
    }
    timer.current = setTimeout(() => void search(wanted), DEBOUNCE_MS);
  }

  function pick(option: Suggestion) {
    clearTimeout(timer.current);
    latest.current = "";
    setOpen(false);
    setFound(null);
    onChange(multiple ? addToList(value, option.value) : option.value);
    onPick?.(option.value);
  }

  const options = open && found ? found.options : [];

  function key(event: KeyboardEvent<HTMLInputElement>) {
    switch (event.key) {
      case "ArrowDown":
      case "ArrowUp": {
        if (!found || found.options.length === 0) {
          return;
        }
        event.preventDefault();
        const total = found.options.length;
        // The list opens on the first press with nothing highlighted, and wraps at either end.
        const from = open ? active : -1;
        setActive(event.key === "ArrowDown" ? (from + 1) % total : (from - 1 + total) % total);
        setOpen(true);
        return;
      }
      case "Enter": {
        const chosen = options[active] ?? (enterPicksFirst ? options[0] : undefined);
        if (chosen) {
          event.preventDefault();
          pick(chosen);
        }
        return;
      }
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
        aria-expanded={options.length > 0}
        aria-controls={options.length > 0 ? listId : undefined}
        aria-activedescendant={options[active] ? `${listId}-${active}` : undefined}
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
      {open && found ? (
        // In the flow under the field, not floating over the card: the card clips what overflows it.
        <div className="mt-1 rounded-lg border bg-popover text-popover-foreground shadow-sm">
          {options.length > 0 ? (
            <div id={listId} role="listbox" aria-label={listLabel} className="max-h-72 overflow-auto py-1">
              {options.map((option, index) => (
                // The keys are the field's (the combobox pattern): the arrows and Enter act on the list from there.
                // biome-ignore lint/a11y/useKeyWithClickEvents: the field handles the keys for every option.
                <div
                  key={option.value}
                  id={`${listId}-${index}`}
                  role="option"
                  tabIndex={-1}
                  aria-selected={index === active}
                  className={cn("cursor-pointer px-2.5 py-1.5 text-sm", index === active && "bg-accent")}
                  // The field keeps the focus, so the list stays open until the click picks.
                  onMouseDown={(event) => event.preventDefault()}
                  onMouseEnter={() => setActive(index)}
                  onClick={() => pick(option)}
                >
                  <span className="font-medium">{option.name}</span>
                  {option.detail ? <span className="text-muted-foreground"> {option.detail}</span> : null}
                </div>
              ))}
            </div>
          ) : null}
          {/* Every answer says where its suggestions came from, or why there are none. */}
          <div className="border-t px-2.5 py-1.5 text-xs text-muted-foreground" data-testid="suggestion-note">
            {found.note.map((line) => (
              <p key={line}>{line}</p>
            ))}
          </div>
        </div>
      ) : null}
    </div>
  );
}
