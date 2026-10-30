// @vitest-environment happy-dom
import { describe, expect, it } from "vitest";
import { render } from "../test/dom";
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
