// @vitest-environment happy-dom
import { readdirSync, readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import { BridgeClient } from "../bridge/client";
import { MockTransport } from "../bridge/mock-transport";
import { type Plan, parseMessage, readReply } from "../bridge/protocol";
import { button, click, render, settle, type } from "../test/dom";
import { MailboxForwardingCard } from "./mailbox-write-cards";
import { ConfirmView, ErrorText, PlanView, ResultView } from "./plan-view";
import { windowWarning } from "./plan-wording";

/** A flag of the command line as a card would show it: `--keep-copy`, `(--yes)`. */
const flag = /(^|[\s("'`])--[a-z]/;

const repo = path.resolve(__dirname, "../../../..");

/** A reply sample of the bridge (tests/contracts/bridge), read as the page reads it. */
function reply(name: string) {
  const message = parseMessage(readFileSync(path.join(repo, "tests/contracts/bridge", name), "utf8"));
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

/** The core's writes the window plans over (task 082, the bridge's write requests): their warnings reach a card. */
const windowSources = ["Exchange/MailboxWrites.cs"];

describe("a plan warning in the window's words", () => {
  it("rewords the forwarding without a copy for the card's box", () => {
    expect(
      windowWarning("Alice Example keeps no copy: the mail reaches alice@fabrikam.com only (--keep-copy keeps one)"),
    ).toBe(
      'Alice Example keeps no copy: the mail reaches alice@fabrikam.com only. To keep one in the mailbox too, choose "Change the picks" and tick "Keep a copy in the mailbox".',
    );
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
    expect(onCards.length).toBeGreaterThanOrEqual(5);
    for (const { template } of onCards) {
      expect(windowWarning(filled(template))).not.toMatch(flag);
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
    }
  });
});
