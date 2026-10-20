using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed partial class ChromiumFixtureBrowser
{
    private static bool IsTableRole(string role) => role is "table" or "grid" or "treegrid" or "row"
        or "cell" or "gridcell" or "rowheader" or "columnheader";

    private async Task<TableObservation> ReadTableAsync(int backendId)
    {
        var resolved = await _cdp.SendAsync("DOM.resolveNode", new Dictionary<string, object>
        { ["backendNodeId"] = backendId, ["objectGroup"] = "hsr-table-semantics" }).ConfigureAwait(false);
        if (resolved is null || !resolved.Value.GetProperty("object").TryGetProperty("objectId", out var objectId))
            throw new BrowserOperationException("TABLE_NODE_UNAVAILABLE");
        var id = objectId.GetString()!;
        try
        {
            // Native geometry and explicit scope only. No text, field values, diagnostic labels or page mutation.
            var response = await _cdp.SendAsync("Runtime.callFunctionOn", new Dictionary<string, object>
            {
                ["objectId"] = id,
                ["returnByValue"] = true,
                ["functionDeclaration"] = """
                    function() {
                      const cell = this.localName === 'td' || this.localName === 'th' ? this : null;
                      const row = cell ? cell.parentElement : this.localName === 'tr' ? this : null;
                      const table = this.localName === 'table' ? this : this.closest('table');
                      if (!table || !['table', 'tr', 'td', 'th'].includes(this.localName))
                        return {rows:0, columns:0, row:null, column:null, scope:null, reason:'TABLE_NATIVE_STRUCTURE_UNAVAILABLE'};
                      const rows = Array.from(table.rows);
                      const cells = rows.flatMap(r => Array.from(r.cells));
                      const columns = rows.length ? rows[0].cells.length : 0;
                      const nodes = [table, ...rows, ...cells];
                      let reason = null;
                      if (rows.length > 256 || cells.length > 4096) reason = 'TABLE_CAPTURE_LIMIT';
                      else if (table.querySelector('table')) reason = 'TABLE_NESTING_UNSUPPORTED';
                      else if (cells.some(c => c.rowSpan !== 1 || c.colSpan !== 1)) reason = 'TABLE_SPAN_UNSUPPORTED';
                      else if (rows.some(r => r.cells.length !== columns)) reason = 'TABLE_IRREGULAR_LAYOUT_UNSUPPORTED';
                      else if (nodes.some(n => ['aria-rowcount','aria-colcount','aria-rowindex','aria-colindex'].some(a => n.hasAttribute(a)))) reason = 'TABLE_DECLARED_GEOMETRY_UNSUPPORTED';
                      else if (table.hasAttribute('role') && table.getAttribute('role') !== 'table'
                        || rows.some(r => r.hasAttribute('role') && r.getAttribute('role') !== 'row')
                        || cells.some(c => c.hasAttribute('role'))) reason = 'TABLE_ROLE_OVERRIDE_UNSUPPORTED';
                      else if (cells.some(c => c.headers.trim())) reason = 'TABLE_EXPLICIT_HEADERS_UNSUPPORTED';
                      else if (cells.some(c => c.isContentEditable || c.querySelector('input,textarea,select,[contenteditable]'))) reason = 'TABLE_EDITABLE_CONTENT_UNSUPPORTED';
                      return {rows:rows.length, columns, row:row ? row.rowIndex + 1 : null,
                        column:cell ? cell.cellIndex + 1 : null, scope:cell && cell.localName === 'th' ? cell.scope : null, reason};
                    }
                    """
            }).ConfigureAwait(false);
            if (response is null || response.Value.TryGetProperty("exceptionDetails", out _))
                throw new BrowserOperationException("TABLE_PROPERTIES_UNAVAILABLE");
            var value = response.Value.GetProperty("result").GetProperty("value");
            static int? Optional(JsonElement value, string key) => value.GetProperty(key).ValueKind == JsonValueKind.Number
                ? value.GetProperty(key).GetInt32() : null;
            var reason = value.GetProperty("reason").GetString();
            return new(value.GetProperty("rows").GetInt32(), value.GetProperty("columns").GetInt32(),
                Optional(value, "row"), Optional(value, "column"), value.GetProperty("scope").GetString(),
                reason is null, reason, SemanticSource.NativeHtmlProperty);
        }
        finally
        {
            await _cdp.SendAsync("Runtime.releaseObject", new Dictionary<string, object> { ["objectId"] = id }).ConfigureAwait(false);
        }
    }
}
