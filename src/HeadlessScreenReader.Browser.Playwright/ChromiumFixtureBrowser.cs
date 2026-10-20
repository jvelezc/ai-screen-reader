using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

public enum BundledFixture { Foundation, Widgets, LiveRegions, Semantics, Landmarks, Text, Frames, Ranges, Composites, Tables }

/// <summary>A disposable, synthetic fixture harness. This is not an arbitrary-site scanning sandbox.</summary>
public sealed partial class ChromiumFixtureBrowser : IScreenReaderBrowser, IAsyncDisposable
{
    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;
    private readonly IBrowserContext _context;
    private readonly IPage _page;
    private readonly ICDPSession _cdp;
    private readonly FrameCaptureOptions _frameOptions;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;

    public FixtureBrowserEnvironment Environment { get; }

    private ChromiumFixtureBrowser(
        IPlaywright playwright, IBrowser browser, IBrowserContext context, IPage page, ICDPSession cdp,
        BundledFixture fixture, bool headed, FrameCaptureOptions frameOptions)
    {
        _playwright = playwright;
        _browser = browser;
        _context = context;
        _page = page;
        _cdp = cdp;
        _frameOptions = frameOptions;
        Environment = new(browser.Version,
            typeof(Microsoft.Playwright.Playwright).Assembly.GetName().Version!.ToString(),
            headed, BundledFixtureCatalog.Describe(fixture), frameOptions);
        _browser.Disconnected += (_, _) => _closed.TrySetResult();
        _page.Close += (_, _) => _closed.TrySetResult();
    }

    public static async Task<ChromiumFixtureBrowser> LaunchAsync(
        bool headed = false, CancellationToken cancellationToken = default,
        BundledFixture fixture = BundledFixture.Foundation, FrameCaptureOptions? frameOptions = null)
    {
        if (!Enum.IsDefined(fixture)) throw new ArgumentOutOfRangeException(nameof(fixture));
        frameOptions ??= new();
        if (frameOptions.MaxFrames is < 1 or > 32 || frameOptions.MaxDepth is < 0 or > 8)
            throw new ArgumentOutOfRangeException(nameof(frameOptions), "Frame bounds are 1..32 documents and 0..8 child levels.");
        cancellationToken.ThrowIfCancellationRequested();
        var playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
        IBrowser? browser = null;
        try
        {
            browser = await playwright.Chromium.LaunchAsync(new()
            {
                Headless = !headed,
                ChromiumSandbox = true,
                Timeout = 15_000
            }).ConfigureAwait(false);
            var context = await browser.NewContextAsync(new()
            {
                AcceptDownloads = false,
                ServiceWorkers = ServiceWorkerPolicy.Block,
                ViewportSize = new() { Width = 1280, Height = 720 }
            }).ConfigureAwait(false);
            await context.RouteAsync("**/*", route => route.AbortAsync()).ConfigureAwait(false);
            var page = await context.NewPageAsync().ConfigureAwait(false);
            page.SetDefaultTimeout(5_000);
            await page.SetContentAsync(BundledFixtureCatalog.ReadHtml(fixture))
                .ConfigureAwait(false);
            var cdp = await context.NewCDPSessionAsync(page).ConfigureAwait(false);
            await cdp.SendAsync("Accessibility.enable").ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new(playwright, browser, context, page, cdp, fixture, headed, frameOptions);
        }
        catch
        {
            if (browser is not null)
                await browser.CloseAsync().ConfigureAwait(false);
            playwright.Dispose();
            throw;
        }
    }

    public Task WaitForCloseAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        _closed.Task.WaitAsync(timeout, cancellationToken);

    public async Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await CaptureCoreAsync().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new BrowserOperationException("CHROMIUM_CAPTURE_TIMEOUT", exception);
        }
    }

    private async Task<FrameDocument> CaptureDocumentAsync(FrameDescriptor frame, string? ownerNodeId)
    {
        try
        {
            var response = await _cdp.SendAsync("Accessibility.getFullAXTree", new Dictionary<string, object>
            { ["frameId"] = frame.Id }).ConfigureAwait(false)
                ?? throw new BrowserOperationException("AX_RESPONSE_MISSING");
            // Chromium can repeat identical synthetic InlineTextBox nodes for list markers.
            // Coalesce exact copies; conflicting identities invalidate this observation.
            var groups = response.GetProperty("nodes").EnumerateArray()
                .GroupBy(node => node.GetProperty("nodeId").GetString()).ToArray();
            if (groups.Any(group => group.Skip(1).Any(node => node.GetRawText() != group.First().GetRawText())))
                throw new BrowserOperationException("AX_NODE_ID_CONFLICT");
            var raw = groups.Select(group => group.First()).ToArray();
            var byId = raw.ToDictionary(node => node.GetProperty("nodeId").GetString()!);
            var root = raw.FirstOrDefault(node => !node.TryGetProperty("parentId", out _));
            if (root.ValueKind == JsonValueKind.Undefined)
                throw new BrowserOperationException("AX_ROOT_MISSING");
            if (!root.TryGetProperty("frameId", out var rootFrame) || rootFrame.GetString() != frame.Id)
                throw new BrowserOperationException("FRAME_ROOT_MISMATCH");
            var documentId = $"{_sessionId}:{frame.Id}:{frame.LoaderId}:{root.GetProperty("nodeId").GetString()}";
            string Scoped(string id) => $"{documentId}:{id}";
            var focusedBackendId = await GetDocumentFocusAsync(root.GetProperty("backendDOMNodeId").GetInt32()).ConfigureAwait(false);
            var byBackendId = raw.Where(node => node.TryGetProperty("backendDOMNodeId", out _))
                .GroupBy(node => node.GetProperty("backendDOMNodeId").GetInt32())
                .ToDictionary(group => group.Key, group => Scoped(group.First().GetProperty("nodeId").GetString()!));

            var ordered = ImmutableArray.CreateBuilder<AccessibleNode>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var editableIds = new HashSet<string>(StringComparer.Ordinal);
            var supplementIds = new Dictionary<string, int>(StringComparer.Ordinal);
            var tableIds = new Dictionary<string, int>(StringComparer.Ordinal);
            void Visit(JsonElement node, string? parentId = null, bool ancestorBusy = false)
            {
                var id = node.GetProperty("nodeId").GetString()!;
                var scopedId = Scoped(id);
                if (!visited.Add(id))
                    return;
                var props = Properties(node);
                var busy = ancestorBusy || props.GetValueOrDefault("busy") is "true" or "1";
                var role = Value(node, "role") ?? "unknown";
                if (!node.GetProperty("ignored").GetBoolean())
                {
                    if (IsTableRole(role) && node.TryGetProperty("backendDOMNodeId", out var tableId))
                        tableIds[scopedId] = tableId.GetInt32();
                    if ((role is "checkbox" or "radio" or "option" or "treeitem" or "listitem" or "tab" or "textbox" or "searchbox"
                            || IsRangeRole(role) && !props.ContainsKey("editable"))
                        && node.TryGetProperty("backendDOMNodeId", out var supplementId))
                        supplementIds[scopedId] = supplementId.GetInt32();
                    ordered.Add(new(scopedId, role, (Value(node, "name") ?? "").Trim(),
                        IsFocused: node.TryGetProperty("backendDOMNodeId", out var focusedBackend)
                            && focusedBackend.GetInt32() == focusedBackendId
                            || role == "RootWebArea" && props.GetValueOrDefault("focused") == "true",
                        IsDisabled: props.GetValueOrDefault("disabled") == "true",
                        HeadingLevel: role == "heading" && int.TryParse(props.GetValueOrDefault("level"),
                            CultureInfo.InvariantCulture, out var level) ? level : null,
                        Checked: props.GetValueOrDefault("checked"),
                        Expanded: bool.TryParse(props.GetValueOrDefault("expanded"), out var expanded)
                            ? expanded : null,
                        Selected: bool.TryParse(props.GetValueOrDefault("selected"), out var selected)
                            ? selected : null,
                        IsRequired: props.GetValueOrDefault("required") == "true",
                        Invalid: props.GetValueOrDefault("invalid"),
                        Description: Value(node, "description"),
                        ActiveDescendantId: ActiveDescendant(node, byBackendId),
                        IsModal: props.GetValueOrDefault("modal") == "true",
                        ParentId: parentId,
                        LiveRegion: props.GetValueOrDefault("live") is { } live
                            ? new(live, props.GetValueOrDefault("atomic") == "true",
                                busy, "") : null,
                        Required: props.GetValueOrDefault("required") is "true" or "false" or "1" or "0"
                            ? new(props["required"] is "true" or "1", SemanticSource.ChromiumAccessibility) : null,
                        Text: role is "StaticText" or "LineBreak" && !props.ContainsKey("editable")
                            ? new(Value(node, "name") ?? "", SemanticSource.ChromiumAccessibility) : null,
                        FrameId: frame.Id, DocumentId: documentId,
                        Range: ReadRange(node, role, props),
                        HierarchyLevel: role == "treeitem" && int.TryParse(props.GetValueOrDefault("level"),
                            CultureInfo.InvariantCulture, out var hierarchyLevel)
                            ? new(hierarchyLevel, SemanticSource.ChromiumAccessibility) : null));
                    parentId = scopedId;
                }
                // Chromium can expose input contents as descendant StaticText, even when
                // the AX value property is omitted. Keep field semantics, drop editable contents.
                if (role is "textbox" or "searchbox" or "spinbutton"
                    || props.ContainsKey("editable"))
                {
                    editableIds.Add(scopedId);
                    return;
                }
                if (node.TryGetProperty("childIds", out var children))
                    foreach (var child in children.EnumerateArray())
                        if (byId.TryGetValue(child.GetString()!, out var childNode))
                            Visit(childNode, parentId, busy);
            }
            Visit(root, ownerNodeId);
            var nodes = ordered.ToImmutable();
            var supplemented = ImmutableArray.CreateBuilder<AccessibleNode>(nodes.Length);
            foreach (var node in nodes)
            {
                var table = tableIds.TryGetValue(node.Id, out var tableBackend)
                    ? await ReadTableAsync(tableBackend).ConfigureAwait(false) : null;
                if (!supplementIds.TryGetValue(node.Id, out var backendId)) { supplemented.Add(node with { Table = table }); continue; }
                var values = await ReadSemanticPropertiesAsync(backendId, node.Range is not null).ConfigureAwait(false);
                var required = node.Required;
                if (required is null && values.TryGetProperty("checkboxRequired", out var native)
                    && native.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    required = new(native.GetBoolean(), SemanticSource.NativeHtmlProperty);
                else if (required is null && values.TryGetProperty("required", out var declared)
                    && declared.GetString() is "true" or "false")
                    required = new(declared.GetString() == "true", SemanticSource.DeclaredAria);
                var positionText = values.GetProperty("position").GetString();
                var sizeText = values.GetProperty("size").GetString();
                LogicalSetPosition? logicalSet = null;
                if (positionText is not null || sizeText is not null)
                {
                    var position = int.TryParse(positionText, NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var parsedPosition) && parsedPosition > 0 ? (int?)parsedPosition : null;
                    var size = int.TryParse(sizeText, NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var parsedSize) && (parsedSize > 0 || parsedSize == -1) ? (int?)parsedSize : null;
                    var valid = (positionText is null || position is not null) && (sizeText is null || size is not null)
                        && !(position is int p && size is > 0 && p > size);
                    logicalSet = new(position, size, valid, SemanticSource.DeclaredAria);
                }
                var protection = node.Role is "textbox" or "searchbox"
                    && values.GetProperty("protected").ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? new ProtectedObservation(values.GetProperty("protected").GetBoolean(), SemanticSource.NativeHtmlProperty) : null;
                supplemented.Add(node with
                {
                    Table = table,
                    Required = required,
                    IsRequired = required?.Value ?? node.IsRequired,
                    LogicalSet = logicalSet,
                    Protection = protection,
                    Range = node.Range is { ValueText: null } range
                        && values.GetProperty("rangeValueText").GetString() is { Length: > 0 } rangeText
                        ? range with { ValueText = new(rangeText, SemanticSource.DeclaredAria) } : node.Range
                });
            }
            nodes = supplemented.ToImmutable();
            var childrenById = nodes.Where(node => node.ParentId is not null)
                .ToLookup(node => node.ParentId!);
            string LiveText(AccessibleNode node, string rootId)
            {
                if (editableIds.Contains(node.Id) || node.Role == "InlineTextBox"
                    || node.Id != rootId && node.LiveRegion is not null) return "";
                if (node.Role == "StaticText") return node.Name;
                var children = childrenById[node.Id].Select(child => LiveText(child, rootId))
                    .Where(text => !string.IsNullOrWhiteSpace(text)).ToArray();
                return children.Length > 0 ? string.Join(" ", children) : node.Name;
            }
            nodes = nodes.Select(node => node.LiveRegion is { } live
                ? node with { LiveRegion = live with { Text = LiveText(node, node.Id) } } : node).ToImmutableArray();
            var emittedIds = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
            return new(documentId, Scoped(root.GetProperty("nodeId").GetString()!), nodes,
                byBackendId.Where(item => emittedIds.Contains(item.Value)).ToDictionary());
        }
        catch (PlaywrightException exception)
        {
            throw new BrowserOperationException("CHROMIUM_CAPTURE_FAILED", exception);
        }
    }

    private async Task<JsonElement> ReadSemanticPropertiesAsync(int backendId, bool readRangeText)
    {
        var resolved = await _cdp.SendAsync("DOM.resolveNode", new Dictionary<string, object>
        { ["backendNodeId"] = backendId, ["objectGroup"] = "hsr-semantics" }).ConfigureAwait(false);
        if (resolved is null || !resolved.Value.GetProperty("object").TryGetProperty("objectId", out var objectId))
            throw new BrowserOperationException("SEMANTIC_NODE_UNAVAILABLE");
        var id = objectId.GetString()!;
        try
        {
            // Fixed, read-only standard semantics. Never read editable values or diagnostic attributes.
            var response = await _cdp.SendAsync("Runtime.callFunctionOn", new Dictionary<string, object>
            {
                ["objectId"] = id,
                ["returnByValue"] = true,
                ["arguments"] = new[] { new Dictionary<string, object> { ["value"] = readRangeText } },
                ["functionDeclaration"] = "function(readRangeText) { return { checkboxRequired: this.localName === 'input' && this.type === 'checkbox' ? this.required : null, protected: this.localName === 'input' ? this.type === 'password' : null, required: this.getAttribute('aria-required'), position: this.getAttribute('aria-posinset'), size: this.getAttribute('aria-setsize'), rangeValueText: readRangeText ? this.getAttribute('aria-valuetext') : null }; }"
            }).ConfigureAwait(false);
            if (response is null || response.Value.TryGetProperty("exceptionDetails", out _))
                throw new BrowserOperationException("SEMANTIC_PROPERTIES_UNAVAILABLE");
            return response.Value.GetProperty("result").GetProperty("value").Clone();
        }
        finally
        {
            await _cdp.SendAsync("Runtime.releaseObject", new Dictionary<string, object> { ["objectId"] = id })
                .ConfigureAwait(false);
        }
    }

    private static string? ActiveDescendant(JsonElement node, Dictionary<int, string> byBackendId)
    {
        if (node.TryGetProperty("properties", out var properties))
            foreach (var property in properties.EnumerateArray())
                if (property.GetProperty("name").GetString() == "activedescendant"
                    && property.GetProperty("value").TryGetProperty("relatedNodes", out var related))
                    foreach (var target in related.EnumerateArray())
                        if (target.TryGetProperty("backendDOMNodeId", out var id)
                            && byBackendId.TryGetValue(id.GetInt32(), out var axId))
                            return axId;
        return null;
    }

    public async Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!BrowserKeyPolicy.Allows(key))
            throw new BrowserOperationException("KEY_DENIED");
        try
        {
            await _page.Keyboard.PressAsync(key).ConfigureAwait(false);
        }
        catch (PlaywrightException exception)
        {
            throw new BrowserOperationException("CHROMIUM_INPUT_FAILED", exception);
        }
    }

    private static string? Value(JsonElement node, string name)
    {
        if (!node.TryGetProperty(name, out var property) || !property.TryGetProperty("value", out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => value.ToString()
        };
    }

    private static Dictionary<string, string> Properties(JsonElement node)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        if (node.TryGetProperty("properties", out var values))
            foreach (var property in values.EnumerateArray())
            {
                var value = Value(property, "value");
                if (value is not null)
                    properties[property.GetProperty("name").GetString()!] = value;
            }
        return properties;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            await _context.CloseAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await _browser.CloseAsync().ConfigureAwait(false);
            }
            finally
            {
                _playwright.Dispose();
            }
        }
    }
}
