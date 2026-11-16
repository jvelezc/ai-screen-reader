using System.Collections.Immutable;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Cli;

internal static class ControlledSearchPointer
{
    internal static async Task<SearchPointerRecording> RecordAsync(string folder, BundledFixture fixture,
        Func<SyntheticSearchInputIntent, CancellationToken, Task<SyntheticSearchInputDecision>> authorize,
        Func<CancellationToken, Task> authorizeMode,
        Func<string, long, ImmutableArray<CompletedCaptureSource>, CancellationToken, Task> retain, CancellationToken token)
    {
        _ = SyntheticSearchSites.Describe(fixture);
        ChromiumRecordingProbe? probe = null; SearchFocusReceipt? focus = null; ReaderStep? entry = null;
        AccessibilitySnapshot? after = null; PointerTaskReceipt? activation = null; RecordingProbeMedia? media = null;
        ImmutableArray<ReaderEvent> timeline = []; DateTimeOffset? readerStarted = null;
        var truncated = false; var stage = "Launch"; string? error = null;
        var document = ""; long epoch = 0;
        Task Sources(ImmutableArray<CompletedCaptureSource> originals, CancellationToken cancellation) => retain(document, epoch, originals, cancellation);
        try
        {
            await using var owned = await ChromiumFixtureBrowser.LaunchGuardedSyntheticSearchRecordingAsync(folder, fixture, async (intent, deadline) =>
            {
                await probe!.CopyCompletedSourcesAsync(Sources, deadline).ConfigureAwait(false);
                return await authorize(intent, deadline).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            probe = owned;
            var initial = await owned.Browser.CaptureAsync(token).ConfigureAwait(false);
            document = initial.DocumentId; epoch = initial.CaptureEpoch;
            stage = "MouseFocus";
            focus = await owned.FocusSyntheticSearchAsync(token).ConfigureAwait(false);
            if (focus is { FocusObserved: true, InputCompleted: true, ErrorCode: null })
            {
                stage = "QueryEntry";
                readerStarted = DateTimeOffset.UtcNow;
                using var reader = new ScreenReaderSession(owned.Browser);
                await owned.CopyCompletedSourcesAsync(Sources, token).ConfigureAwait(false);
                await authorizeMode(token).ConfigureAwait(false);
                await reader.ExecuteAsync(ReaderCommand.EnterFocusMode, cancellationToken: token).ConfigureAwait(false);
                entry = await reader.ExecuteAsync(ReaderCommand.EnterSearchText, SyntheticSearchPolicy.Query, cancellationToken: token).ConfigureAwait(false);
                timeline = reader.Timeline; truncated = reader.TimelineTruncated;
                token.ThrowIfCancellationRequested();
                after = await owned.Browser.CaptureAsync(token).ConfigureAwait(false);
                if (entry is { ErrorCode: null, BrowserKeystrokes: 13, SearchInput: { ValueVerified: true, CompletedCharacters: 13 } }
                    && after.FocusedNode is { } node)
                {
                    stage = "MouseActivation";
                    activation = await owned.ObserveSyntheticSearchPointerAsync(new("e", after.DocumentId, after.CaptureEpoch,
                        node.Id, SyntheticSearchPolicy.Query[..^1]), token).ConfigureAwait(false);
                }
                else error = "SEARCH_POINTER_ENTRY_UNCONFIRMED";
            }
            else error = "SEARCH_POINTER_FOCUS_UNCONFIRMED";
            token.ThrowIfCancellationRequested();
            await Task.Delay(120, token).ConfigureAwait(false);
            stage = "FinalizeAndDecodeOriginalMedia";
            media = await owned.FinishAndInspectAsync(Sources, token).ConfigureAwait(false);
            stage = "Complete";
        }
        catch (OperationCanceledException) { error = "SEARCH_POINTER_CANCELLED"; }
        catch (BrowserOperationException exception) { error = exception.Code; }
        catch (Exception exception) when (exception is IOException or TimeoutException or Microsoft.Playwright.PlaywrightException or UnauthorizedAccessException)
        { error = "SEARCH_POINTER_UNCONFIRMED"; }
        return new(new(focus, readerStarted, entry, timeline, after, error) { TimelineTruncated = truncated }, activation,
            probe?.Environment, media, probe?.CaptureStatus, probe?.ShutdownStatus, stage, error)
        { DecodeAttempts = media is null ? probe?.DecodeAttempts : null };
    }
}
