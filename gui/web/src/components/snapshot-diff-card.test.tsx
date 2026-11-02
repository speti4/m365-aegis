// @vitest-environment happy-dom
import { readFileSync } from "node:fs";
import path from "node:path";
import { act } from "react";
import { describe, expect, it } from "vitest";
import { BridgeClient } from "../bridge/client";
import { mockBobSnapshotDiff, mockSnapshotDiff, mockTooFewSnapshots } from "../bridge/mock-snapshot-diff";
import { MockTransport } from "../bridge/mock-transport";
import { MessageType, parseMessage, readReply, type SnapshotDiffReport } from "../bridge/protocol";
import { button, isDisabled, field as labeled, liveStatus, render, settle, submit, type } from "../test/dom";
import { ScriptedTransport } from "../test/scripted-transport";
import { diffPart } from "./plan-wording";
import { SnapshotDiffCard, SnapshotDiffView } from "./snapshot-diff-card";
import { DEBOUNCE_MS } from "./suggest-field";

const contracts = path.resolve(__dirname, "../../../../tests/contracts/bridge");

/** A reply sample's payload, read as the page reads it. */
function samplePayload(name: string): unknown {
  const message = parseMessage(readFileSync(path.join(contracts, name), "utf8"));
  const reply = message ? readReply(message) : null;
  if (!reply) {
    throw new Error(`${name} is no reply the page reads`);
  }
  return reply.data;
}

async function card(transport: MockTransport | ScriptedTransport) {
  const rendered = await render(<SnapshotDiffCard client={new BridgeClient(transport)} />);
  const field = (label: string) => labeled(rendered.container, label);
  const compare = async (picks: { from?: string; to?: string; user?: string } = {}) => {
    await type(field("Compare from"), picks.from ?? "");
    await type(field("Compare to"), picks.to ?? "");
    await type(field("Only this user"), picks.user ?? "");
    await submit(rendered.container.querySelector("form") as HTMLFormElement);
    await settle();
  };
  return { ...rendered, field, compare };
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

const titles = (container: HTMLElement) =>
  [...container.querySelectorAll("section h3")].map((heading) => heading.textContent ?? "");

describe("the access changes card", () => {
  it("compares the newest snapshot with the one before it: one added and one removed row per part", async () => {
    const { container, compare } = await card(new MockTransport());

    await compare();
    expect(liveStatus(container)).toBe("8 added, 6 removed between the two snapshots");

    expect(container.querySelector('[data-testid="snapshots"]')?.textContent).toBe(
      "From the snapshot taken 2026-10-31 06:00 UTC (26 hours ago) to the one taken 2026-11-01 06:00 UTC (2 hours ago): 8 added, 6 removed.",
    );
    expect(titles(container)).toEqual([
      "Group members · 3 added, 1 removed",
      "Group owners · 1 added, 1 removed",
      "Shared files · 1 added, 1 removed",
      "Mailbox permissions · 1 added, 1 removed",
      "Calendar permissions · 1 added, 1 removed",
      "Mail forwarding · 1 added, 1 removed",
    ]);
    // How each row holds what it holds, in the window's words (task 122), never the core's values.
    expect(rows(container, "Group members")).toEqual([
      ["Added", "Audit", "bob@contoso.com", "Member"],
      ["Removed", "Finance EU", "FIN-KIOSK-01", "Member (device)"],
      ["Added", "Finance Staff", "Audit", "Member (group)"],
      ["Added", "Project Phoenix", "bob@contoso.com", "Member"],
    ]);
    expect(rows(container, "Group owners").map((row) => row[3])).toEqual(["Owner", "Owner"]);
    expect(rows(container, "Shared files").map((row) => row[3])).toEqual([
      "Link: people in your organization · View",
      "Link: specific people · View",
    ]);
    expect(rows(container, "Mailbox permissions")).toEqual([
      ["Added", "finance@contoso.com", "dave@contoso.com", "Full Access"],
      ["Removed", "reception@contoso.com", "Finance Staff", "Full Access"],
    ]);
    expect(rows(container, "Calendar permissions").map((row) => row[3])).toEqual(["Free/busy only", "Reviewer"]);
    expect(rows(container, "Mail forwarding").map((row) => row[3])).toEqual([
      "To a recipient, keeps a copy",
      "To an email address, outside your organization, keeps a copy",
    ]);
    // A snapshot by its time, never its file (task 122).
    expect(container.textContent).toContain(
      "SharePoint sites: read only in part in the snapshot of 2026-10-31 06:00 UTC (1 item could not be read), so rows of Shared files may show as added or removed without a change.",
    );
    expect(container.textContent).not.toContain(".sqlite");
    expect(container.querySelector('[data-testid="user"]')).toBeNull();
    expect(container.querySelector('[data-testid="unchanged"]')).toBeNull();
    expect(container.textContent).not.toContain("Through");
    expect(container.querySelector('[role="alert"]')).toBeNull();
  });

  it("narrows the changes to one user, with the group each row comes through", async () => {
    const { container, compare } = await card(new MockTransport());

    await compare({ user: " BOB@contoso.com " });

    expect(container.querySelector('[data-testid="user"]')?.textContent).toBe("Bob Examplebob@contoso.com");
    expect(rows(container, "Groups")).toEqual([
      ["Added", "Audit", "bob@contoso.com", "Member", "Direct"],
      ["Added", "Finance Staff", "bob@contoso.com", "Member", "Audit"],
      ["Added", "Project Phoenix", "bob@contoso.com", "Member", "Direct"],
    ]);
    expect(rows(container, "Calendar permissions")).toEqual([
      ["Added", "carol@contoso.com: Calendar", "Finance Staff", "Reviewer", "Finance Staff"],
    ]);
    expect(container.querySelector('[data-testid="unchanged"]')?.textContent).toBe(
      "No change in: Group owners, Mailboxes.",
    );
  });

  it("says plainly that there is nothing to compare yet, and who takes the snapshots", async () => {
    const { container, compare } = await card(new MockTransport({ snapshot: false }));

    await compare();

    expect(container.textContent).toContain("Nothing to compare yet");
    expect(liveStatus(container)).toBe("Nothing to compare yet");
    expect(container.textContent).toContain("No snapshot of the profile 'contoso' yet.");
    expect(container.textContent).toContain(
      "The card compares two snapshots: ask the administrator to take one every morning",
    );
    expect(container.querySelector("table")).toBeNull();
  });

  it("shows a user neither snapshot knows under the fields and drops the old answer (task 120)", async () => {
    const { container, field, compare } = await card(new MockTransport());
    await compare();
    expect(titles(container)).toHaveLength(6);

    await compare({ user: "nobody@contoso.com" });

    const alert = container.querySelector('[role="alert"]') as HTMLElement;
    expect(alert.textContent).toContain("The comparison failed");
    expect(alert.textContent).toContain(
      "Neither the snapshot of 2026-10-31 06:00 UTC nor the snapshot of 2026-11-01 06:00 UTC knows a user 'nobody@contoso.com'.",
    );
    expect(field("Only this user").getAttribute("aria-invalid")).toBe("true");
    // From is described by the line of what it takes, and by the error.
    const hint = container.querySelector('[data-testid="sides-hint"]') as HTMLElement;
    expect(field("Compare from").getAttribute("aria-describedby")).toBe(`${hint.id} ${alert.id}`);
    expect(titles(container)).toHaveLength(0);
  });

  it("sends only the picks it was given, waits for the host, and ignores a reply that is no diff answer", async () => {
    const transport = new ScriptedTransport((message) =>
      (message.data as { user?: string }).user === undefined
        ? { type: MessageType.pong, data: { product: "m365-aegis", version: "1" } }
        : null,
    );
    const { container, field, compare } = await card(transport);

    await compare({ from: " yesterday ", to: " " });
    expect(isDisabled(button(container, "Compare"))).toBe(false);
    expect(container.querySelector("section section")).toBeNull();
    expect(liveStatus(container)).toBe("");

    await compare({ from: "", to: "latest", user: "bob@contoso.com" });
    expect(isDisabled(button(container, "Comparing…"))).toBe(true);
    expect(field("Compare to").readOnly).toBe(true);
    expect(liveStatus(container)).toBe("Comparing the snapshots…");
    expect(transport.sent.map((message) => message.data)).toEqual([
      { from: "yesterday" },
      { to: "latest", user: "bob@contoso.com" },
    ]);
    expect(container.querySelector('[role="alert"]')).toBeNull();
  });

  it("dims the old answer while the next comparison runs", async () => {
    const transport = new MockTransport();
    const { container, compare } = await card(transport);
    await compare();
    transport.send = () => {};
    await compare();
    expect(container.querySelector('[aria-busy="true"] [data-testid="snapshots"]')).not.toBeNull();
    expect(container.querySelector('[data-testid="snapshots"]')?.closest(".opacity-50")).not.toBeNull();
  });
});

describe("the changes view", () => {
  it("is the mock's answer: the browser mock answers with the samples' payloads", () => {
    expect(mockSnapshotDiff).toEqual(samplePayload("snapshot-diff-report.json"));
    expect(mockBobSnapshotDiff).toEqual(samplePayload("snapshot-diff-report.user.json"));
    expect(mockTooFewSnapshots).toEqual(samplePayload("too-few-snapshots.json"));
  });

  it("names the parts left out, a part it does not know by the core's name, and a user by the first name it has", async () => {
    const report: SnapshotDiffReport = {
      ...mockBobSnapshotDiff,
      user: { objectId: "00000000-0000-0000-0000-000000000003" },
      domains: [
        { name: "groups", compared: false, added: 0, removed: 0 },
        { name: "licenses", compared: true, added: 1, removed: 0 },
      ],
      changes: [{ domain: "licenses", change: "added", what: "E5", who: "bob@contoso.com", how: "assigned" }],
      warnings: [
        "groups is left out: userGroups was not read in 2026-11-01T060000Z.sqlite; members was not read in 2026-10-31T060000Z.sqlite",
        "userGroups is partial in 2026-11-01T060000Z.sqlite (items not read: unknown): the groups, sites, sharedItems, mailboxes and calendars rows of those items may show as removed or added",
      ],
    };
    const { container } = await render(<SnapshotDiffView report={report} />);

    expect(container.textContent).toContain(
      "Groups: not compared; Nested group memberships not read in the snapshot of 2026-11-01 06:00 UTC; Group members not read in the snapshot of 2026-10-31 06:00 UTC.",
    );
    expect(container.textContent).toContain(
      "Nested group memberships: read only in part in the snapshot of 2026-11-01 06:00 UTC (some items could not be read), so rows of Groups, SharePoint sites, Shared files, Mailboxes, Calendar permissions may show as added or removed without a change.",
    );

    expect(container.querySelector('[data-testid="user"]')?.textContent).toBe("00000000-0000-0000-0000-000000000003");
    expect(titles(container)).toEqual(["licenses · 1 added, 0 removed"]);
    expect(rows(container, "licenses")).toEqual([["Added", "E5", "bob@contoso.com", "assigned", "Direct"]]);
    expect(container.querySelector('[data-testid="left-out"]')?.textContent).toBe(
      'Not compared: Groups (see "Good to know").',
    );
  });

  it("names an address no directory account stands for by the address, and says when nothing changed", async () => {
    const report: SnapshotDiffReport = {
      ...mockBobSnapshotDiff,
      user: { displayName: "Partner Example", mail: "partner@fabrikam.com" },
      changes: [],
      added: 0,
      removed: 0,
      domains: mockBobSnapshotDiff.domains.map((domain) => ({ ...domain, added: 0, removed: 0 })),
      warnings: [],
    };
    const nameless: SnapshotDiffReport = { ...report, user: { displayName: "Nobody" } };
    const { container } = await render(<SnapshotDiffView report={report} />);
    const second = await render(<SnapshotDiffView report={nameless} />);

    expect(container.querySelector('[data-testid="user"]')?.textContent).toBe("Partner Examplepartner@fabrikam.com");
    expect(container.querySelector('[data-testid="unchanged"]')?.textContent).toBe(
      "No change in: Groups, Group owners, SharePoint sites, Shared files, Mailboxes, Calendar permissions, Mail forwarding.",
    );
    expect(container.querySelector("section")).toBeNull();
    expect(second.container.querySelector('[data-testid="user"]')?.textContent).toBe("Nobody");
    expect([diffPart("sharing"), diffPart("sharedItems"), diffPart("calendars")]).toEqual([
      "Shared files",
      "Shared files",
      "Calendar permissions",
    ]);
  });
});

describe("the access changes card's fields and tables (task 126)", () => {
  it("says what Compare from and Compare to take, and picks the user from the suggestions", async () => {
    const transport = new MockTransport();
    const { container, field } = await card(transport);
    const hint = container.querySelector('[data-testid="sides-hint"]') as HTMLElement;
    expect(hint.textContent).toBe(
      "Leave both empty to compare the latest snapshot with the one before it. Each also takes latest, yesterday, a date such as 2026-10-31, or a snapshot's name.",
    );
    expect(field("Compare to").getAttribute("aria-describedby")).toBe(hint.id);
    expect(field("Compare from").placeholder).toBe("Snapshot before the latest");
    expect(field("Compare to").placeholder).toBe("Latest snapshot");

    const user = field("Only this user");
    expect(user.getAttribute("role")).toBe("combobox");
    await type(user, "bob");
    await settle(DEBOUNCE_MS);
    await settle();
    expect(liveStatus(container)).toBe("1 matching user: use the arrow keys to choose one.");
    // Enter on the open list picks; the comparison waits for Enter on a closed one.
    const enter = new KeyboardEvent("keydown", { key: "Enter", bubbles: true, cancelable: true });
    await act(async () => {
      user.dispatchEvent(enter);
    });
    expect(enter.defaultPrevented).toBe(true);
    expect(user.value).toBe("bob@contoso.com");
    await submit(container.querySelector("form") as HTMLFormElement);
    await settle();
    expect(container.querySelector('[data-testid="user"]')?.textContent).toContain("Bob Example");
  });

  it("lays every part's table out in the same fixed columns", async () => {
    const { container } = await render(<SnapshotDiffView report={mockBobSnapshotDiff} />);
    const tables = [...container.querySelectorAll("table")];
    expect(tables.length).toBeGreaterThan(1);
    for (const table of tables) {
      expect(table.className).toContain("table-fixed");
      expect([...table.querySelectorAll("col")].map((column) => column.className)).toEqual([
        "w-24",
        "",
        "w-[28%]",
        "w-[18%]",
        "w-[18%]",
      ]);
    }
    const whole = await render(<SnapshotDiffView report={mockSnapshotDiff} />);
    expect(whole.container.querySelector("table")?.querySelectorAll("col")).toHaveLength(4);
  });
});
