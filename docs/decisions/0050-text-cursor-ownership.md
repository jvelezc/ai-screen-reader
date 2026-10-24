# Text cursor ownership across fresh observations

A retained word, character or line range belongs to a particular root document, node, frame and node document. Reusing a node ID and identical text does not establish continuity across owners.

Before reading or advancing an existing range, the reader checks the current node's frame and document against the retained range, in addition to the existing root-document, frame-catalog generation, node-presence and exact-text checks. This prevents an unchanged frame catalog from hiding a changed node owner. For legacy observations without node document metadata, the existing root-document fallback remains supported.

An ownership change returns `Unknown`, `textCursorReset:DocumentChanged`, and no `textRange`. Further text navigation stays unknown until an explicit reader re-anchor, such as `DocumentStart`, followed by `NextWord`. It does not silently restart at the new owner's first word. Stable ownership preserves the selected offset, even if an unrelated child document changes. Re-anchoring reports the new range's actual document and frame.

The repair lives in the platform-independent reader and therefore applies to CLI, local MCP and hosted HTTP text navigation. Hosted trace entries retain the fresh snapshot and reset outcome; old text offsets cannot be presented as current evidence. A node ID in two states alone does not prove text-position continuity. The root-document request fence remains a separate check: a changed child owner can invalidate text while the root document still matches.

This is a compatible behavior correction to HTTP contract1.3.0 and reader profilev13. It changes no request/response shape, command allowlist, input authority, native transport, database, storage or ingress. Current hosted reading still admits only the Foundation fixture. Real application JWT/RLS, authenticated Edge-to-core and human/AT acceptance remain separate from synthetic tests and private delivery.

See [validation](../validation/text-cursor-ownership-v1.md).
