// @vitest-environment happy-dom
import { readdirSync, readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import { BridgeClient } from "../bridge/client";
import { MockTransport } from "../bridge/mock-transport";
import { type BridgeMessage, type Plan, parseMessage, readReply } from "../bridge/protocol";
import { button, click, render, settle, submit, type } from "../test/dom";
import { MailboxForwardingCard } from "./mailbox-write-cards";
import { ConfirmView, ErrorText, PlanView, ResultView } from "./plan-view";
import { snapshotPart, snapshotPartNames, windowWarning } from "./plan-wording";
import { AccessReportView, NoSnapshotView, UserAccessCard } from "./user-access-card";

/** A flag of the command line as a card would show it: `--keep-copy`, `(--yes)`. */
const flag = /(^|[\s("'`])--[a-z]/;

/**
 * A command of the command line as a card would show it (task 104): `m365-aegis snapshot take`, or a command in
 * backticks as the core writes one into a sentence (`group owners-missing`); the product's name alone is no command.
 */
const command = /\bm365-aegis\s+[a-z]|`/;

const repo = path.resolve(__dirname, "../../../..");

const contracts = path.join(repo, "tests/contracts/bridge");

/** A reply sample of the bridge (tests/contracts/bridge), read as the page reads it. */
function reply(name: string) {
  const message = parseMessage(readFileSync(path.join(contracts, name), "utf8"));
  const read = message ? readReply(message) : null;
  if (!read) {
    throw new Error(`${name} is not a reply the page reads`);
  }
  return read;
}

interface CoreWarning {
  /** The source file under src/M365Aegis.Core. */
  file: string;
  /** The warning's text as the source writes it, its holes (`{row.To}`, `{0}`) left in. */
  template: string;
}

/**
 * Every warning the core can put into a plan, from its sources: each `PlanBuilder.Warn` and the resume's own
 * `warnings.Add`, a plain, interpolated or `string.Format` text.
 */
function coreWarnings(): CoreWarning[] {
  const root = path.join(repo, "src/M365Aegis.Core");
  const call =
    /(?:\.Warn|warnings\.Add)\(\s*(?:string\.Format\(\s*CultureInfo\.InvariantCulture,\s*)?\$?"((?:[^"\\]|\\.)*)"/g;
  return readdirSync(root, { recursive: true, encoding: "utf8" })
    .filter((file) => file.endsWith(".cs"))
    .sort()
    .flatMap((file) =>
      [...readFileSync(path.join(root, file), "utf8").matchAll(call)].map((match) => ({
        file: file.replaceAll("\\", "/"),
        template: match[1],
      })),
    );
}

/** A warning as the core would write it: each hole filled with a made-up value. */
const filled = (template: string) => template.replace(/\{[^{}]*\}/g, "Finance");

/**
 * The core's writes whose plans reach a card: the mailbox writes the window plans over (task 082, the bridge's write
 * requests), and the group writes, whose card comes later and meets the wording of task 104 already.
 */
const windowSources = ["Exchange/MailboxWrites.cs", "Groups/GroupWrites.cs"];

/**
 * The access view's warnings (task 084), which the user access card shows (task 102): the old snapshot's
 * `warnings.Add`, and the domain warnings of the `switch`, each `=> $"…"`.
 */
function accessWarnings(): string[] {
  const source = readFileSync(path.join(repo, "src/M365Aegis.Core/Snapshots/SnapshotAccess.cs"), "utf8");
  const added = /warnings\.Add\(\s*string\.Format\(\s*CultureInfo\.InvariantCulture,\s*"((?:[^"\\]|\\.)*)"/g;
  const switched = /=> \$"((?:[^"\\]|\\.)*)"/g;
  return [...source.matchAll(added), ...source.matchAll(switched)].map((match) => match[1]);
}

/** The parts of the take (task 113), from its source: each `DomainAsync("…"` and the skipped part's `new SnapshotDomain("…"`. */
function takeParts(): string[] {
  const source = readFileSync(path.join(repo, "src/M365Aegis.Core/Snapshots/SnapshotTake.cs"), "utf8");
  return [...source.matchAll(/(?:DomainAsync|new SnapshotDomain)\(\s*"(\w+)"/g)].map((match) => match[1]);
}

/**
 * A part of the snapshot named by the store's word as a warning starts with it (task 113): `sites is partial`,
 * `grants could not be read`, `oneDrive was not read`.
 */
const bareDomain = new RegExp(`\\b(${takeParts().join("|")}) (is partial|could not be read|was not read)\\b`);

describe("a plan warning in the window's words", () => {
  it("rewords the forwarding without a copy for the card's box", () => {
    expect(
      windowWarning("Alice Example keeps no copy: the mail reaches alice@fabrikam.com only (--keep-copy keeps one)"),
    ).toBe(
      'Alice Example keeps no copy: the mail reaches alice@fabrikam.com only. To keep one in the mailbox too, choose "Change the picks" and tick "Keep a copy in the mailbox".',
    );
  });

  it("rewords the group owner write's last owner for what the colleague can do in the window (task 104)", () => {
    expect(
      windowWarning(
        "Finance loses its last owner: only an administrator can manage it afterwards, and `group owners-missing` lists it",
      ),
    ).toBe(
      "Finance loses its last owner: only an administrator can manage it afterwards. To keep it managed, add another owner first; otherwise the administrator's list of groups without an owner names it.",
    );
  });

  it("rewords the access view's warnings for the snapshot the administrator takes (task 102)", () => {
    expect(
      windowWarning("the snapshot is 40.4 hours old, older than 36: run `m365-aegis snapshot take` for a fresh one"),
    ).toBe(
      "The snapshot is 40.4 hours old, older than 36 hours, so what it shows may have changed since. Ask the administrator for a fresh one.",
    );
    expect(
      windowWarning(
        "sharing is partial in this snapshot: 3 item(s) could not be read (forbidden 2, throttled 1); `m365-aegis snapshot show` names them",
      ),
    ).toBe(
      "Shared files: read only in part in this snapshot; 3 item(s) could not be read (forbidden 2, throttled 1), so the answer may miss what they hold. The administrator can see which ones in the snapshot's details.",
    );
    expect(
      windowWarning("grants could not be read in this snapshot; `m365-aegis snapshot show` names the failure"),
    ).toBe(
      "Mailbox rights: could not be read in this snapshot, so the answer has none of it. The administrator can see why in the snapshot's details.",
    );
    expect(windowWarning("oneDrive was not read: other users' OneDrives need a consent")).toBe(
      "OneDrive: not read in this snapshot; other users' OneDrives need a consent.",
    );
  });

  it("names every part of the take in the window's words, never the store's identifier (task 113)", () => {
    const parts = takeParts();
    // A new part of the take fails here: name it in `snapshotPartNames` (plan-wording.ts).
    expect([...parts].sort()).toEqual(Object.keys(snapshotPartNames).sort());
    expect(parts).toHaveLength(13);
    expect(snapshotPart("sites")).toBe("SharePoint sites");
    expect(snapshotPart("oneDrive")).toBe("OneDrive");
    expect(snapshotPart("constructor")).toBe("constructor");

    for (const part of parts) {
      for (const template of accessWarnings().filter((warning) => warning.startsWith("{domain.Name}"))) {
        const worded = windowWarning(filled(template.replace("{domain.Name}", part)));
        expect(worded.startsWith(`${snapshotPartNames[part]}: `)).toBe(true);
        expect(worded).not.toMatch(bareDomain);
      }
    }
  });

  it("passes a warning it does not know through unchanged", () => {
    const outside =
      "alice@fabrikam.com is outside the accepted domains: the mail leaves the organization, and the outbound spam filter policy blocks automatic forwarding to an external address unless the policy allows it";
    expect(windowWarning(outside)).toBe(outside);
    expect(windowWarning("a new warning of the core (--some-flag)")).toBe("a new warning of the core (--some-flag)");
    expect(windowWarning("Finance keeps no copy (--keep-copy keeps one)")).toBe(
      "Finance keeps no copy (--keep-copy keeps one)",
    );
  });

  it("knows every warning of the core that names a flag: the window's are reworded, the rest is the command line's", () => {
    const warnings = coreWarnings();
    expect(warnings.length).toBeGreaterThanOrEqual(10);

    const naming = warnings.filter((warning) => flag.test(warning.template));
    // A new warning that names a flag fails here: reword it in plan-wording.ts when a card can plan it.
    expect(naming.map((warning) => [warning.file, warning.template.match(/--[a-z-]+/)?.[0]])).toEqual([
      ["Exchange/MailboxWrites.cs", "--keep-copy"],
      ["Operations/PlanPrune.cs", "--resume"],
    ]);

    const onCards = warnings.filter((warning) => windowSources.includes(warning.file));
    expect(onCards.length).toBeGreaterThanOrEqual(9);
    for (const { template } of onCards) {
      expect(windowWarning(filled(template))).not.toMatch(flag);
    }
  });

  it("knows every warning of the core that names a command: the cards' are reworded (task 104)", () => {
    const naming = coreWarnings().filter((warning) => command.test(warning.template));
    // A new warning that names a command fails here: reword it in plan-wording.ts when a card can show it. `plan
    // prune`'s is the command line's own; the access view's old snapshot is the user access card's (task 102).
    expect(naming.map((warning) => [warning.file, warning.template.match(/`[^`]+`/)?.[0]])).toEqual([
      ["Groups/GroupWrites.cs", "`group owners-missing`"],
      ["Operations/PlanPrune.cs", "`m365-aegis plan show {0}`"],
      ["Snapshots/SnapshotAccess.cs", "`m365-aegis snapshot take`"],
    ]);

    const onCards = coreWarnings().filter((warning) => windowSources.includes(warning.file));
    for (const { template } of [...onCards, ...accessWarnings().map((template) => ({ template }))]) {
      expect(windowWarning(filled(template))).not.toMatch(command);
    }
    // The old snapshot, a partial part, a failed part, and a part not read with the take's own note.
    expect(accessWarnings()).toHaveLength(4);
  });
});

describe("no card renders a command of the command line (task 104)", () => {
  /** Every string of a payload with where it sits, the warnings as the card words them. */
  function shown(value: unknown, at: string): Array<[string, string]> {
    if (typeof value === "string") {
      return [[at, at.includes(".warnings.") ? windowWarning(value) : value]];
    }
    if (typeof value === "object" && value !== null) {
      return Object.entries(value).flatMap(([key, item]) => shown(item, `${at}.${key}`));
    }
    return [];
  }

  it("over every reply sample of the bridge: no string a card shows names a flag or a command", () => {
    const replies = readdirSync(contracts)
      .filter((name) => name.endsWith(".json"))
      .map((name) => [name, parseMessage(readFileSync(path.join(contracts, name), "utf8")) as BridgeMessage] as const)
      .filter(([, message]) => readReply(message) !== null);
    expect(replies.map(([name]) => name)).toEqual(
      expect.arrayContaining(["access-report.json", "no-snapshot.json", "plan.group-owners-remove.json", "plan.json"]),
    );

    const offending = replies
      .flatMap(([name, message]) => shown(message.data, name))
      .filter(([, text]) => flag.test(text) || command.test(text) || bareDomain.test(text));
    expect(offending).toEqual([]);
  });

  it("over the group write plan's sample, the access samples and the browser mock's access card", async () => {
    const planned = reply("plan.group-owners-remove.json");
    const report = reply("access-report.json");
    const none = reply("no-snapshot.json");
    if (planned.type !== "plan" || report.type !== "access-report" || none.type !== "no-snapshot") {
      throw new Error("the samples changed their types");
    }
    const old = {
      ...report.data,
      warnings: accessWarnings().map(filled),
    };

    const views = [
      await render(<PlanView plan={planned.data.plan} />),
      await render(<ConfirmView plan={planned.data.plan} onConfirm={() => {}} onBack={() => {}} />),
      await render(<AccessReportView report={old} />),
      await render(<NoSnapshotView info={none.data} />),
      await render(<UserAccessCard client={new BridgeClient(new MockTransport())} onError={() => {}} />),
    ];
    const card = views[4].container;
    await type(card.querySelector('input[aria-label="User"]') as HTMLInputElement, "dave@contoso.com");
    await submit(card.querySelector("form") as HTMLFormElement);
    await settle();

    expect(views[0].container.textContent).toContain("To keep it managed, add another owner first");
    expect(views[2].container.textContent).toContain("Ask the administrator for a fresh one.");
    expect(card.textContent).toContain("The administrator can see which ones in the snapshot's details.");
    // The parts of the snapshot in the window's words (task 113).
    expect(card.textContent).toContain("SharePoint sites: read only in part in this snapshot;");
    expect(card.textContent).toContain("OneDrive: not read in this snapshot;");
    for (const view of views) {
      expect(view.container.textContent).not.toMatch(flag);
      expect(view.container.textContent).not.toMatch(command);
      expect(view.container.textContent).not.toMatch(bareDomain);
      await view.unmount();
    }
  });
});

describe("no card renders a flag of the command line", () => {
  /** Every warning a mailbox write of the core can carry, as the window would receive it. */
  const mailboxWarnings = coreWarnings()
    .filter((warning) => windowSources.includes(warning.file))
    .map((warning) => filled(warning.template));

  it("over the bridge samples: the plan, its confirmation, the applied plan and an error", async () => {
    const planned = reply("plan.json");
    const applied = reply("plan-applied.json");
    const failed = reply("error.json");
    if (planned.type !== "plan" || applied.type !== "plan-applied" || failed.type !== "error") {
      throw new Error("the samples changed their types");
    }
    const withWarnings = (plan: Plan): Plan => ({ ...plan, warnings: [...plan.warnings, ...mailboxWarnings] });

    const views = [
      await render(<PlanView plan={withWarnings(planned.data.plan)} />),
      await render(<ConfirmView plan={planned.data.plan} onConfirm={() => {}} onBack={() => {}} />),
      await render(<ResultView applied={{ ...applied.data, plan: withWarnings(applied.data.plan) }} />),
      await render(<ErrorText error={failed.data} />),
    ];

    expect(views[0].container.textContent).toContain('tick "Keep a copy in the mailbox"');
    for (const view of views) {
      expect(view.container.textContent).not.toMatch(flag);
      expect(view.container.textContent).not.toMatch(command);
      await view.unmount();
    }
  });

  it("over the browser mock: a forwarding without a copy, from the picks to the result", async () => {
    const { container } = await render(
      <MailboxForwardingCard client={new BridgeClient(new MockTransport())} signedIn />,
    );
    const text = () => container.textContent ?? "";
    const seen: string[] = [text()];

    await type(container.querySelector('input[aria-label="Mailbox"]') as HTMLInputElement, "alice@contoso.com");
    await type(container.querySelector('input[aria-label="Forward to"]') as HTMLInputElement, "alice@fabrikam.com");
    await click(container.querySelector("label input") as HTMLInputElement);
    await click(button(container, "See the plan"));
    await settle();
    seen.push(text());
    expect(text()).toContain(
      'Alice Example keeps no copy: the mail reaches alice@fabrikam.com only. To keep one in the mailbox too, choose "Change the picks" and tick "Keep a copy in the mailbox".',
    );

    await click(button(container, "Continue to confirm"));
    seen.push(text());
    await click(container.querySelector("label input") as HTMLInputElement);
    await click(button(container, "Confirm and apply"));
    await settle();
    seen.push(text());
    expect(text()).toContain("Every change was made.");

    for (const shown of seen) {
      expect(shown).not.toMatch(flag);
      expect(shown).not.toMatch(command);
    }
  });
});
