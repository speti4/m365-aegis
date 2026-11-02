import { describe, expect, it } from "vitest";
import {
  calendarRole,
  capitalize,
  changeVerb,
  diffHow,
  groupKindName,
  mailboxTypeName,
  objectTypeName,
  plural,
  rightName,
  rightNames,
  roleName,
  sentence,
  sharingAccess,
  snapshotTime,
  windowError,
} from "./vocabulary";

describe("the window's vocabulary (task 122)", () => {
  it("counts by the English plural rules, a regular plural or the one given", () => {
    expect(plural(0, "change")).toBe("0 changes");
    expect(plural(1, "change")).toBe("1 change");
    expect(plural(2.5, "hour")).toBe("2.5 hours");
    expect(plural(3, "mailbox")).toBe("3 mailboxes");
    expect(plural(2, "match")).toBe("2 matches");
    expect(plural(2, "person", "people")).toBe("2 people");
  });

  it("makes a sentence of the core's fragment, but keeps an address, a path or a quote as it is", () => {
    expect(sentence("the plan is older than 15 minutes")).toBe("The plan is older than 15 minutes.");
    expect(sentence("  try again.  ")).toBe("Try again.");
    expect(sentence("is it?")).toBe("Is it?");
    expect(sentence("bob@contoso.com is outside your organization")).toBe(
      "bob@contoso.com is outside your organization.",
    );
    expect(sentence("'Fin' matches 2 groups")).toBe("'Fin' matches 2 groups.");
    expect(sentence("")).toBe("");
    expect(capitalize("no forwarding")).toBe("No forwarding");
    expect(capitalize("/sites/finance")).toBe("/sites/finance");
  });

  it("names the permissions, the mailbox types, the group types and the object types as the admin centers do", () => {
    expect(Object.values(rightNames)).toEqual(["Full Access", "Send As", "Send on Behalf"]);
    expect(rightName("send-as")).toBe("Send As");
    expect(rightName("FullAccess")).toBe("FullAccess");
    expect(["SharedMailbox", "UserMailbox", "RoomMailbox", "DiscoveryMailbox"].map(mailboxTypeName)).toEqual([
      "Shared mailbox",
      "User mailbox",
      "Room mailbox",
      "DiscoveryMailbox",
    ]);
    // The group lookup's word and the snapshot's name the same type the same way.
    expect(
      ["microsoft365", "Microsoft365", "MailEnabledSecurity", "distribution", "Unified"].map(groupKindName),
    ).toEqual(["Microsoft 365", "Microsoft 365", "Mail-enabled security", "Distribution list", "Unified"]);
    expect(["user", "group", "device", "orgContact", "servicePrincipal", "printer"].map(objectTypeName)).toEqual([
      "User",
      "Group",
      "Device",
      "Contact",
      "App",
      "printer",
    ]);
    expect([roleName("member"), roleName("owner"), roleName("guest")]).toEqual(["Member", "Owner", "guest"]);
  });

  it("names a shared item's permission as SharePoint's share dialog does", () => {
    expect(sharingAccess({ kind: "link", scope: "organization", roles: "read" })).toBe(
      "Link: people in your organization · View",
    );
    expect(
      sharingAccess({ kind: "link", scope: "anonymous", roles: "read,write", expires: "2026-12-31", password: true }),
    ).toBe("Link: anyone with the link · View, Edit · until 2026-12-31 · with a password");
    expect(sharingAccess({ kind: "link", scope: "tenantWide" })).toBe("Link: tenantWide");
    expect(sharingAccess({ kind: "link" })).toBe("Link");
    expect(sharingAccess({ kind: "direct", roles: "owner" })).toBe("Shared directly · Owner");
    expect(sharingAccess({ kind: "inherited", roles: "sp.full control" })).toBe("inherited · sp.full control");
  });

  it("names the calendar roles as Outlook does", () => {
    expect(calendarRole("AvailabilityOnly")).toBe("Free/busy only");
    expect(calendarRole("LimitedDetails")).toBe("Free/busy, titles and locations");
    expect(calendarRole("Editor, PublishingEditor")).toBe("Editor, Publishing editor");
    expect(calendarRole("Reviewer")).toBe("Reviewer");
  });

  it("reads how a diff's row holds what it holds by the part it belongs to", () => {
    expect(diffHow("members", "member")).toBe("Member");
    expect(diffHow("members", "member (device)")).toBe("Member (device)");
    expect(diffHow("owners", "owner (exchange)")).toBe("Owner");
    expect(diffHow("groups", "member")).toBe("Member");
    expect(diffHow("sites", "owner")).toBe("Owner");
    expect(diffHow("members", "manager")).toBe("manager");
    expect(diffHow("sharing", "link users write until 2026-12-31 with a password")).toBe(
      "Link: specific people · Edit · until 2026-12-31 · with a password",
    );
    expect(diffHow("sharedItems", "direct read")).toBe("Shared directly · View");
    expect(diffHow("sharing", "a link of another kind")).toBe("a link of another kind");
    expect(diffHow("grants", "send-on-behalf")).toBe("Send on Behalf");
    expect(diffHow("mailboxes", "full-access")).toBe("Full Access");
    expect(diffHow("folderPermissions", "AvailabilityOnly")).toBe("Free/busy only");
    expect(diffHow("calendars", "Reviewer")).toBe("Reviewer");
    expect(diffHow("forwarding", "smtp-address, external, keeps a copy")).toBe(
      "To an email address, outside your organization, keeps a copy",
    );
    expect(diffHow("forwarding", "recipient")).toBe("To a recipient");
    expect(diffHow("licenses", "assigned")).toBe("assigned");
  });

  it("puts the card's verb on a change's badge, and the action's word for an operation without one", () => {
    expect(changeVerb("mailbox-access-grant", "add")).toBe("Grant");
    expect(changeVerb("mailbox-access-revoke", "remove")).toBe("Revoke");
    expect(changeVerb("mailbox-forwarding-set", "set")).toBe("Forward");
    expect(changeVerb("mailbox-forwarding-clear", "clear")).toBe("Stop forwarding");
    expect(changeVerb("group-members-add", "add")).toBe("Add");
    expect(changeVerb("call", "send")).toBe("Send");
    expect(changeVerb("call", "patch")).toBe("patch");
  });

  it("names a snapshot by its time, not its file", () => {
    expect(snapshotTime("2026-10-31T060000Z.sqlite")).toBe("2026-10-31 06:00 UTC");
    expect(snapshotTime("2026-10-31T060000Z")).toBe("2026-10-31 06:00 UTC");
    expect(snapshotTime("before-the-move.sqlite")).toBe("before-the-move.sqlite");
  });
});

describe("an error in the window's words (task 122)", () => {
  it("rewords a name that finds no group, and a word that finds several, by name", () => {
    expect(
      windowError({
        code: "not-found",
        message: "no group named 'Finanse'",
        hint: "give the exact display name, the address, the object id, or a word of the name",
      }),
    ).toEqual({
      message: "No group named 'Finanse'.",
      hint: "Check the spelling, or enter the group's email address, its object ID, or one word of its name.",
    });
    expect(
      windowError({
        code: "not-found",
        message: "'Fin' names 3 groups",
        hint: "give the object id of one: Finance (00000000-0000-0000-0000-000000000031), Finance EU (00000000-0000-0000-0000-000000000032), Finance Staff (00000000-0000-0000-0000-000000000033)",
      }),
    ).toEqual({
      message: "'Fin' matches 3 groups: Finance, Finance EU, Finance Staff.",
      hint: "Enter the full name or the email address of the one you want.",
    });
    expect(
      windowError({
        code: "not-found",
        message: "'a' names 10 groups, and maybe more",
        hint: "give the object id of one: A (00000000-0000-0000-0000-000000000041)",
      }).message,
    ).toBe("'a' matches 10 or more groups: A.");
  });

  it("rewords a holder or an address the mailbox writes do not find", () => {
    expect(
      windowError({
        code: "not-found",
        message: "recipient 'zed@contoso.com' not found (line 3)",
        hint: "give the holder's address or user principal name; nothing was sent",
      }),
    ).toEqual({
      message: "No one named 'zed@contoso.com' was found.",
      hint: "Check the spelling, or enter the person's sign-in name or email address. Nothing was sent.",
    });
    expect(
      windowError({
        code: "usage",
        message: "the forwarding address: 'bob' is not an address",
        hint: "give the address to forward to, such as bob@fabrikam.com; nothing was sent",
      }),
    ).toEqual({
      message: "'bob' is not an email address.",
      hint: "Enter the email address to forward to, such as bob@fabrikam.com. Nothing was sent.",
    });
  });

  it("gives any other error the window's terms, each a sentence, and keeps a missing hint missing", () => {
    expect(
      windowError({
        code: "usage",
        message: "the snapshot 2026-10-31T060000Z.sqlite knows no user 'x'",
        hint: "give the user principal names or the object ids; FullAccess needs a mailbox",
      }),
    ).toEqual({
      message: "The snapshot of 2026-10-31 06:00 UTC knows no user 'x'.",
      hint: "Enter the sign-in names or the object IDs; Full Access needs a mailbox.",
    });
    expect(
      windowError({
        code: "not-found",
        message: "neither 2026-10-31T060000Z.sqlite nor 2026-11-01T060000Z.sqlite knows a user principal name 'x'",
      }),
    ).toEqual({
      message:
        "Neither the snapshot of 2026-10-31 06:00 UTC nor the snapshot of 2026-11-01 06:00 UTC knows a sign-in name 'x'.",
      hint: undefined,
    });
    // An ambiguous group's sentence without the core's list of candidates is no rewording of it.
    expect(windowError({ code: "not-found", message: "'Fin' names 3 groups", hint: "ask again" })).toEqual({
      message: "'Fin' names 3 groups.",
      hint: "Ask again.",
    });
    expect(windowError({ code: "not-found", message: "'Fin' names 3 groups" }).hint).toBeUndefined();
  });
});
