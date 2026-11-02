import { type Ref, useCallback, useEffect, useRef } from "react";
import type { BridgeClient } from "../bridge/client";
import { type BridgeError, type MailboxInfo, MessageType } from "../bridge/protocol";
import { count } from "./plan-wording";
import { type Suggested, SuggestField } from "./suggest-field";

/** How many mailboxes the list shows at most: more match a name too short to help. */
export const MAX_MAILBOXES = 8;

/** The line under the list that names where the suggestions come from. */
const source = "From the shared mailboxes; any other mailbox by its full address.";

/**
 * The shared mailboxes whose name, a word of it, address or user principal name starts with `text` (any case), as the
 * list shows them (task 125): eight at most, and the lines the field says and shows under them.
 */
export function mailboxSuggestions(mailboxes: readonly MailboxInfo[], text: string): Suggested {
  const wanted = text.toLowerCase();
  const matching = mailboxes.filter((mailbox) =>
    [mailbox.displayName, ...mailbox.displayName.split(/\s+/), mailbox.primarySmtpAddress, mailbox.userPrincipalName]
      .filter((candidate) => candidate !== undefined)
      .some((candidate) => candidate.toLowerCase().startsWith(wanted)),
  );
  const shown = matching.slice(0, MAX_MAILBOXES);
  const more = matching.length > shown.length;
  if (shown.length === 0) {
    const none = `No shared mailbox starts with "${text}": a full address still works.`;
    return { options: [], announce: none, note: [none, source] };
  }
  return {
    options: shown.map((mailbox) => ({
      value: mailbox.primarySmtpAddress,
      name: mailbox.displayName,
      detail: mailbox.primarySmtpAddress,
    })),
    announce: `${count(shown.length, "matching shared mailbox", "matching shared mailboxes")}${more ? ", and more" : ""}: use the arrow keys to pick one.`,
    note: [...(more ? ["More mailboxes match: type more of the name."] : []), source],
  };
}

/** The line under the field when the shared mailboxes could not be listed. */
export function unlisted(error: BridgeError): Suggested {
  const line = `No suggestions: the shared mailboxes could not be listed (${error.message}). Type the mailbox's address.`;
  return { options: [], announce: line, note: [line] };
}

/**
 * The mailbox field of the write cards (task 125): it suggests from the shared mailboxes, which the window lists once
 * with `mailbox-list` (`shared: true`, as the mailbox access card's "List the shared mailboxes" does) when the first
 * letter is typed, and filters on this computer from then on; a pick puts the mailbox's address in the field. A
 * mailbox's address, name or object id typed by hand still works, a user mailbox among them. The list is asked for
 * only while the card can write (`enabled`: signed in, with an Exchange Online token); a sign-out forgets it, and a
 * failed listing is said under the field and asked for again at the next letter.
 */
export function MailboxPicker({
  client,
  enabled,
  ...field
}: {
  client: BridgeClient;
  enabled: boolean;
  id: string;
  inputRef?: Ref<HTMLInputElement>;
  value: string;
  onChange: (value: string) => void;
  onAnnounce?: (text: string) => void;
}) {
  const listed = useRef<Promise<MailboxInfo[] | BridgeError> | null>(null);
  useEffect(() => {
    if (!enabled) {
      listed.current = null;
    }
  }, [enabled]);

  const suggest = useCallback(
    async (text: string): Promise<Suggested | null> => {
      if (!enabled) {
        return null;
      }
      listed.current ??= client
        .call(MessageType.mailboxList, { shared: true })
        .then((reply) =>
          reply.type === MessageType.mailboxes
            ? reply.data.mailboxes
            : reply.type === MessageType.error
              ? reply.data
              : { code: "bad-reply", message: `the window answered with a '${reply.type}'` },
        );
      const mailboxes = await listed.current;
      if (!Array.isArray(mailboxes)) {
        listed.current = null;
        return unlisted(mailboxes);
      }
      return mailboxSuggestions(mailboxes, text);
    },
    [client, enabled],
  );

  return (
    <SuggestField
      {...field}
      placeholder="Start typing a shared mailbox's name, or an address"
      suggest={suggest}
      listLabel="Matching shared mailboxes"
      minLength={1}
      enterPicksFirst
    />
  );
}
