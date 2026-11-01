import type { BridgeError, SnapshotDiffReport, TooFewSnapshotsInfo } from "./protocol";

/**
 * The browser mock's two snapshots (task 115): what changed between the fixture snapshot and its changed copy a day
 * later, two hours after the second take, for the whole tenant and for bob (task 114), and the answer without two
 * snapshots; a Vitest test holds the three to the bridge samples `snapshot-diff-report.json`,
 * `snapshot-diff-report.user.json` and `too-few-snapshots.json` under tests/contracts/bridge, field by field.
 */
export const mockSnapshotDiff: SnapshotDiffReport = {
  from: {
    snapshot: "2026-10-31T060000Z.sqlite",
    taken: "2026-10-31T06:00:00+00:00",
    ageHours: 26,
    pick: "2026-10-31T060000Z.sqlite",
  },
  to: {
    snapshot: "2026-11-01T060000Z.sqlite",
    taken: "2026-11-01T06:00:00+00:00",
    ageHours: 2,
    pick: "latest",
  },
  domains: [
    {
      name: "members",
      compared: true,
      added: 3,
      removed: 1,
    },
    {
      name: "owners",
      compared: true,
      added: 1,
      removed: 1,
    },
    {
      name: "sharing",
      compared: true,
      added: 1,
      removed: 1,
    },
    {
      name: "grants",
      compared: true,
      added: 1,
      removed: 1,
    },
    {
      name: "folderPermissions",
      compared: true,
      added: 1,
      removed: 1,
    },
    {
      name: "forwarding",
      compared: true,
      added: 1,
      removed: 1,
    },
  ],
  changes: [
    {
      domain: "members",
      change: "added",
      what: "Audit",
      who: "bob@contoso.com",
      how: "member",
      whatId: "00000000-0000-0000-0000-000000000037",
      whoId: "00000000-0000-0000-0000-000000000003",
    },
    {
      domain: "members",
      change: "removed",
      what: "Finance EU",
      who: "FIN-KIOSK-01",
      how: "member (device)",
      whatId: "00000000-0000-0000-0000-000000000032",
      whoId: "00000000-0000-0000-0000-000000000006",
    },
    {
      domain: "members",
      change: "added",
      what: "Finance Staff",
      who: "Audit",
      how: "member (group)",
      whatId: "00000000-0000-0000-0000-000000000035",
      whoId: "00000000-0000-0000-0000-000000000037",
    },
    {
      domain: "members",
      change: "added",
      what: "Project Phoenix",
      who: "bob@contoso.com",
      how: "member",
      whatId: "00000000-0000-0000-0000-000000000033",
      whoId: "00000000-0000-0000-0000-000000000003",
    },
    {
      domain: "owners",
      change: "removed",
      what: "Finance EU",
      who: "carol@contoso.com",
      how: "owner (graph)",
      whatId: "00000000-0000-0000-0000-000000000032",
      whoId: "00000000-0000-0000-0000-000000000004",
    },
    {
      domain: "owners",
      change: "added",
      what: "Project Phoenix",
      who: "dave@contoso.com",
      how: "owner (graph)",
      whatId: "00000000-0000-0000-0000-000000000033",
      whoId: "00000000-0000-0000-0000-000000000005",
    },
    {
      domain: "sharing",
      change: "removed",
      what: "Finance: /Budget.xlsx",
      who: "everyone in the organization",
      how: "link organization read",
    },
    {
      domain: "sharing",
      change: "added",
      what: "Project Phoenix: /Plans/Roadmap.pptx",
      who: "bob@contoso.com",
      how: "link users read",
      whoId: "00000000-0000-0000-0000-000000000003",
    },
    {
      domain: "grants",
      change: "added",
      what: "finance@contoso.com",
      who: "dave@contoso.com",
      how: "full-access",
      whatId: "00000000-0000-0000-0000-000000000015",
      whoId: "00000000-0000-0000-0000-000000000005",
    },
    {
      domain: "grants",
      change: "removed",
      what: "reception@contoso.com",
      who: "Finance Staff",
      how: "full-access",
      whatId: "00000000-0000-0000-0000-000000000016",
      whoId: "00000000-0000-0000-0000-000000000035",
    },
    {
      domain: "folderPermissions",
      change: "removed",
      what: "carol@contoso.com: Calendar",
      who: "Default",
      how: "AvailabilityOnly",
      whatId: "00000000-0000-0000-0000-000000000004",
    },
    {
      domain: "folderPermissions",
      change: "added",
      what: "carol@contoso.com: Calendar",
      who: "Default",
      how: "Reviewer",
      whatId: "00000000-0000-0000-0000-000000000004",
    },
    {
      domain: "forwarding",
      change: "added",
      what: "alice@contoso.com",
      who: "carol@contoso.com",
      how: "recipient, keeps a copy",
      whatId: "00000000-0000-0000-0000-000000000001",
      whoId: "00000000-0000-0000-0000-000000000004",
    },
    {
      domain: "forwarding",
      change: "removed",
      what: "bob@contoso.com",
      who: "bob@fabrikam.com",
      how: "smtp-address, external, keeps a copy",
      whatId: "00000000-0000-0000-0000-000000000003",
    },
  ],
  warnings: [
    "sites is partial in 2026-10-31T060000Z.sqlite (1 item(s) not read): the sharing rows of those items may show as removed or added",
    "sites is partial in 2026-11-01T060000Z.sqlite (1 item(s) not read): the sharing rows of those items may show as removed or added",
  ],
  added: 8,
  removed: 6,
};

/** Bob's changes between the same two snapshots: the nested membership through the new group Audit and what it brings. */
export const mockBobSnapshotDiff: SnapshotDiffReport = {
  from: {
    snapshot: "2026-10-31T060000Z.sqlite",
    taken: "2026-10-31T06:00:00+00:00",
    ageHours: 26,
    pick: "2026-10-31T060000Z.sqlite",
  },
  to: {
    snapshot: "2026-11-01T060000Z.sqlite",
    taken: "2026-11-01T06:00:00+00:00",
    ageHours: 2,
    pick: "latest",
  },
  domains: [
    {
      name: "groups",
      compared: true,
      added: 3,
      removed: 0,
    },
    {
      name: "owners",
      compared: true,
      added: 0,
      removed: 0,
    },
    {
      name: "sites",
      compared: true,
      added: 1,
      removed: 0,
    },
    {
      name: "sharedItems",
      compared: true,
      added: 2,
      removed: 0,
    },
    {
      name: "mailboxes",
      compared: true,
      added: 0,
      removed: 0,
    },
    {
      name: "calendars",
      compared: true,
      added: 1,
      removed: 0,
    },
    {
      name: "forwarding",
      compared: true,
      added: 0,
      removed: 1,
    },
  ],
  changes: [
    {
      domain: "groups",
      change: "added",
      what: "Audit",
      who: "bob@contoso.com",
      how: "member",
      whatId: "00000000-0000-0000-0000-000000000037",
      whoId: "00000000-0000-0000-0000-000000000003",
    },
    {
      domain: "groups",
      change: "added",
      what: "Finance Staff",
      who: "bob@contoso.com",
      how: "member",
      whatId: "00000000-0000-0000-0000-000000000035",
      whoId: "00000000-0000-0000-0000-000000000003",
      through: "Audit",
    },
    {
      domain: "groups",
      change: "added",
      what: "Project Phoenix",
      who: "bob@contoso.com",
      how: "member",
      whatId: "00000000-0000-0000-0000-000000000033",
      whoId: "00000000-0000-0000-0000-000000000003",
    },
    {
      domain: "sites",
      change: "added",
      what: "Project Phoenix",
      who: "bob@contoso.com",
      how: "member",
      whoId: "00000000-0000-0000-0000-000000000003",
      through: "Project Phoenix",
    },
    {
      domain: "sharedItems",
      change: "added",
      what: "Project Phoenix: /Plans/Roadmap.pptx",
      who: "bob@contoso.com",
      how: "link users read",
      whoId: "00000000-0000-0000-0000-000000000003",
    },
    {
      domain: "sharedItems",
      change: "added",
      what: "Project Phoenix: /Plans/Roadmap.pptx",
      who: "Finance Staff",
      how: "link users read",
      whoId: "00000000-0000-0000-0000-000000000035",
      through: "Finance Staff",
    },
    {
      domain: "calendars",
      change: "added",
      what: "carol@contoso.com: Calendar",
      who: "Finance Staff",
      how: "Reviewer",
      whatId: "00000000-0000-0000-0000-000000000004",
      whoId: "00000000-0000-0000-0000-000000000035",
      through: "Finance Staff",
    },
    {
      domain: "forwarding",
      change: "removed",
      what: "bob@contoso.com",
      who: "bob@fabrikam.com",
      how: "smtp-address, external, keeps a copy",
      whatId: "00000000-0000-0000-0000-000000000003",
    },
  ],
  warnings: [
    "sites is partial in 2026-10-31T060000Z.sqlite (1 item(s) not read): the sites rows of those items may show as removed or added",
    "sites is partial in 2026-11-01T060000Z.sqlite (1 item(s) not read): the sites rows of those items may show as removed or added",
    "sites is partial in 2026-10-31T060000Z.sqlite (1 item(s) not read): the sharedItems rows of those items may show as removed or added",
    "sites is partial in 2026-11-01T060000Z.sqlite (1 item(s) not read): the sharedItems rows of those items may show as removed or added",
  ],
  added: 7,
  removed: 1,
  user: {
    objectId: "00000000-0000-0000-0000-000000000003",
    displayName: "Bob Example",
    userPrincipalName: "bob@contoso.com",
    mail: "bob@contoso.com",
  },
};

/** The answer without two snapshots to compare. */
export const mockTooFewSnapshots: TooFewSnapshotsInfo = {
  profile: "contoso",
  snapshots: 0,
  message: "no snapshot of the profile 'contoso' yet",
  hint: "the card compares two snapshots: ask the administrator to take one every morning, so there is always the day before to compare with",
};

/** The names bob is known by in the mock's snapshots, as the core finds a user: object id, user principal name, address. */
const bobsNames: readonly string[] = ["00000000-0000-0000-0000-000000000003", "bob@contoso.com"];

/**
 * The mock's `snapshot-diff` answer: without a snapshot (`snapshot` false) the plain state; else the two snapshots'
 * changes, bob's by any of his names, and any other user unknown to both. The mock has only the two snapshots, so a
 * named pick is ignored.
 */
export function mockDiff(
  user: string,
  snapshot: boolean,
): { type: string; data: SnapshotDiffReport | TooFewSnapshotsInfo | BridgeError } {
  if (!snapshot) {
    return { type: "too-few-snapshots", data: mockTooFewSnapshots };
  }
  if (user === "") {
    return { type: "snapshot-diff-report", data: mockSnapshotDiff };
  }
  return bobsNames.includes(user.toLowerCase())
    ? { type: "snapshot-diff-report", data: mockBobSnapshotDiff }
    : {
        type: "error",
        data: {
          code: "not-found",
          message: `neither ${mockSnapshotDiff.from.snapshot} nor ${mockSnapshotDiff.to.snapshot} knows a user '${user}'`,
          hint: "check the name or the address and try again",
        },
      };
}
