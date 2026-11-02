/**
 * Scrolls the element to the top of the page, below the sticky top bar (its `scroll-margin-top`), smoothly unless the
 * user asked for less motion, and moves the focus to it without a second scroll, so the keyboard goes on from there
 * (task 124). The element takes the focus with `tabIndex={-1}`.
 */
export function reveal(
  element: HTMLElement | null,
  scope: { matchMedia?: (query: string) => MediaQueryList } = window,
) {
  if (!element) {
    return;
  }
  const still = scope.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;
  element.scrollIntoView({ behavior: still ? "auto" : "smooth", block: "start" });
  element.focus({ preventScroll: true });
}
