import { lazy, StrictMode, Suspense } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import "./index.css";

/**
 * The task card design sketch (task 018): made-up data, no bridge request, in the dev server only. The build
 * replaces `import.meta.env.DEV` with false, so neither the route nor the sketch's chunk reaches `dist/` and the
 * window never shows it.
 */
const TaskCardSketch = import.meta.env.DEV ? lazy(() => import("./sketch/task-card-sketch")) : null;
const SKETCH_ROUTE = "#/sketch/task-cards";

const root = document.getElementById("root");
if (!root) {
  throw new Error("no #root element");
}
createRoot(root).render(
  <StrictMode>
    {TaskCardSketch && window.location.hash === SKETCH_ROUTE ? (
      <Suspense fallback={null}>
        <TaskCardSketch />
      </Suspense>
    ) : (
      <App />
    )}
  </StrictMode>,
);
