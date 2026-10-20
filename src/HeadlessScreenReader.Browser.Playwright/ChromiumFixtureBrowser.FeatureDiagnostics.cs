using System.Collections.Immutable;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed partial class ChromiumFixtureBrowser : IFeatureDiagnostics
{
    public async Task<FeatureDiscovery> CaptureFeatureDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_publicPolicy is not null) throw new BrowserOperationException("PUBLIC_FEATURE_DIAGNOSTICS_UNAVAILABLE");
        try
        {
            return await _captureAdmission.RunAsync(CaptureFeatureDiagnosticsCoreAsync)
                .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException) { throw new BrowserOperationException("FEATURE_DISCOVERY_TIMEOUT"); }
        catch (PlaywrightException) { throw new BrowserOperationException("FEATURE_DISCOVERY_FAILED"); }
        catch (BrowserOperationException exception) { throw new BrowserOperationException(exception.Code); }
    }

    private async Task<FeatureDiscovery> CaptureFeatureDiagnosticsCoreAsync()
    {
        var frames = await ReadFrameTreeAsync().ConfigureAwait(false);
        var document = await CaptureDocumentAsync(frames[0], null).ConfigureAwait(false);
        var candidates = document.Nodes.Where(n => n.Role is "button" or "link"
            && n.Editability is null && n.Protection?.Value != true).ToArray();
        var backend = document.BackendNodeIds.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
        var hints = new List<(AccessibleNode Node, DiagnosticFeatureHint? Hint, DiagnosticHintState State, bool Connected)>();
        foreach (var node in candidates.Take(64))
        {
            if (!backend.TryGetValue(node.Id, out var backendId))
            { hints.Add((node, null, DiagnosticHintState.Unavailable, false)); continue; }
            var hint = await ReadFeatureHintAsync(backendId).ConfigureAwait(false);
            hints.Add((node, hint.Hint, hint.State, hint.Connected));
        }
        var afterFrames = await ReadFrameTreeAsync().ConfigureAwait(false);
        var sameFrame = frames[0] == afterFrames[0];
        var after = sameFrame ? await CaptureDocumentAsync(afterFrames[0], null).ConfigureAwait(false) : null;
        var stableDocument = after?.DocumentId == document.DocumentId;
        var entries = hints.Select((hint, index) =>
        {
            var current = after?.Nodes.FirstOrDefault(n => n.Id == hint.Node.Id);
            var stable = stableDocument && hint.Connected && current is not null
                && JsonSerializer.Serialize(current) == JsonSerializer.Serialize(hint.Node);
            return new FeatureCandidate($"feature-{index + 1}", hint.Node, hint.Hint, hint.State, stable);
        }).ToImmutableArray();
        return new(document.DocumentId, _captureAdmission.Status.Epoch, entries, candidates.Length > 64,
            ["Trusted fixture diagnostics only; main document, at most 64 exposed noneditable buttons/links.",
             "Declared data-hsr-feature is an untrusted diagnostic hypothesis, never a reader name or verified goal.",
             "Sequential acquisition is not atomic. Stable identities/semantics are checked before and after hint reads.",
             "Hidden, unexposed, child-frame and non-button/link features are outside this inventory; absence proves no completeness."]);
    }

    private async Task<(DiagnosticFeatureHint? Hint, DiagnosticHintState State, bool Connected)> ReadFeatureHintAsync(int backendId)
    {
        string? objectId = null;
        try
        {
            var resolved = await _cdp.SendAsync("DOM.resolveNode", new Dictionary<string, object>
            { ["backendNodeId"] = backendId, ["objectGroup"] = "hsr-feature-diagnostics" }).ConfigureAwait(false);
            if (resolved is null || !resolved.Value.GetProperty("object").TryGetProperty("objectId", out var id))
                return (null, DiagnosticHintState.Unavailable, false);
            objectId = id.GetString()!;
            var result = await _cdp.SendAsync("Runtime.callFunctionOn", new Dictionary<string, object>
            {
                ["objectId"] = objectId,
                ["returnByValue"] = true,
                // Fixed diagnostic attribute only. No editable values, text scraping, URLs or input.
                ["functionDeclaration"] = "function() { const hint = this.getAttribute('data-hsr-feature'); return { connected: this.isConnected, oversized: hint !== null && hint.length > 128, hint: hint !== null && hint.length <= 128 ? hint : null }; }"
            }).ConfigureAwait(false);
            if (result is null || result.Value.TryGetProperty("exceptionDetails", out _))
                return (null, DiagnosticHintState.Unavailable, false);
            var value = result.Value.GetProperty("result").GetProperty("value");
            var connected = value.GetProperty("connected").GetBoolean();
            if (value.GetProperty("oversized").GetBoolean()) return (null, DiagnosticHintState.RejectedOversize, connected);
            var text = value.GetProperty("hint").GetString()?.Trim();
            return string.IsNullOrEmpty(text) ? (null, DiagnosticHintState.Absent, connected)
                : (new(text, DiagnosticHintSource.DeclaredFeatureAttribute), DiagnosticHintState.Observed, connected);
        }
        catch (PlaywrightException) { return (null, DiagnosticHintState.Unavailable, false); }
        finally
        {
            if (objectId is not null)
                try { await _cdp.SendAsync("Runtime.releaseObject", new Dictionary<string, object> { ["objectId"] = objectId }).ConfigureAwait(false); }
                catch (PlaywrightException) { /* A detached diagnostic node remains unavailable. */ }
        }
    }
}
