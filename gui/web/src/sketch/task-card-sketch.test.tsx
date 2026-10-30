// @vitest-environment happy-dom
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { button, click, hasButton, render, settle } from "../test/dom";
import { planFor } from "./sketch-data";
import TaskCardSketch from "./task-card-sketch";

// The made-up core, wrapped so a test can hand the sketch a plan the data never gives (a removal, no hint).
vi.mock("./sketch-data", async (importOriginal) => {
  const original = await importOriginal<typeof import("./sketch-data")>();
  return { ...original, planFor: vi.fn(original.planFor) };
});

/** How long the sketch's made-up core works. */
const WORK_MS = 900;

async function sketch() {
  const rendered = await render(<TaskCardSketch />);
  const { container } = rendered;
  const text = () => container.textContent ?? "";
  /** The button `name` inside the radio group or fieldset labelled `group`. */
  const choice = (group: string, name: string) => {
    const scope = [...container.querySelectorAll('[role="radiogroup"], fieldset')].find(
      (candidate) =>
        candidate.getAttribute("aria-label") === group || candidate.querySelector("legend")?.textContent === group,
    );
    if (!scope) {
      throw new Error(`no group '${group}'`);
    }
    return [...scope.querySelectorAll("button")].find((candidate) =>
      candidate.textContent?.startsWith(name),
    ) as HTMLButtonElement;
  };
  const open = async (title: string) => {
    const card = [...container.querySelectorAll('[data-slot="card"]')].find((candidate) =>
      candidate.textContent?.includes(title),
    ) as HTMLElement;
    await click(button(card, "Start"));
  };
  return { ...rendered, text, choice, open };
}

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
  vi.mocked(planFor).mockClear();
  document.body.innerHTML = "";
});

describe("the task card sketch", () => {
  it("lists the cards, and only signed in can one start", async () => {
    const { container, text, choice } = await sketch();
    expect(container.querySelectorAll('[data-slot="card"]')).toHaveLength(3);
    expect(text()).toContain("changes the tenant: plan, then confirm");
    expect(text()).toContain("reads only");

    await click(choice("Sign-in", "signed out"));
    expect(text()).toContain("The cards need a sign-in.");
    expect(container.querySelector('[role="status"]')).not.toBeNull();
    expect(container.querySelectorAll("button[disabled]")).toHaveLength(3);

    await click(choice("Sign-in", "signed in"));
    expect(text()).not.toContain("The cards need a sign-in.");
  });

  it("switches the theme of the page", async () => {
    const { container, choice } = await sketch();
    const page = container.firstElementChild as HTMLElement;

    await click(choice("Theme", "dark"));
    expect(page.classList.contains("dark")).toBe(true);
    await click(choice("Theme", "light"));
    expect(page.classList.contains("light")).toBe(true);
    await click(choice("Theme", "system"));
    expect(page.classList.contains("light")).toBe(false);
  });

  it("runs a write card: pick, plan, confirm, result, and again", async () => {
    const { container, text, choice, open } = await sketch();
    await open("Grant FullAccess on a shared mailbox");
    expect(container.querySelectorAll('ol[aria-label="Steps"] li')).toHaveLength(4);
    expect(button(container, "See the plan").disabled).toBe(true);

    await click(choice("Shared mailbox", "Finance"));
    await click(choice("Colleagues", "Alice Example"));
    await click(choice("Colleagues", "Carol Example"));
    await click(choice("Colleagues", "Carol Example"));
    expect(choice("Colleagues", "Carol Example").getAttribute("aria-checked")).toBe("false");
    await click(choice("Colleagues", "Bob Example"));
    await click(button(container, "See the plan"));
    expect(text()).toContain("The core is computing the plan…");

    await settle(WORK_MS);
    expect(text()).toContain("1 change(s) when you confirm, and none before:");
    expect(text()).toContain("Alice Example already holds FullAccess on Finance");
    expect(text()).toContain("Good to know");
    expect(text()).toContain("this plan's shape is made up");

    await click(button(container, "Continue to confirm"));
    const confirm = button(container, "Confirm and apply");
    expect(confirm.disabled).toBe(true);
    await click(button(container, "Back to the plan"));
    await click(button(container, "Continue to confirm"));
    await click(container.querySelector('input[type="checkbox"]') as HTMLInputElement);
    await click(button(container, "Confirm and apply"));
    expect(text()).toContain("Applying the plan: one change at a time…");

    await settle(WORK_MS);
    expect(text()).toContain("Every change was made.");
    expect(container.querySelector('[aria-label="done"]')).not.toBeNull();

    await click(button(container, "Run this card again"));
    expect(button(container, "See the plan").disabled).toBe(true);
    await click(button(container, "All cards"));
    expect(container.querySelectorAll('[data-slot="card"]')).toHaveLength(3);
  });

  it("shows a partial result with the failed change", async () => {
    const { container, text, choice, open } = await sketch();
    await click(choice("The core answers", "with a partial result"));
    await open("Add a group owner");
    await click(choice("Group", "Finance EU"));
    await click(choice("New owners", "Bob Example"));
    await click(choice("New owners", "Carol Example"));
    await click(button(container, "See the plan"));
    await settle(WORK_MS);
    await click(button(container, "Continue to confirm"));
    await click(container.querySelector('input[type="checkbox"]') as HTMLInputElement);
    await click(button(container, "Confirm and apply"));
    await settle(WORK_MS);

    expect(text()).toContain("1 of 2 change(s) were made; 1 failed and can be run again.");
    expect(container.querySelector('[aria-label="failed"]')).not.toBeNull();
    expect(text()).toContain("the retries ran out");
    await click(button(container, "Done"));
    expect(container.querySelectorAll('[data-slot="card"]')).toHaveLength(3);
  });

  it("shows the core's refusal and goes back to the picks", async () => {
    const { container, text, choice, open } = await sketch();
    await click(choice("The core answers", "with a refusal"));
    await open("Grant FullAccess on a shared mailbox");
    await click(choice("Shared mailbox", "Reception"));
    await click(choice("Colleagues", "Carol Example"));
    await click(button(container, "See the plan"));
    await settle(WORK_MS);

    expect(container.querySelector('[role="alert"]')?.textContent).toContain("Refused: nothing was changed");
    expect(text()).toContain("What to do: ask an administrator for the Recipient Management role");
    await click(button(container, "Change the picks"));
    expect(hasButton(container, "See the plan")).toBe(true);
  });

  it("shows a refusal without a hint, and a plan that removes", async () => {
    vi.mocked(planFor)
      .mockReturnValueOnce({ code: "forbidden", message: "no" })
      .mockReturnValueOnce({
        title: "Remove an owner",
        changes: [{ action: "remove", target: "Finance", detail: "Bob Example as an owner" }],
        unchanged: [],
        warnings: [],
        readOnly: false,
      });
    const { container, text, choice, open } = await sketch();
    await open("Add a group owner");
    await click(choice("Group", "Finance"));
    await click(choice("New owners", "Bob Example"));
    await click(button(container, "See the plan"));
    await settle(WORK_MS);
    expect(text()).not.toContain("What to do");

    await click(button(container, "Change the picks"));
    await click(button(container, "See the plan"));
    await settle(WORK_MS);
    expect(container.querySelector('[data-variant="destructive"]')?.textContent).toBe("remove");
    expect(text()).not.toContain("Left as it is");
  });

  it("plans nothing when everything is already as asked", async () => {
    const { container, text, choice, open } = await sketch();
    await open("Grant FullAccess on a shared mailbox");
    await click(choice("Shared mailbox", "Finance"));
    await click(choice("Colleagues", "Alice Example"));
    await click(button(container, "See the plan"));
    await settle(WORK_MS);

    expect(text()).toContain("Nothing to do");
    expect(hasButton(container, "Continue to confirm")).toBe(false);
    await click(button(container, "Change the picks"));
  });

  it("answers a check card without a confirmation", async () => {
    vi.mocked(planFor).mockImplementationOnce(() => ({
      title: "What nobody can use",
      changes: [],
      unchanged: [],
      warnings: [],
      readOnly: true,
    }));
    const { container, text, choice, open } = await sketch();
    await open("Check a user's access");
    expect(container.querySelectorAll('ol[aria-label="Steps"] li')).toHaveLength(2);
    await click(choice("Colleague", "Alice Example"));
    await click(button(container, "Check"));
    expect(text()).toContain("Reading the access…");
    await settle(WORK_MS);
    expect(container.querySelectorAll("ul.list-disc li")).toHaveLength(0);

    await click(button(container, "Check another colleague"));
    await click(choice("Colleague", "Alice Example"));
    await click(choice("Colleague", "Bob Example"));
    await click(button(container, "Check"));
    await settle(WORK_MS);
    expect(text()).toContain("FullAccess on Reception");
    expect(text()).toContain("Direct grants only; nothing was changed.");
    expect(hasButton(container, "Continue to confirm")).toBe(false);
  });

  it("closes an open card when the window signs out", async () => {
    const { container, text, choice, open } = await sketch();
    await open("Check a user's access");

    await click(choice("Sign-in", "signed out"));

    expect(text()).toContain("The cards need a sign-in.");
    expect(hasButton(container, "All cards")).toBe(false);
  });
});
