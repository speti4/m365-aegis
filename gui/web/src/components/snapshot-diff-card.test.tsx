// @vitest-environment happy-dom
import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it, vi } from "vitest";
import { BridgeClient } from "../bridge/client";
import { mockBobSnapshotDiff, mockSnapshotDiff, mockTooFewSnapshots } from "../bridge/mock-snapshot-diff";
import { MockTransport } from "../bridge/mock-transport";
import { MessageType, parseMessage, readReply, type SnapshotDiffReport } from "../bridge/protocol";
import { button, render, settle, submit, type } from "../test/dom";
import { ScriptedTransport } from "../test/scripted-transport";
import { partName, SnapshotDiffCard, SnapshotDiffView } from "./snapshot-diff-card";

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
  const onError = vi.fn();
  const rendered = await render(<SnapshotDiffCard client={new BridgeClient(transport)} onError={onError} />);
  const field = (label: string) => rendered.container.querySelector(`input[aria-label="${label}"]`) as HTMLInputElement;
  const compare = async (picks: { from?: string; to?: string; user?: string } = {}) => {
    await type(field("From"), picks.from ?? "");
    await type(field("To"), picks.to ?? "");
    await type(field("Only this user"), picks.user ?? "");
    await submit(rendered.container.querySelector("form") as HTMLFormElement);
    await settle();
  };
  return { ...rendered, onError, compare };
}

/** The rows of the section with the given title, cell by cell. */
function rows(container: HTMLElement, title: string): string[][] {
  const section = [...container.querySelectorAll("section")].find((candidate) =>
    candidate.querySelector("h3")?.textContent?.startsWith(title),
  );
  return [...(section?.querySelectorAll("tbody tr") ?? [])].map((row) =>
    [...row.querySelectorAll("td")].map((cell) => cell.textContent ?? ""),
  );
}

const titles = (container: HTMLElement) =>
  [...container.querySelectorAll("section h3")].map((heading) => heading.textContent ?? "");

describe("the access changes card", () => {
  it("compares the newest snapshot with the one before it: one added and one removed row per part", async () => {
    const { container, compare, onError } = await card(new MockTransport());

    await compare();

    expect(container.querySelector('[data-testid="snapshots"]')?.textContent).toBe(
      "From the snapshot taken 2026-10-31 06:00 UTC (26 hour(s) ago) to the one taken 2026-11-01 06:00 UTC (2 hour(s) ago): 8 added, 6 removed.",
    );
    expect(titles(container)).toEqual([
      "Group members · 3 added, 1 removed",
      "Group owners · 1 added, 1 removed",
      "Shared files · 1 added, 1 removed",
      "Mailbox rights · 1 added, 1 removed",
      "Calendars · 1 added, 1 removed",
      "Forwarding · 1 added, 1 removed",
    ]);
    expect(rows(container, "Group members")).toEqual([
      ["added", "Audit", "bob@contoso.com", "member"],
      ["removed", "Finance EU", "FIN-KIOSK-01", "member (device)"],
      ["added", "Finance Staff", "Audit", "member (group)"],
      ["added", "Project Phoenix", "bob@contoso.com", "member"],
    ]);
    expect(rows(container, "Mailbox rights")).toEqual([
      ["added", "finance@contoso.com", "dave@contoso.com", "FullAccess"],
      ["removed", "reception@contoso.com", "Finance Staff", "FullAccess"],
    ]);
    expect(container.textContent).toContain(
      "sites is partial in 2026-10-31T060000Z.sqlite (1 item(s) not read): the sharing rows of those items may show as removed or added",
    );
    expect(container.querySelector('[data-testid="user"]')).toBeNull();
    expect(container.querySelector('[data-testid="unchanged"]')).toBeNull();
    expect(container.textContent).not.toContain("Through");
    expect(onError).not.toHaveBeenCalled();
  });

  it("narrows the changes to one user, with the group each row comes through", async () => {
    const { container, compare } = await card(new MockTransport());

    await compare({ user: " BOB@contoso.com " });

    expect(container.querySelector('[data-testid="user"]')?.textContent).toBe("Bob Examplebob@contoso.com");
    expect(rows(container, "Groups")).toEqual([
      ["added", "Audit", "bob@contoso.com", "member", "direct"],
      ["added", "Finance Staff", "bob@contoso.com", "member", "Audit"],
      ["added", "Project Phoenix", "bob@contoso.com", "member", "direct"],
    ]);
    expect(rows(container, "Calendars")).toEqual([
      ["added", "carol@contoso.com: Calendar", "Finance Staff", "Reviewer", "Finance Staff"],
    ]);
    expect(container.querySelector('[data-testid="unchanged"]')?.textContent).toBe(
      "No change in: Group owners, Mailboxes.",
    );
  });

  it("says plainly that there is nothing to compare yet, and who takes the snapshots", async () => {
    const { container, compare } = await card(new MockTransport({ snapshot: false }));

    await compare();

    expect(container.textContent).toContain("Nothing to compare yet");
    expect(container.textContent).toContain("No snapshot of the profile 'contoso' yet.");
    expect(container.textContent).toContain("ask the administrator to take one every morning");
    expect(container.querySelector("table")).toBeNull();
  });

  it("raises a user neither snapshot knows as the page's error and keeps what it showed", async () => {
    const { container, compare, onError } = await card(new MockTransport());
    await compare();

    await compare({ user: "nobody@contoso.com" });

    expect(onError).toHaveBeenCalledWith({
      code: "not-found",
      message: "neither 2026-10-31T060000Z.sqlite nor 2026-11-01T060000Z.sqlite knows a user 'nobody@contoso.com'",
      hint: "check the name or the address and try again",
    });
    expect(titles(container)).toHaveLength(6);
  });

  it("sends only the picks it was given, waits for the host, and ignores a reply that is no diff answer", async () => {
    const transport = new ScriptedTransport((message) =>
      (message.data as { user?: string }).user === undefined
        ? { type: MessageType.pong, data: { product: "m365-aegis", version: "1" } }
        : null,
    );
    const { container, compare, onError } = await card(transport);

    await compare({ from: " yesterday ", to: " " });
    expect(button(container, "Compare").disabled).toBe(false);
    expect(container.querySelector("section")).toBeNull();

    await compare({ from: "", to: "latest", user: "bob@contoso.com" });
    expect(button(container, "Comparing…").disabled).toBe(true);
    expect(transport.sent.map((message) => message.data)).toEqual([
      { from: "yesterday" },
      { to: "latest", user: "bob@contoso.com" },
    ]);
    expect(onError).not.toHaveBeenCalled();
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
      warnings: [],
    };
    const { container } = await render(<SnapshotDiffView report={report} />);

    expect(container.querySelector('[data-testid="user"]')?.textContent).toBe("00000000-0000-0000-0000-000000000003");
    expect(titles(container)).toEqual(["licenses · 1 added, 0 removed"]);
    expect(rows(container, "licenses")).toEqual([["added", "E5", "bob@contoso.com", "assigned", "direct"]]);
    expect(container.querySelector('[data-testid="left-out"]')?.textContent).toBe(
      'Not compared: Groups (see "Good to know").',
    );
    expect(container.textContent).not.toContain("Good to know:");
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
      "No change in: Groups, Group owners, Sites, Shared files, Mailboxes, Calendars, Forwarding.",
    );
    expect(container.querySelector("section")).toBeNull();
    expect(second.container.querySelector('[data-testid="user"]')?.textContent).toBe("Nobody");
    expect(partName("sharing")).toBe("Shared files");
  });
});
