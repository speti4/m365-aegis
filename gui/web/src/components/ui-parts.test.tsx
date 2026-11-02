// @vitest-environment happy-dom
import { describe, expect, it } from "vitest";
import { render } from "../test/dom";
import { SubmitButton } from "./card-parts";
import { addToList, currentItem } from "./suggest-field";
import { Table, TableBody, TableCaption, TableCell, TableFooter, TableRow } from "./ui/table";

/** The parts of the generated shadcn/ui components no card uses yet (task 070): they render their slot. */
describe("the generated table", () => {
  it("renders a caption and a footer", async () => {
    const { container } = await render(
      <Table>
        <TableCaption>Members</TableCaption>
        <TableBody>
          <TableRow>
            <TableCell>alice@contoso.com</TableCell>
          </TableRow>
        </TableBody>
        <TableFooter>
          <TableRow>
            <TableCell>1 member</TableCell>
          </TableRow>
        </TableFooter>
      </Table>,
    );

    expect(container.querySelector('caption[data-slot="table-caption"]')?.textContent).toBe("Members");
    expect(container.querySelector('tfoot[data-slot="table-footer"]')?.textContent).toBe("1 member");
  });
});

describe("the parts every card shares (task 126)", () => {
  it("keeps the submit button as wide as its working label, which a hidden copy holds", async () => {
    const { container, rerender } = await render(
      <SubmitButton busy={false} missing={null} hintId="hint" idle="Look up" working="Looking up…" />,
    );
    const sizer = container.querySelector('[data-testid="button-sizer"]') as HTMLElement;
    expect(sizer.getAttribute("aria-hidden")).toBe("true");
    expect(sizer.className).toContain("invisible");
    expect(sizer.textContent).toBe("Looking up…");
    expect(sizer.parentElement).toBe(container.querySelector("button")?.parentElement);
    expect(container.querySelector("button")?.textContent).toBe("Look up");

    await rerender(<SubmitButton busy missing={null} hintId="hint" idle="Look up" working="Looking up…" />);
    expect(container.querySelector("button")?.textContent).toBe("Looking up…");
  });

  it("adds a pick to a comma-separated list once, in place of the item being typed", () => {
    expect(currentItem("alice@contoso.com; bo")).toBe(" bo");
    expect(currentItem("bob")).toBe("bob");
    expect(addToList("bo", "bob@contoso.com")).toBe("bob@contoso.com, ");
    expect(addToList("alice@contoso.com;carol@contoso.com, bo", "bob@contoso.com")).toBe(
      "alice@contoso.com, carol@contoso.com, bob@contoso.com, ",
    );
    expect(addToList("Bob@contoso.com, bo", "bob@contoso.com")).toBe("Bob@contoso.com, ");
  });
});
