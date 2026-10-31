// @vitest-environment happy-dom
import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it, vi } from "vitest";
import { BridgeClient } from "../bridge/client";
import { mockDaveAccess } from "../bridge/mock-access";
import { MockTransport } from "../bridge/mock-transport";
import { type AccessReport, MessageType, parseMessage, readReply } from "../bridge/protocol";
import { button, render, settle, submit, type } from "../test/dom";
import { ScriptedTransport } from "../test/scripted-transport";
import { AccessReportView, describeAge, describeTaken, UserAccessCard } from "./user-access-card";

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
  const onError = vi.fn();
  const rendered = await render(<UserAccessCard client={new BridgeClient(transport)} onError={onError} />);
  const lookUp = async (user: string) => {
    await type(rendered.container.querySelector('input[aria-label="User"]') as HTMLInputElement, user);
    await submit(rendered.container.querySelector("form") as HTMLFormElement);
    await settle();
  };
  return { ...rendered, onError, lookUp };
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

describe("the user access card", () => {
  it("answers for a fixture user from the snapshot sample, its date and its warnings in the window's words", async () => {
    const { container, lookUp, onError } = await card(new MockTransport());
    expect(button(container, "Look up").disabled).toBe(true);

    await lookUp(" dave@contoso.com ");

    const text = container.textContent ?? "";
    expect(text).toContain("Dave Exampledave@contoso.com");
    expect(container.querySelector('[data-testid="snapshot"]')?.textContent).toBe(
      "From the snapshot taken 2026-10-31 06:00 UTC, 2 hour(s) ago.",
    );
    expect(rows(container, "Groups")).toEqual([
      ["Finance", "Microsoft365", "through Finance EU"],
      ["Finance EU", "Security", "direct"],
      ["Finance Staff", "MailEnabledSecurity", "through Finance EU"],
      ["Project Phoenix", "Microsoft365", "direct"],
    ]);
    expect(rows(container, "Sites")).toEqual([
      ["Finance", "https://contoso.sharepoint.com/sites/finance", "member of Finance"],
      ["Project Phoenix", "https://contoso.sharepoint.com/sites/phoenix", "member of Project Phoenix"],
    ]);
    expect(rows(container, "Shared files")).toEqual([
      ["Finance: /Reports", "direct read", "Finance EU"],
      ["Project Phoenix: /Plans/Roadmap.pptx", "link users read", "Finance Staff"],
      ["Project Phoenix: /Plans/Roadmap.pptx", "direct read", "direct"],
    ]);
    expect(rows(container, "Mailboxes")).toEqual([
      ["Reception", "reception@contoso.com", "FullAccess", "Finance Staff"],
    ]);
    expect(rows(container, "Calendars")).toEqual([
      ["Carol Example", "carol@contoso.com", "Calendar: Reviewer", "Finance Staff"],
    ]);
    expect(container.querySelector('[data-testid="everyone"]')?.textContent).toBe(
      "Reaches everyone, this user too: 1 link(s) anyone in the organization can open, 1 link(s) anyone can open, 1 calendar(s) open beyond free/busy to every user and 1 to anonymous.",
    );
    expect(text).toContain(
      "sites is partial in this snapshot: 1 item(s) could not be read (forbidden 1), so the answer may miss what they hold. The administrator can see which ones in the snapshot's details.",
    );
    expect(text).toContain("oneDrive was not read: other users' OneDrives need a consent");
    expect(text).not.toContain("`");
    expect(onError).not.toHaveBeenCalled();
  });

  it("says plainly that there is no snapshot, and who takes one", async () => {
    const { container, lookUp } = await card(new MockTransport({ snapshot: false }));

    await lookUp("dave@contoso.com");

    expect(container.textContent).toContain("No snapshot yet");
    expect(container.textContent).toContain(
      "The card answers from a snapshot of the tenant, and this computer has none for the profile contoso.",
    );
    expect(container.textContent).toContain(
      "ask the administrator to take a snapshot on this computer, or to schedule one every morning",
    );
    expect(container.querySelector("table")).toBeNull();
  });

  it("raises an unknown user as the page's error and keeps what it showed", async () => {
    const { container, lookUp, onError } = await card(new MockTransport());
    await lookUp("dave@contoso.com");

    await lookUp("nobody@contoso.com");

    expect(onError).toHaveBeenCalledWith({
      code: "not-found",
      message: "the snapshot 2026-10-31T060000Z.sqlite knows no user 'nobody@contoso.com'",
      hint: "check the name or the address and try again",
    });
    expect(container.textContent).toContain("Dave Example");
  });

  it("waits for the host while it looks up, and ignores a reply that is no access answer", async () => {
    const transport = new ScriptedTransport((message) =>
      (message.data as { user: string }).user === "dave@contoso.com"
        ? { type: MessageType.pong, data: { product: "m365-aegis", version: "1" } }
        : null,
    );
    const { container, lookUp, onError } = await card(transport);

    await lookUp("dave@contoso.com");
    expect(button(container, "Look up").disabled).toBe(false);
    expect(container.querySelector("section")).toBeNull();

    await lookUp("carol@contoso.com");
    expect(button(container, "Looking up…").disabled).toBe(true);
    expect(transport.sent.map((message) => message.data)).toEqual([
      { user: "dave@contoso.com" },
      { user: "carol@contoso.com" },
    ]);
    expect(onError).not.toHaveBeenCalled();
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

    expect(container.textContent).toContain("Partner Examplepartner@fabrikam.comno directory account");
    expect(container.textContent).toContain("In no group.");
    expect(container.textContent).toContain("No site through a group.");
    expect(container.textContent).toContain("No right on another mailbox.");
    expect(container.textContent).toContain("No permission on another calendar.");
    expect(rows(container, "Shared files")).toEqual([["Finance: /", "link until 2026-12-31", "direct"]]);
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
    };
    const { container } = await render(<AccessReportView report={report} />);

    expect(container.querySelector("strong")?.textContent).toBe("00000000-0000-0000-0000-000000000005");
    expect(rows(container, "Groups")).toEqual([["00000000-0000-0000-0000-000000000031", "", "direct"]]);
    expect(rows(container, "Sites")).toEqual([["Finance", "", "owner of Finance"]]);
    expect(container.textContent).toContain("No file or folder shared with the user or the groups.");
  });

  it("writes the snapshot's time in UTC and its age in hours, then in days", () => {
    expect(describeTaken("2026-10-31T07:30:00+01:00")).toBe("2026-10-31 06:30 UTC");
    expect(describeTaken("yesterday")).toBe("yesterday");
    expect(describeAge(40.4)).toBe("40.4 hour(s) ago");
    expect(describeAge(72)).toBe("3.0 days ago");
  });
});
