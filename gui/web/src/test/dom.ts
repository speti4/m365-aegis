import { act, type ReactNode } from "react";
import { createRoot } from "react-dom/client";
import { vi } from "vitest";

/**
 * The DOM tests' helpers (task 070): a component rendered into happy-dom with React's own `act`, and the few user
 * steps the cards take (a click, typing, a form's submit), so the tests need no testing library. A test file opts
 * into the DOM with `// @vitest-environment happy-dom` on its first line.
 */

// React warns when an update happens outside `act` unless it knows it runs in a test.
(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

export interface Rendered {
  container: HTMLElement;
  /** Renders another element into the same root (new props). */
  rerender(node: ReactNode): Promise<void>;
  unmount(): Promise<void>;
}

/** Renders the element into a fresh element of the document. */
export async function render(node: ReactNode): Promise<Rendered> {
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  await act(async () => root.render(node));
  return {
    container,
    rerender: async (next) => {
      await act(async () => root.render(next));
    },
    unmount: async () => {
      await act(async () => root.unmount());
      container.remove();
    },
  };
}

/** The button whose text is `name` (trimmed, exact); throws when there is none. */
export function button(container: ParentNode, name: string): HTMLButtonElement {
  const found = [...container.querySelectorAll("button")].find((candidate) => candidate.textContent?.trim() === name);
  if (!found) {
    throw new Error(`no button '${name}' in ${container.textContent}`);
  }
  return found;
}

/** Whether a button whose text is `name` is there. */
export function hasButton(container: ParentNode, name: string): boolean {
  return [...container.querySelectorAll("button")].some((candidate) => candidate.textContent?.trim() === name);
}

/** Clicks the element and lets React settle. */
export async function click(element: Element): Promise<void> {
  await act(async () => {
    element.dispatchEvent(new MouseEvent("click", { bubbles: true }));
  });
}

/** Types `value` into an input as a user would: React's own value setter, then the input event. */
export async function type(input: HTMLInputElement, value: string): Promise<void> {
  await act(async () => {
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
    setter?.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

/** Submits the form and lets React settle. */
export async function submit(form: HTMLFormElement): Promise<void> {
  await act(async () => {
    form.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
  });
}

/** How many turns of the real clock `settle` lets pass after its wait: a reply can start a request of its own. */
const FOLLOW_UP_TURNS = 4;

/**
 * Lets the transport answer and React settle: moves the fake clock by `ms`, or waits `ms` of the real one and then a
 * few more turns of it, so a request a reply starts (an apply that makes the access card read the mailbox again, task
 * 091) is answered too; with one turn that second answer came before or after the wait by chance, and a test failed
 * now and then.
 */
export async function settle(ms = 0): Promise<void> {
  await act(async () => {
    if (vi.isFakeTimers()) {
      await vi.advanceTimersByTimeAsync(ms);
    } else {
      await new Promise((resolve) => setTimeout(resolve, ms));
      for (let turn = 0; turn < FOLLOW_UP_TURNS; turn += 1) {
        await new Promise((resolve) => setTimeout(resolve, 0));
      }
    }
  });
}
