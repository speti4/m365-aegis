// @vitest-environment happy-dom
import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import "./test/dom";

/** The entry (task 070): it mounts the page on #root, or the design sketch on its route in the dev server only. */
async function start(hash = ""): Promise<HTMLElement> {
  const root = document.createElement("div");
  root.id = "root";
  document.body.append(root);
  window.location.hash = hash;
  await act(async () => {
    await import("./main");
  });
  return root;
}

beforeEach(() => {
  vi.resetModules();
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllEnvs();
  document.body.innerHTML = "";
  window.location.hash = "";
});

describe("the entry", () => {
  it("mounts the window's page", async () => {
    const root = await start();

    expect(root.querySelector("h1")?.textContent).toBe("m365-aegis");
  });

  it("mounts the task card sketch on its route in the dev server", async () => {
    const root = await start("#/sketch/task-cards");
    await act(async () => {
      await vi.dynamicImportSettled();
    });

    expect(root.querySelector("h1")?.textContent).toBe("Task cards");
  });

  it("never mounts the sketch in the build", async () => {
    vi.stubEnv("DEV", false);

    const root = await start("#/sketch/task-cards");

    expect(root.querySelector("h1")?.textContent).toBe("m365-aegis");
  });

  it("fails without the #root element", async () => {
    await expect(import("./main")).rejects.toThrow("no #root element");
  });
});
