# Previous landmark leaves enclosing context

Date: October 18, 2026. Status: accepted. Profile: hsr-desktop-reader-v5.

The v4 NVDA comparison recorded DIFF-004: from the end of content inside main, PreviousLandmark returned main, whereas NVDA Shift+D reached the preceding Primary navigation landmark. Returning a container already occupied by the virtual cursor is an unhelpful backward navigation stop for an AI reader.

In v5, PreviousLandmark searches earlier exposed landmark nodes while excluding the current cursor's known enclosing ancestors. Earlier siblings, including nested landmarks in completed sibling subtrees, remain destinations. From a landmark node, search starts before that node. An unset cursor searches backward from the end. No match preserves the cursor, and navigation never wraps or sends browser input.

Only ParentId observations establish enclosure. The existing bounded, cycle-safe ReaderContextBuilder supplies known ancestors, including partial ancestry when a parent is missing or cyclic. No DOM discovery, flat-order containment inference, or invented ancestor is added. AncestryComplete continues to describe uncertainty in results. Forward landmark traversal and ordinary MovePrevious retain their existing exposed-node order; focus-mode rules are unchanged.

This is a behavior change, so the profile advances to v5. Existing v4 worksheets remain historical and are rejected by the current reference assessor. No new Domain contract, dependency, package or public browser operation is needed. A closed embedded Landmarks fixture exercises nested contexts separately from the unchanged Foundation/Widgets reference fixtures.

Acceptance: deterministic boundary/uncertainty/no-input tests, Foundation and nested real Chromium paths, Windows/Linux Release checks, and a fresh optional targeted NVDA reading comparison. Neither this decision nor that comparison establishes general NVDA equivalence, complete text reading, child-frame support or speech arbitration.
