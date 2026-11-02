import type { ReactNode } from "react";
import { focusTarget } from "@/lib/focus";
import { cn } from "@/lib/utils";

/**
 * The technical side of an answer, collapsed under one "Details" line (task 126): the layer that read a row, how long
 * the answer took, the object ids, an error's code. A support colleague reads the answer without them; whoever needs
 * them opens the line (a click, or Enter or Space on it).
 */
export function DetailsLine({ className, children }: { className?: string; children: ReactNode }) {
  return (
    <details className={cn("text-xs text-muted-foreground", className)} data-testid="details">
      <summary className={cn("w-fit cursor-pointer rounded-sm select-none hover:text-foreground", focusTarget)}>
        Details
      </summary>
      <div className="mt-1 space-y-1 pl-3.5">{children}</div>
    </details>
  );
}
