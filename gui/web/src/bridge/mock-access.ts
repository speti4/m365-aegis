import type { AccessReport, AccessUser, BridgeError, NoSnapshotInfo, UserMatches } from "./protocol";

/**
 * The browser mock's snapshot (task 102): dave's access view as the core answers it over the fixture snapshot, two
 * hours after the take, and the answer without a snapshot; a Vitest test holds both to the bridge samples
 * `access-report.json` and `no-snapshot.json` under tests/contracts/bridge, field by field.
 */
export const mockDaveAccess: AccessReport = {
  snapshot: "2026-10-31T060000Z.sqlite",
  taken: "2026-10-31T06:00:00+00:00",
  ageHours: 2,
  user: {
    objectId: "00000000-0000-0000-0000-000000000005",
    displayName: "Dave Example",
    userPrincipalName: "dave@contoso.com",
    mail: "dave@contoso.com",
  },
  groups: [
    {
      id: "00000000-0000-0000-0000-000000000031",
      displayName: "Finance",
      kind: "Microsoft365",
      via: ["Finance EU"],
      direct: false,
    },
    {
      id: "00000000-0000-0000-0000-000000000032",
      displayName: "Finance EU",
      kind: "Security",
      via: [],
      direct: true,
    },
    {
      id: "00000000-0000-0000-0000-000000000035",
      displayName: "Finance Staff",
      kind: "MailEnabledSecurity",
      via: ["Finance EU"],
      direct: false,
    },
    {
      id: "00000000-0000-0000-0000-000000000033",
      displayName: "Project Phoenix",
      kind: "Microsoft365",
      via: [],
      direct: true,
    },
  ],
  sites: [
    {
      name: "Finance",
      webUrl: "https://contoso.sharepoint.com/sites/finance",
      group: "Finance",
      role: "member",
    },
    {
      name: "Project Phoenix",
      webUrl: "https://contoso.sharepoint.com/sites/phoenix",
      group: "Project Phoenix",
      role: "member",
    },
  ],
  sharedItems: [
    {
      site: "Finance",
      item: "Reports",
      path: "/",
      kind: "direct",
      roles: "read",
      through: "Finance EU",
    },
    {
      site: "Project Phoenix",
      item: "Roadmap.pptx",
      path: "/Plans",
      kind: "link",
      scope: "users",
      roles: "read",
      through: "Finance Staff",
    },
    {
      site: "Project Phoenix",
      item: "Roadmap.pptx",
      path: "/Plans",
      kind: "direct",
      roles: "read",
    },
  ],
  mailboxes: [
    {
      mailbox: "Reception",
      address: "reception@contoso.com",
      right: "full-access",
      through: "Finance Staff",
    },
  ],
  calendars: [
    {
      mailbox: "Carol Example",
      address: "carol@contoso.com",
      folder: "Calendar",
      rights: "Reviewer",
      through: "Finance Staff",
    },
  ],
  everyone: {
    organizationLinks: 1,
    anonymousLinks: 1,
    defaultCalendars: 1,
    anonymousCalendars: 1,
  },
  warnings: [
    "sites is partial in this snapshot: 1 item(s) could not be read (forbidden 1); `m365-aegis snapshot show` names them",
    "oneDrive was not read: other users' OneDrives need a consent the tool does not ask for (Sites.Read.All reads the signed-in account's only; task 092)",
  ],
};

/** The answer of a profile without a snapshot, as the host gives it. */
export const mockNoSnapshot: NoSnapshotInfo = {
  profile: "contoso",
  message: "no snapshot of the profile 'contoso' yet",
  hint: "ask the administrator to take a snapshot on this computer, or to schedule one every morning",
};

/**
 * The mock's `access` answer: the user is missing (a usage error), the profile has no snapshot (`snapshot` false), the
 * user is dave by any of his names, or the snapshot knows no such user.
 */
export function mockAccess(
  user: string,
  snapshot: boolean,
): { type: string; data: AccessReport | NoSnapshotInfo | BridgeError } {
  if (user === "") {
    return {
      type: "error",
      data: {
        code: "usage",
        message: "no user was named",
        hint: "give the user's user principal name, address or object id",
      },
    };
  }
  if (!snapshot) {
    return { type: "no-snapshot", data: mockNoSnapshot };
  }
  const { objectId, displayName, userPrincipalName, mail } = mockDaveAccess.user;
  return [objectId, displayName, userPrincipalName, mail].some((name) => name?.toLowerCase() === user.toLowerCase())
    ? { type: "access-report", data: mockDaveAccess }
    : {
        type: "error",
        data: {
          code: "not-found",
          message: `the snapshot ${mockDaveAccess.snapshot} knows no user '${user}'`,
          hint: "check the name or the address and try again",
        },
      };
}

/** A user of the mock's directory: the fixtures' `* Example` users; a user without an address has none in Graph. */
const directoryUser = (
  name: string,
  id: string,
  mail = true,
): AccessUser & { displayName: string; userPrincipalName: string } => ({
  objectId: `00000000-0000-0000-0000-0000000000${id}`,
  displayName: `${name} Example`,
  userPrincipalName: `${name.toLowerCase()}@contoso.com`,
  ...(mail ? { mail: `${name.toLowerCase()}@contoso.com` } : {}),
});

/**
 * The users of the mock's directory and snapshot (task 112), as the recorded Graph answers hold them (Carol has no
 * address there); a Vitest test holds the mock's answer to the `user-search` sample (`exa`, a word of every name),
 * signed in, equal to the `user-matches` sample.
 */
export const mockDirectoryUsers = [
  directoryUser("Alice", "01"),
  directoryUser("Bob", "03"),
  directoryUser("Carol", "04", false),
  directoryUser("Dave", "05"),
  directoryUser("Eve", "07"),
];

/**
 * The mock's `user-search` answer, as the host gives it: a missing text is a usage error; signed in, the directory
 * answers; signed out, the snapshot (its name and time), or `no-snapshot` without one. A user matches when the start
 * of the name or of a word of it, of the principal name or of the address is the text, in any case.
 */
export function mockUserSearch(
  text: string,
  { signedIn, snapshot }: { signedIn: boolean; snapshot: boolean },
): { type: string; data: UserMatches | NoSnapshotInfo | BridgeError } {
  if (text === "") {
    return {
      type: "error",
      data: { code: "usage", message: "no text to search for", hint: "type the start of a name or an address" },
    };
  }
  if (!signedIn && !snapshot) {
    return { type: "no-snapshot", data: mockNoSnapshot };
  }
  const prefix = text.toLowerCase();
  // The start of the name or of a word of it (" " before the name stands for its start), of the principal name or address.
  const users = mockDirectoryUsers.filter(
    ({ displayName, userPrincipalName, mail }) =>
      ` ${displayName.toLowerCase()}`.includes(` ${prefix}`) ||
      [userPrincipalName, mail ?? ""].some((name) => name.startsWith(prefix)),
  );
  const source = signedIn
    ? { source: "graph" as const }
    : { source: "snapshot" as const, snapshot: mockDaveAccess.snapshot, taken: mockDaveAccess.taken };
  return { type: "user-matches", data: { ...source, users, more: false } };
}
