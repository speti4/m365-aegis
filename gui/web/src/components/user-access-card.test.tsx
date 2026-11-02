// @vitest-environment happy-dom
import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import { BridgeClient } from "../bridge/client";
import { mockDaveAccess } from "../bridge/mock-access";
import { MockTransport } from "../bridge/mock-transport";
import { type AccessReport, MessageType, parseMessage, readReply } from "../bridge/protocol";
import { button, click, field, isDisabled, liveStatus, render, settle, submit, type } from "../test/dom";
import { ScriptedTransport } from "../test/scripted-transport";
import { AccessReportView, describeAge, describeTaken, UserAccessCard } from "./user-access-card";
import { DEBOUNCE_MS } from "./user-picker";

const contracts = path.resolve(__dirname, "../../../../tests/contracts/bridge");

/** The access-report sample's payload, read as the page reads it. */
function sampleReport(): AccessReport {
  const message = parseMessage(readFileSync(path.join(contracts, "access-report.json"), "utf8"));
  const reply = message ? readReply(message) : null;
  if (reply?.type !== MessageType.accessReport) {
    throw new Error("access-report.json is not an access-report reply");
  }
  return reply.data;
}

async function card(transport: MockTransport | ScriptedTransport) {
  const rendered = await render(<UserAccessCard client={new BridgeClient(transport)} />);
  const lookUp = async (user: string) => {
    await type(field(rendered.container, "User"), user);
    await submit(rendered.container.querySelector("form") as HTMLFormElement);
    await settle();
  };
  return { ...rendered, lookUp };
}

/** The column headers of the section with the given title. */
function headers(container: HTMLElement, title: string): string[] {
  const section = [...container.querySelectorAll("section")].find((candidate) =>
    candidate.querySelector(":scope > h3")?.textContent?.startsWith(title),
  );
  return [...(section?.querySelectorAll("th") ?? [])].map((cell) => cell.textContent ?? "");
}

/** The rows of the section with the given title, cell by cell. */
function rows(container: HTMLElement, title: string): string[][] {
  const section = [...container.querySelectorAll("section")].find((candidate) =>
    candidate.querySelector(":scope > h3")?.textContent?.startsWith(title),
  );
  return [...(section?.querySelectorAll("tbody tr") ?? [])].map((row) =>
    [...row.querySelectorAll("td")].map((cell) => cell.textContent ?? ""),
  );
}

describe("the user access card", () => {
  it("answers for a fixture user from the snapshot sample, its date and its warnings in the window's words", async () => {
    const { container, lookUp } = await card(new MockTransport());
    expect(isDisabled(button(container, "Look up"))).toBe(true);
    // The sections are headed a level below the card's heading.
    expect(container.querySelector("h2")?.textContent).toBe("User access");

    await lookUp(" dave@contoso.com ");

    const text = container.textContent ?? "";
    expect(text).toContain("Dave Exampledave@contoso.com");
    expect(container.querySelector('[data-testid="answer-for"]')?.textContent).toBe('Answer for "dave@contoso.com"');
    expect(liveStatus(container)).toBe(
      "dave@contoso.com: 4 groups, 2 sites, 3 shared files, 1 mailbox permission, 1 calendar permission",
    );
    expect(container.querySelectorAll("h3")).toHaveLength(5);
    expect(container.querySelector('[data-testid="snapshot"]')?.textContent).toBe(
      "From the snapshot taken 2026-10-31 06:00 UTC, 2 hours ago.",
    );
    expect(rows(container, "Groups")).toEqual([
      ["Finance", "Microsoft 365", "Through Finance EU"],
      ["Finance EU", "Security", "Direct"],
      ["Finance Staff", "Mail-enabled security", "Through Finance EU"],
      ["Project Phoenix", "Microsoft 365", "Direct"],
    ]);
    expect(rows(container, "Sites")).toEqual([
      ["Finance", "https://contoso.sharepoint.com/sites/finance", "Member of Finance"],
      ["Project Phoenix", "https://contoso.sharepoint.com/sites/phoenix", "Member of Project Phoenix"],
    ]);
    expect(rows(container, "Shared files")).toEqual([
      ["Finance: /Reports", "Shared directly · View", "Finance EU"],
      ["Project Phoenix: /Plans/Roadmap.pptx", "Link: specific people · View", "Finance Staff"],
      ["Project Phoenix: /Plans/Roadmap.pptx", "Shared directly · View", "Direct"],
    ]);
    expect(rows(container, "Mailboxes")).toEqual([
      ["Reception", "reception@contoso.com", "Full Access", "Finance Staff"],
    ]);
    expect(headers(container, "Sites")).toEqual(["Site", "URL", "Through"]);
    expect(headers(container, "Mailboxes")).toEqual(["Mailbox", "Email address", "Permission", "Through"]);
    expect(rows(container, "Calendars")).toEqual([
      ["Carol Example", "carol@contoso.com", "Calendar: Reviewer", "Finance Staff"],
    ]);
    expect(container.querySelector('[data-testid="everyone"]')?.textContent).toBe(
      "Also open to everyone, including this user: 1 link anyone in your organization can open, 1 link anyone can open, and 1 calendar that shows more than free/busy to the whole organization (1 to anonymous users).",
    );
    expect(text).toContain(
      "SharePoint sites: read only in part in this snapshot; 1 item could not be read (forbidden 1), so the answer may miss what they hold. The administrator can see which ones in the snapshot's details.",
    );
    // The skipped part's note in the window's words (task 118), not the take's.
    expect(text).toContain(
      "OneDrive: not read in this snapshot; other people's OneDrive files are not in the snapshot; ask the administrator if you need them.",
    );
    expect(text).not.toMatch(/Sites\.Read\.All|task \d{3}/);
    expect(text).not.toContain("`");
    expect(container.querySelector('[role="alert"]')).toBeNull();
  });

  it("looks up the user picked from the suggestions by the user principal name (task 112)", async () => {
    const transport = new MockTransport();
    const sent: string[] = [];
    const record = transport.send.bind(transport);
    transport.send = (message) => {
      sent.push(`${message.type} ${JSON.stringify(message.data)}`);
      record(message);
    };
    const { container } = await card(transport);

    await type(field(container, "User"), "Dav");
    await settle(DEBOUNCE_MS);
    const option = container.querySelector('[role="option"]') as HTMLElement;
    // The name and the address are two words to a screen reader.
    expect(option.textContent).toBe("Dave Example dave@contoso.com");
    expect(liveStatus(container)).toBe("1 matching user: use the arrow keys to choose one.");
    await click(option);
    await settle();

    expect(sent).toEqual(['user-search {"text":"Dav"}', 'access {"user":"dave@contoso.com"}']);
    expect(container.querySelector('[data-testid="snapshot"]')).not.toBeNull();
    expect(container.querySelector('[role="alert"]')).toBeNull();
  });

  it("says plainly that there is no snapshot, and who takes one", async () => {
    const { container, lookUp } = await card(new MockTransport({ snapshot: false }));

    await lookUp("dave@contoso.com");

    expect(container.textContent).toContain("No snapshot yet");
    expect(liveStatus(container)).toBe("No snapshot yet");
    expect(container.textContent).toContain(
      "The card answers from a snapshot of the tenant, and this computer has none for the profile contoso.",
    );
    expect(container.textContent).toContain(
      "Ask the administrator to take a snapshot on this computer, or to schedule one every morning.",
    );
    expect(container.querySelector("table")).toBeNull();
  });

  it("shows an unknown user under the field and drops the old answer (task 120)", async () => {
    const { container, lookUp } = await card(new MockTransport());
    await lookUp("dave@contoso.com");

    await lookUp("nobody@contoso.com");

    const alert = container.querySelector('[role="alert"]') as HTMLElement;
    expect(alert.textContent).toContain("The lookup failed");
    // The core's sentence in the window's words: a sentence, the snapshot by its time (task 122).
    expect(alert.textContent).toContain("The snapshot of 2026-10-31 06:00 UTC knows no user 'nobody@contoso.com'.");
    expect(alert.textContent).toContain("What to do: Check the name or the address and try again.");
    const input = field(container, "User");
    expect(input.getAttribute("aria-invalid")).toBe("true");
    expect(input.getAttribute("aria-describedby")).toBe(alert.id);
    expect(container.textContent).not.toContain("Dave Example");
  });

  it("waits for the host while it looks up, and ignores a reply that is no access answer", async () => {
    const transport = new ScriptedTransport((message) =>
      (message.data as { user: string }).user === "dave@contoso.com"
        ? { type: MessageType.pong, data: { product: "m365-aegis", version: "1" } }
        : null,
    );
    const { container, lookUp } = await card(transport);

    await lookUp("dave@contoso.com");
    expect(isDisabled(button(container, "Look up"))).toBe(false);
    expect(container.querySelector("section section")).toBeNull();

    await lookUp("carol@contoso.com");
    expect(isDisabled(button(container, "Looking up…"))).toBe(true);
    expect(field(container, "User").readOnly).toBe(true);
    expect(transport.sent.map((message) => message.data)).toEqual([
      { user: "dave@contoso.com" },
      { user: "carol@contoso.com" },
    ]);
    expect(container.querySelector('[role="alert"]')).toBeNull();
    // An empty field sends nothing.
    const empty = await card(transport);
    await empty.lookUp("  ");
    expect(transport.sent).toHaveLength(2);
  });

  it("dims the old answer while the next lookup runs", async () => {
    const transport = new MockTransport();
    const { container, lookUp } = await card(transport);
    await lookUp("dave@contoso.com");
    transport.send = () => {};
    await lookUp("carol@contoso.com");
    const shown = container.querySelector('[data-testid="answer-for"]')?.closest('[aria-busy="true"]');
    expect(shown?.className).toContain("opacity-50");
  });
});

describe("the access view", () => {
  it("is the mock's answer: the browser mock answers dave with the sample's payload", () => {
    expect(mockDaveAccess).toEqual(sampleReport());
  });

  it("says when a section is empty, and names an address no directory account stands for", async () => {
    const report: AccessReport = {
      ...sampleReport(),
      user: { displayName: "Partner Example", mail: "partner@fabrikam.com" },
      groups: [],
      sites: [],
      sharedItems: [{ site: "Finance", kind: "link", expires: "2026-12-31" }],
      mailboxes: [],
      calendars: [],
      warnings: [],
    };
    const { container } = await render(<AccessReportView report={report} />);

    expect(container.textContent).toContain("Partner Examplepartner@fabrikam.comNot in the directory");
    expect(container.textContent).toContain("In no group.");
    expect(container.textContent).toContain("No site through a group.");
    expect(container.textContent).toContain("No permissions on other mailboxes.");
    expect(container.textContent).toContain("No permissions on other calendars.");
    expect(rows(container, "Shared files")).toEqual([["Finance: /", "Link · until 2026-12-31", "Direct"]]);
    expect(container.textContent).not.toContain("Good to know");
  });

  it("names a user without a display name by the first name the snapshot has, and a group by its id", async () => {
    const base = sampleReport();
    const report: AccessReport = {
      ...base,
      user: { objectId: "00000000-0000-0000-0000-000000000005" },
      groups: [{ id: "00000000-0000-0000-0000-000000000031", via: [], direct: true }],
      sites: [{ name: "Finance", group: "Finance", role: "owner" }],
      sharedItems: [],
      everyone: { organizationLinks: 2, anonymousLinks: 0, defaultCalendars: 3, anonymousCalendars: 0 },
    };
    const { container } = await render(<AccessReportView report={report} />);

    expect(container.querySelector("strong")?.textContent).toBe("00000000-0000-0000-0000-000000000005");
    expect(rows(container, "Groups")).toEqual([["00000000-0000-0000-0000-000000000031", "", "Direct"]]);
    expect(rows(container, "Sites")).toEqual([["Finance", "", "Owner of Finance"]]);
    expect(container.textContent).toContain("No file or folder shared with the user or the groups.");
    expect(container.querySelector('[data-testid="everyone"]')?.textContent).toBe(
      "Also open to everyone, including this user: 2 links anyone in your organization can open, 0 links anyone can open, and 3 calendars that show more than free/busy to the whole organization (0 to anonymous users).",
    );
  });

  it("writes the snapshot's time in UTC and its age in hours, then in days", () => {
    expect(describeTaken("2026-10-31T07:30:00+01:00")).toBe("2026-10-31 06:30 UTC");
    expect(describeTaken("yesterday")).toBe("yesterday");
    expect(describeAge(40.4)).toBe("40.4 hours ago");
    expect(describeAge(1)).toBe("1 hour ago");
    expect(describeAge(72)).toBe("3.0 days ago");
  });
});
