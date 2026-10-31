import type { AccessReport, BridgeError, NoSnapshotInfo } from "./protocol";

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
