# ADR 0004: structured context and observed live changes

Status: accepted for the local fixture increment, October 18, 2026.

The live NVDA comparison exposed useful group/dialog context, item positions and independent status utterances. An AI client needs these facts without conflating them with the current control, input success or verified goals.

## Decision

Keep the existing project dependency direction. Domain adds observation records; Chromium normalizes exposed AX ancestry and live properties; ScreenReader interprets that data. No new dependency or project is required. Profile hsr-desktop-context-v3 identifies the changed output contract.

ReaderStep.Context identifies the announced node and its outer-to-inner semantic containers. ParentId is the nearest exposed AX ancestor, with ignored wrappers skipped. Missing parents and cycles leave AncestryComplete false. Radios and options receive an ExposedPeers position/count within their nearest exposed group or listbox. These counts describe only currently exposed peers, not aria-posinset, aria-setsize or a virtualized logical set. Names remain source observations, not inferred labels.

LiveRegionObservation carries exposed politeness, atomic state, effective busy state and normalized accessible text. Busy state includes exposed ancestors and accommodates Chromium's numeric boolean representation. Text is built from the existing value-minimized AX subtree, skips editable roots and excludes nested live roots from their parent's text.

ScreenReader compares live text between captures in the same document. Initial content seeds a baseline; later nonempty additions or changes produce typed LiveRegionChanged timeline events. Unchanged/off/empty regions do not emit; busy changes wait until an observed non-busy capture. Removed roots are forgotten, so later reinsertion is a new observation. Document replacement resets the baseline. The event records observed text, not whether NVDA would speak it or a goal has succeeded.

Current-control Announcement remains separate. There is no background observer, speech queue, live-region timing/priority arbitration, partial non-atomic text diff or claim to capture intermediate changes between snapshots. The existing bounded timeline/truncation contract applies.

## Consequences

Agents can correlate an option with its listbox, a radio with its group, or a button with its dialog, and inspect status changes without moving focus or replacing the command announcement. Browse activation still requires actual browser focus. Missing native checkbox required state remains a documented AX limitation; no diagnostic DOM inference is introduced.

Existing v2 NVDA observations remain historical and incomplete. They do not validate the new profile, and current reference assessment rejects their profile mismatch. The original architecture plan under outputs/ is preserved.

The source property choices follow the [Chromium Accessibility protocol](https://github.com/ChromeDevTools/devtools-protocol/blob/master/pdl/domains/Accessibility.pdl), which defines AX parent/child, live, atomic and busy observations. Acceptance uses the actual pinned browser rather than assuming every protocol value has the expected JSON representation.
