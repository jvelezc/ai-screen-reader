using System.Collections.Immutable;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed record FrameCaptureOptions(int MaxFrames = 8, int MaxDepth = 3);

public sealed partial class ChromiumFixtureBrowser
{
    private sealed record FrameDescriptor(string Id, string? ParentId, string LoaderId, string Url, int Depth);
    private sealed record FrameDocument(string DocumentId, string RootNodeId,
        ImmutableArray<AccessibleNode> Nodes, Dictionary<int, string> BackendNodeIds);

    private async Task<int?> GetDocumentFocusAsync(int backendDocumentId)
    {
        var resolved = await _cdp.SendAsync("DOM.resolveNode", new Dictionary<string, object>
        { ["backendNodeId"] = backendDocumentId, ["objectGroup"] = "hsr-focus" }).ConfigureAwait(false);
        if (resolved is null || !resolved.Value.GetProperty("object").TryGetProperty("objectId", out var objectId))
            throw new BrowserOperationException("FRAME_FOCUS_UNAVAILABLE");
        var documentObject = objectId.GetString()!;
        try
        {
            // Read only the document's actual focus owner. Parent iframe elements do not
            // compete with the focused control in a captured child document.
            var focused = await _cdp.SendAsync("Runtime.callFunctionOn", new Dictionary<string, object>
            {
                ["objectId"] = documentObject,
                ["functionDeclaration"] = "function() { const active = this.activeElement; return this.hasFocus() && active && active.localName !== 'iframe' && active.localName !== 'frame' ? active : null; }"
            }).ConfigureAwait(false);
            if (focused is null || focused.Value.TryGetProperty("exceptionDetails", out _))
                throw new BrowserOperationException("FRAME_FOCUS_UNAVAILABLE");
            if (!focused.Value.GetProperty("result").TryGetProperty("objectId", out var activeId)) return null;
            var activeObject = activeId.GetString()!;
            try
            {
                var described = await _cdp.SendAsync("DOM.describeNode", new Dictionary<string, object>
                { ["objectId"] = activeObject }).ConfigureAwait(false);
                return described?.GetProperty("node").GetProperty("backendNodeId").GetInt32();
            }
            finally
            {
                await _cdp.SendAsync("Runtime.releaseObject", new Dictionary<string, object>
                { ["objectId"] = activeObject }).ConfigureAwait(false);
            }
        }
        finally
        {
            await _cdp.SendAsync("Runtime.releaseObject", new Dictionary<string, object>
            { ["objectId"] = documentObject }).ConfigureAwait(false);
        }
    }

    private async Task<List<FrameDescriptor>> ReadFrameTreeAsync()
    {
        var response = await _cdp.SendAsync("Page.getFrameTree").ConfigureAwait(false)
            ?? throw new BrowserOperationException("FRAME_TREE_MISSING");
        var result = new List<FrameDescriptor>();
        var pending = new Stack<(JsonElement Tree, string? ParentId, int Depth)>();
        pending.Push((response.GetProperty("frameTree"), null, 0));
        while (pending.TryPop(out var item))
        {
            var frame = item.Tree.GetProperty("frame");
            result.Add(new(frame.GetProperty("id").GetString()!, item.ParentId,
                frame.GetProperty("loaderId").GetString()!, frame.GetProperty("url").GetString()!, item.Depth));
            if (item.Tree.TryGetProperty("childFrames", out var children))
                foreach (var child in children.EnumerateArray().Reverse())
                    pending.Push((child, frame.GetProperty("id").GetString(), item.Depth + 1));
        }
        return result;
    }

    private async Task<AccessibilitySnapshot> CaptureCoreAsync()
    {
        try
        {
            var frames = await ReadFrameTreeAsync().ConfigureAwait(false);
            var coverage = new List<FrameObservation>();
            var documents = new Dictionary<string, FrameDocument>(StringComparer.Ordinal);
            var attempts = 0;
            foreach (var frame in frames)
            {
                string? ownerId = null;
                FrameObservationStatus? excluded = null;
                string? reason = null;
                if (frame.ParentId is not null)
                {
                    var parent = coverage.First(item => item.FrameId == frame.ParentId);
                    if (parent.Status != FrameObservationStatus.Observed)
                    { excluded = parent.Status; reason = "ANCESTOR_FRAME_NOT_OBSERVED"; }
                    else
                    {
                        try
                        {
                            var owner = await _cdp.SendAsync("DOM.getFrameOwner", new Dictionary<string, object>
                            { ["frameId"] = frame.Id }).ConfigureAwait(false);
                            if (owner is not null && documents[frame.ParentId].BackendNodeIds.TryGetValue(
                                owner.Value.GetProperty("backendNodeId").GetInt32(), out var id)) ownerId = id;
                            if (ownerId is null)
                            { excluded = FrameObservationStatus.Excluded; reason = "FRAME_OWNER_NOT_EXPOSED"; }
                        }
                        catch (PlaywrightException)
                        { excluded = FrameObservationStatus.Unavailable; reason = "FRAME_OWNER_UNAVAILABLE"; }
                    }
                }
                if (excluded is null && (frame.Depth > _frameOptions.MaxDepth || attempts >= _frameOptions.MaxFrames))
                { excluded = FrameObservationStatus.LimitExceeded; reason = "FRAME_CAPTURE_LIMIT"; }
                if (excluded is null && frame.ParentId is not null && frame.Url is not "about:srcdoc" and not "about:blank")
                { excluded = FrameObservationStatus.Unsupported; reason = "FRAME_DOCUMENT_UNSUPPORTED"; }
                if (excluded is not null)
                {
                    coverage.Add(new(frame.Id, frame.ParentId, ownerId, null, null, excluded.Value, reason));
                    continue;
                }
                attempts++;
                try
                {
                    var document = await CaptureDocumentAsync(frame, ownerId).ConfigureAwait(false);
                    documents.Add(frame.Id, document);
                    coverage.Add(new(frame.Id, frame.ParentId, ownerId, document.DocumentId,
                        document.RootNodeId, FrameObservationStatus.Observed));
                }
                catch (Exception exception) when (frame.ParentId is not null
                    && exception is PlaywrightException or BrowserOperationException)
                {
                    coverage.Add(new(frame.Id, frame.ParentId, ownerId, null, null, FrameObservationStatus.Unavailable,
                        exception is BrowserOperationException operation ? operation.Code : "FRAME_CAPTURE_UNAVAILABLE"));
                }
            }
            var after = await ReadFrameTreeAsync().ConfigureAwait(false);
            if (!frames.SequenceEqual(after)) throw new BrowserOperationException("FRAME_TREE_CHANGED");
            var children = coverage.Where(frame => frame.Status == FrameObservationStatus.Observed && frame.OwnerNodeId is not null)
                .ToDictionary(frame => frame.OwnerNodeId!, frame => frame.FrameId, StringComparer.Ordinal);
            var ordered = ImmutableArray.CreateBuilder<AccessibleNode>();
            void Append(string frameId)
            {
                foreach (var node in documents[frameId].Nodes)
                {
                    ordered.Add(node);
                    if (children.TryGetValue(node.Id, out var child)) Append(child);
                }
            }
            Append(frames[0].Id);
            var nodes = ordered.ToImmutable();
            if (nodes.Count(node => node.IsFocused && node.Role != "RootWebArea") > 1)
                throw new BrowserOperationException("FRAME_FOCUS_AMBIGUOUS");
            return new(documents[frames[0].Id].DocumentId, nodes,
                ["Only trusted same-target about:srcdoc/about:blank child documents are supported; frame coverage is explicit.",
                 "Frame captures are bounded and sequential, not an atomic cross-document observation.",
                 "Widget coverage is limited to the bundled patterns.",
                 "Live changes are snapshot observations, not a speech scheduler; intermediate updates can be missed.",
                 "Exposed peer counts and declared logical set positions are separate observations.",
                 "Required and logical positions carry explicit AX/native/declared source provenance.",
                 "Text navigation uses exposed text runs; newline-delimited lines do not describe visual wrapping."])
            { Frames = coverage.ToImmutableArray() };
        }
        catch (PlaywrightException exception)
        {
            throw new BrowserOperationException("CHROMIUM_CAPTURE_FAILED", exception);
        }
    }
}
