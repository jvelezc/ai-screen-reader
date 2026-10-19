# Observed tree and tab context

Date: October 19, 2026. Profile: hsr-desktop-reader-v9.

## Decision

Add bounded tree and tab support using exposed Chromium roles, parent links, hierarchy levels, selection/expansion states, actual focus and active descendants. A tree level comes from AX and has independent source provenance. It is not a parent relationship inferred from flat node order. HeadingLevel is now populated only for headings; tree levels use HierarchyLevelObservation.

Context includes tree, treeitem, tablist and tabpanel containers. Exposed tree peers require a known local tree/group/item owner, positive matching exposed levels and a complete parent chain. A flat level greater than one under the tree root cannot establish a nested sibling set. Tabs use their nearest exposed tablist. Known document-identity conflicts suppress peer positions and composite-owner activation. Existing RootWebArea boundaries prevent borrowing an outer document's tree. Missing node-level document IDs retain the existing snapshot/observed-root scoping; they do not supply new document evidence.

ExposedPeers describes only presently exposed peers. Declared LogicalSet positions remain separate, including unknown size. Hidden or virtualized items are never added to observed counts. AncestryComplete describes whether the supplied parent links can be followed without a missing node or cycle; it is not website completeness or proof of valid author markup.

## Navigation and input

NextTree/PreviousTree, NextTreeItem/PreviousTreeItem and NextTabItem/PreviousTabItem are browse-only virtual navigation with no keyboard input or wrapping. Tab and ReverseTab remain actual keyboard commands. Landing on a tree, treeitem, tab or tablist selects focus mode. Raw arrow/Home/End input uses the unchanged allowlist and the website's own handlers.

Activate may send Enter to a directly focused treeitem or tab, or to a focused tree/tablist whose effective item has observed same-document ownership. It does not move focus, repair an active descendant, expand a branch while browsing or equate focus with selection. Disabled observations retain the existing scoped disabled-control outcome. Missing, cyclic, foreign or cross-document ownership remains unsupported rather than an accessibility barrier.

The trusted fixture uses one single-select tree with active-descendant and roving-focus modes, plus a tablist with roving DOM focus and explicit Enter selection. The website itself switches tree focus mode through its exposed button; the reader does not inject focus. Its keyboard behavior follows selected interactions from the [W3C tree-view pattern](https://www.w3.org/WAI/ARIA/apg/patterns/treeview/) and [tabs pattern](https://www.w3.org/WAI/ARIA/apg/patterns/tabs/). These references guide the fixture; they do not establish arbitrary-site correctness or equivalence to NVDA.

## Limits

Generalized tables/treegrids, multiselection, typeahead, asynchronous/virtualized tree loading, all tab orientation variants and arbitrary composite patterns remain outside this increment. Snapshot changes and hierarchy-bearing events are sampled observations. No editable-value capture, credential entry, new browser key, arbitrary target, recording or host service is introduced. Earlier profile worksheets remain historical; no fresh v9 assistive-technology comparison is claimed.
