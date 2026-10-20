# 0011 Bounded native table navigation

Status: accepted. Date: October 19, 2026. Profile: hsr-desktop-reader-v10.

The reader needs to associate values with their table row and column without flattening text or repairing missing semantics. Domain defines source-tagged native table geometry and reader header context. ScreenReader resolves ownership and commands from Domain observations. Browser.Playwright only produces observations; it does not reference the reader.

Only exposed AX tables, rows and cells receive fixed read-only native HTML supplements: table.rows dimensions, rowIndex, cellIndex and explicit header scope. Dimensions and positions are one-based native geometry and can include hidden rows/cells. They are not counts of exposed content. No editable values, diagnostic attributes or injected input/focus are used. Observations remain sampled, not an atomic browser transaction.

Simple rectangular native tables are supported. Header links require named exposed rowheader/columnheader nodes, explicit native row/col scope, compatible dimensions and complete same-document/frame ancestry under the same nearest table. Missing or unnamed headers remain unknown. This is intentionally a bounded custom rule rather than the full HTML header-association algorithm.

Next/PreviousTable traverses exposed tables without wrapping; PreviousTable skips the current enclosing table. Next/PreviousCell walks exposed native positions within the current table, or enters the first/last exposed cell when on the table. On a row, it enters that row. Next/PreviousRow retains the column and requires the adjacent native row to expose that cell. Next/PreviousColumn similarly retains the row. Boundaries and hidden/missing coordinates return NoMatch and retain the cursor. All commands require browse mode and dispatch no browser keys.

Spans, ragged rows, an outer table containing another table, declared ARIA geometry, explicit headers links, editable table content and role overrides return explicit unsupported results. The inner simple table can still be read independently. Native capture limits are 256 rows and 4096 cells; these limits do not authorize arbitrary-site capture. Broken/cyclic/foreign ancestry, stale nodes and conflicting coordinates cannot invent successful movement or barriers. New documents and missing nodes invalidate the cursor. Table observations are retained on ordered events; steps include resolved header context.

Real Chromium acceptance covers ordinary values and headers, missing and unnamed headers, hidden content, multiple/nested tables, unsupported layout declarations, external row removal and trusted child-document isolation. Local/CI success remains separate from assistive-technology compatibility, hosted isolation and deployment.
