# 0018: Execution traces support later feature path assessment

Status: trace representation implemented; feature search and reasonable-effort assessment are future work. October 20, 2026.

The user clarified the purpose: traversal is a trace of state nodes and action edges. Later, a feature such as opening a cart or completing a form will have a declared objective and may require a particular combination of reader commands and keystrokes. Those paths, their failed alternatives and their complexity provide evidence for deciding whether someone can reasonably perform the action.

## Keep the observed trace

A node is an observed browser/reader state, including document/frame identity, virtual cursor, actual focus, active descendant and reader mode. Its visible label comes from exposed semantics; an unnamed button remains unnamed. A feature hypothesis does not supply a missing accessible label.

An edge connects the last retained source observation to the actual result observation of a reader command. It records the exact ordered browser-key attempts and completed API calls with event references. Reader commands and browser keys are distinct: NextButton can move the virtual cursor with zero browser keys while still representing a reader interaction. Zero browser keys does not mean zero user effort.

Keep repeated state observations in the immutable trace. Later grouping can show loops, revisits and alternative routes without rewriting the evidence. A changed DOM instance is not automatically the same state. A large number of captured nodes or repeated observations does not establish a difficult task.

If an attempt has no reliable result observation, retain its unresolved action and input uncertainty separately; never invent a destination node. A dashed arrow ends without a result node in the diagram. It is not a successful transition or an accessibility barrier. Selecting any node or arrow opens passive evidence, not browser input or replay.

Graph projection `execution-trace-v2` adds exposed state labels, reader/focus identity, exact key event references, input-evidence completeness and unresolved attempts. The authoritative receipt remains `hsr-traversal-v1`; it can be reprojected. The SVG and equivalent Markdown table are views of that same evidence. Green marks an explicitly verified objective, amber an observed disabled control, and neutral nodes are ordinary observed states. Missing or truncated input evidence is explicit.

## Later connect features to objectives and paths

A future feature record will link its observed or diagnostic discovery provenance, a declared initial-state predicate and an explicit destination predicate to one or more attempted traces. A feature may have several candidate paths. Keep diagnostic hypotheses, reader-identifiable features and verified paths distinguishable.

The future bounded planner will search permitted combinations of reader commands and keys under a named profile. It must retain its frontier, exclusions, budgets and stop reason, including engine limitations and policy restrictions. An authored successful route demonstrates one observed path, not the shortest or only path. Independent fresh-browser alternatives currently demonstrate separate experiments; they are not merged into a proven navigable graph.

"No combination found within our bounds" remains Unknown with search coverage. It does not prove the objective impossible. An observed obstacle can establish a barrier for its tested route while other paths remain untested. Engine errors, policy denials, missing capture coverage and cancelled attempts must remain separate from barriers.

## Later assess reasonable effort

Assess the objective-specific path, not the complexity of the entire website graph. A complex website may still offer a short, discoverable route to the objective. A small graph may contain an unlabeled decision, timing requirement or focus trap that makes a simple-looking route difficult.

Useful future evidence includes:

- Reader-command count and actual browser keystrokes, reported separately.
- Choices the reader can identify from exposed semantics, rather than diagnostic knowledge.
- Repeated actions, backtracking, state revisits, dead ends and focus recovery.
- Browse/focus changes and context the person must remember or reconstruct.
- Interaction time separated from passive loading/verification waits and timing-sensitive demands.
- Reproducibility, human assistance and comparison with other verified paths from equivalent initial conditions.

Define and validate any burden rubric before classifying a path as requiring unreasonable effort. Path search completeness and user-effort assessment are separate questions. Report measured signals, supporting traces and unknowns; use user evaluation to establish defensible thresholds. There is no automatic complexity score, global "unreachable" conclusion, or challenge classification in this increment.

The future assessment can flag an objective as a candidate challenge when its tested routes are repeatedly blocked, require substantial observed burden, or depend on information the reader cannot identify. Each flag should link to the relevant path and alternatives, explain the difficulty, and retain the scope of the search. Distinguish reaching an objective with substantial effort from failing to find a route, and both from an evaluation prevented by engine limitations.

This concept remains a later capability. The immediate foundation gate remains capture exclusion before recording or credential entry.
