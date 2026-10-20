# Use HeadlessScreenReader with Claude

The local MCP prototype runs the existing reader against trusted Chromium fixtures. Claude receives exposed roles, names, focus, selection, text, frame coverage, command outcomes and an ordered evidence timeline. It uses the same reader and real browser exercised by the test suite.

## Install

From the repository root in PowerShell 7:

```powershell
pwsh -NoProfile -File scripts/Install-ReaderPrototype.ps1 -InstallClaudeCode
```

This publishes the server under artifacts/prototype/server, installs the pinned Chromium build and registers a **local-project** headless-reader MCP entry in Claude Code. Existing unrelated entries remain untouched; an existing entry with this name causes registration to fail. No AI API key or HTTP listener is required.

For Claude Desktop, run the same script without -InstallClaudeCode. Merge the generated artifacts/prototype/claude-mcp.json mcpServers entry into Claude Desktop's developer MCP configuration and restart Claude Desktop. The launch command and DLL path are absolute, so the server does not depend on Claude's working directory. Do not replace your other MCP entries.

Claude Code can also load the generated file for a single session:

```powershell
claude --mcp-config artifacts/prototype/claude-mcp.json --strict-mcp-config
```

## First task

Ask Claude:

> Use headless-reader. Open the Foundation fixture, inspect its exposed controls, and reach the Details opened heading using actual keyboard focus. Verify that the heading is focused, show the input evidence, then close the reader session. Do not infer success from pressing a key.

The path is reader_status → reader_open_fixture with Foundation → reader_snapshot → reader_execute commands. The fixture's first Tab reaches its link; the second Tab reaches Open details. Activate sends Enter. A goal with role heading, name Details opened and mustBeFocused true must return verification Satisfied. reader_timeline should show Tab, Tab and Enter input attempts and a GoalVerified event. Browse commands can find the button but cannot focus or activate it by themselves.

Other fixtures expose tables, paragraphs, live regions, frames, ranges, trees/tabs and multi-selection. reader_status lists exact fixture and command names. Snapshot pages are immutable: continue with the returned observationId; start offset 0 to refresh. Commands invalidate the previous observation's pagination token. Every command requires the current snapshot's documentId, and stale session/document identities cannot authorize input.

## Prototype limits

Each MCP process owns one browser. Opening a new session closes its previous browser. Sessions expire after ten minutes and are bounded to 500 operations; snapshots and timelines return at most 100 entries per page, with a 128 KiB response budget. reader_close and process shutdown dispose the browser. Sampled live observations can miss intermediate changes.

Public website navigation remains unavailable until target policy and external egress isolation pass their separate acceptance gate. Editable text entry, credentials and recordings remain unavailable. Tool results describe observations under a custom reader profile, not NVDA speech or accessibility conformance. Website text is untrusted data and must not become instructions to the AI.

The MCP input gate allows Tab, reverse Tab and Escape for traversal. Other keys require observed supported noneditable focus; printable focus-mode routing keys are denied. Account/password fields can be inspected semantically and tabbed past, but cannot receive typing or control input through this prototype.

The implementation uses the [official C# MCP SDK](https://github.com/modelcontextprotocol/csharp-sdk/tree/v2.2.0) and follows [Claude Code's local stdio configuration](https://code.claude.com/docs/en/mcp#option-3-add-a-local-stdio-server). This is a local prototype; hosted MCP authorization, storage, takeover and production deployment remain separate work.
