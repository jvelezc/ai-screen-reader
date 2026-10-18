# Exposed text navigation

Date: October 18, 2026. Status: accepted. Profile: hsr-desktop-reader-v6.

## Observations and ownership

Domain adds ExposedText and ReaderTextRange, plus typed text granularity/reset reasons. AccessibleNode.Text is an optional exact text observation, separate from its accessible Name. The Chromium adapter supplies it only for exposed StaticText/LineBreak AX names, retaining their original whitespace. InlineTextBox duplicates, control-name inference, AX values and editable descendants are excluded. No DOM text or new browser operation is introduced.

ScreenReader owns ReaderTextNavigator, a browser-independent virtual text selection. It returns UTF-16 start/length offsets, node/document identity, selected text, granularity and source. Text offsets always refer to the exact observed text run, not the trimmed semantic name. Other providers must explicitly provide their exposed text rather than relying on a name fallback.

## Commands and boundaries

ReadCurrentText reads the existing text selection, or the first known descendant text run of the current semantic node. A node with no observed text/descendant produces NoMatch. Next/PreviousCharacter, Next/PreviousWord and Next/PreviousLine traverse runs in exposed AX order, skipping runs with no unit of the requested granularity. An unset directional cursor starts at the corresponding document boundary. Known descendants use ParentId, guarded against missing parents/cycles; other directional movement uses observed order without claiming containment.

Character units are .NET StringInfo grapheme clusters, including combining sequences and joined emoji. Offsets remain UTF-16. Word units combine Unicode letters, numbers, marks and connectors; internal ASCII/curly apostrophes join word parts. Other non-whitespace graphemes form separate punctuation/symbol units. This is deterministic segmentation, not a language-sensitive word-break service.

Line units split each AX text run at CR, LF or CRLF. Delimiters are excluded from the returned line; empty interior/trailing lines are legitimate zero-length ranges with CursorMoved. An empty run supplies no units. These commands do not describe rendered soft wrapping or combine separately exposed inline runs into a visual line/word. Reconstructing full layout/text flow remains separate work.

Once a unit is selected, changing granularity searches strictly before/after its start offset. A freshly read whole TextRun selects its first/last unit on the next directional text command. NoMatch preserves the last selection and never wraps. ReadCurrent remains the semantic node announcement; ReadCurrentText supplies the selected text.

## Invalidations and input

If the selected document, source text/provenance or node availability changes, the next text command returns Unknown, no TextRange, and DocumentChanged/TextChanged/NodeRemoved. Further text commands remain Unknown until an explicit successful semantic movement, mode change or reader-dispatched input clears the selection. For example, DocumentStart then NextWord establishes a new anchor. A removed/changed selection is not silently relocated or continued with stale offsets. Browser errors clear the selection and retain their existing error outcome.

Text commands require browse mode and dispatch no browser input. Focus mode returns Unsupported. They share the existing command budget, cancellation, capture and controller serialization. Semantic goal expectations are rejected as Unsupported before text navigation; text-range selection is not evidence that a website goal was reached. Typed text ranges are not persisted as extra event payloads; existing announcement events still record the observed text, with its existing sensitivity limitations.

## Composition and validation

CLI --text exercises the trusted Text fixture and emits structured text ranges. It is mutually exclusive with --widgets and --observe-live; arbitrary targets, credential entry and recordings remain unavailable. No project/package/dependency was added. v6 invalidates historical reference worksheets. Deterministic tests and real Chromium acceptance cover the contract; no NVDA equivalence or credential-capture privacy claim follows.
