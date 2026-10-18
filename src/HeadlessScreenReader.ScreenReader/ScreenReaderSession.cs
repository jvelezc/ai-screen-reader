using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.ScreenReader;

/// <summary>One local controller. Dispose after all calls finish; hosted controller fencing is separate.</summary>
public sealed class ScreenReaderSession : IDisposable
{
    public const string Profile = "hsr-desktop-reader-v7";
    private readonly ReaderTextNavigator _text = new();
    private readonly IScreenReaderBrowser _browser;
    private readonly int _maxCommands;
    private readonly ObservationWaitOptions _wait;
    private readonly TimeProvider _time;
    private readonly long _started;
    private readonly int _maxTimelineEvents;
    private readonly SemaphoreSlim _inputGate = new(1, 1);
    private readonly object _timelineLock = new();
    private readonly List<ReaderEvent> _timeline = [];
    private readonly Dictionary<string, string> _liveText = new(StringComparer.Ordinal);
    private AccessibilitySnapshot? _snapshot;
    private string? _cursorId;
    private ReaderMode _mode;
    private ReaderCommand _command;
    private int _commandsUsed;
    private long _commandSequence;
    private long _eventSequence;
    private bool _inputAttempted;
    private int _completedKeystrokes;
    private bool _disposed;

    public ScreenReaderSession(
        IScreenReaderBrowser browser, int maxCommands = 100,
        ObservationWaitOptions? waitOptions = null, TimeProvider? timeProvider = null,
        int maxTimelineEvents = 10_000)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCommands, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTimelineEvents, 1);
        _wait = waitOptions ?? new();
        if (_wait.Timeout <= TimeSpan.Zero || _wait.PollInterval <= TimeSpan.Zero || _wait.MaxPolls < 1)
            throw new ArgumentOutOfRangeException(nameof(waitOptions), "Wait bounds must be positive.");
        _browser = browser;
        _maxCommands = maxCommands;
        _maxTimelineEvents = maxTimelineEvents;
        _time = timeProvider ?? TimeProvider.System;
        _started = _time.GetTimestamp();
    }

    public ImmutableArray<ReaderEvent> Timeline
    {
        get { lock (_timelineLock) return _timeline.ToImmutableArray(); }
    }

    public bool TimelineTruncated { get; private set; }

    public async Task<ReaderStep> ExecuteAsync(
        ReaderCommand command, string? key = null, CancellationToken cancellationToken = default,
        ObservationExpectation? expectation = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _inputGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        ReaderStep? step = null;
        try
        {
            _command = command;
            _commandSequence++;
            _inputAttempted = false;
            _completedKeystrokes = 0;
            Record(ReaderEventKind.CommandStarted, command.ToString());
            cancellationToken.ThrowIfCancellationRequested();
            step = ReaderTextNavigator.IsCommand(command) && expectation is not null
                ? Result(ReaderOutcome.Unsupported, "Text navigation does not accept semantic goal expectations.")
                : await RunCommandAsync(command, key, cancellationToken).ConfigureAwait(false);
            if (expectation is not null)
            {
                if (step.Outcome is ReaderOutcome.Observed or ReaderOutcome.CursorMoved)
                    step = await VerifyAsync(step, expectation, cancellationToken).ConfigureAwait(false);
                else if (step.Outcome == ReaderOutcome.Blocked)
                    step = step with { Verification = VerificationStatus.Blocked };
            }
            return step;
        }
        catch (OperationCanceledException) when (_inputAttempted)
        {
            Record(ReaderEventKind.Cancelled, "Post-input observation interrupted.");
            step = Result(ReaderOutcome.Unknown, "Observation interrupted; destination not verified.",
                inputMayHaveOccurred: true) with
            { Verification = VerificationStatus.Interrupted };
            return step;
        }
        catch (BrowserOperationException exception)
        {
            Record(ReaderEventKind.BrowserError, exception.Code);
            _cursorId = null;
            _snapshot = null;
            _text.Clear();
            step = Result(ReaderOutcome.BrowserError, "Browser observation or input failed.",
                errorCode: exception.Code, inputMayHaveOccurred: _inputAttempted);
            return step;
        }
        catch (OperationCanceledException)
        {
            Record(ReaderEventKind.Cancelled, "Cancelled before input.");
            throw;
        }
        finally
        {
            if (step is not null)
                Record(ReaderEventKind.Announcement, step.Announcement, step.CursorNodeId);
            Record(ReaderEventKind.CommandCompleted, step?.Outcome.ToString() ?? "No result");
            _inputGate.Release();
        }
    }

    private async Task<ReaderStep> RunCommandAsync(ReaderCommand command, string? key, CancellationToken token)
    {
        if (_commandsUsed >= _maxCommands)
            return Result(ReaderOutcome.BudgetExceeded, "Command budget exhausted.");
        _commandsUsed++;
        if (!Enum.IsDefined(command))
            return Result(ReaderOutcome.Unsupported, "Unsupported reader command.");
        if (command == ReaderCommand.ObserveChanges)
            return Result(ReaderOutcome.Unsupported, "Use the bounded observation stream for idle changes.");
        if (command == ReaderCommand.PressKey && !BrowserKeyPolicy.Allows(key))
            return Result(ReaderOutcome.Denied, "Keyboard operation denied by the local fixture policy.");
        await RefreshAsync(token).ConfigureAwait(false);
        if (command == ReaderCommand.EnterBrowseMode)
        {
            _text.Clear();
            _mode = ReaderMode.Browse;
            return Result(ReaderOutcome.Observed, "Browse mode.");
        }
        if (command == ReaderCommand.EnterFocusMode)
        {
            if (_snapshot!.FocusedNode is null)
                return Result(ReaderOutcome.FocusRequired, "Browser focus required.");
            _mode = ReaderMode.Focus;
            _text.Clear();
            _cursorId = _snapshot.EffectiveFocusedNode?.Id;
            return Result(ReaderOutcome.Observed, "Focus mode.");
        }
        if (command is ReaderCommand.Tab or ReaderCommand.ReverseTab or ReaderCommand.Escape)
            return await DispatchAsync(command switch
            {
                ReaderCommand.Tab => "Tab",
                ReaderCommand.ReverseTab => "Shift+Tab",
                _ => "Escape"
            }, token, synchronizeMode: true).ConfigureAwait(false);
        if (command == ReaderCommand.PressKey)
        {
            if (_mode != ReaderMode.Focus)
                return Result(ReaderOutcome.Unsupported, "Explicit browser keys require focus mode.");
            return await DispatchAsync(key!, token).ConfigureAwait(false);
        }
        string? shortcut = command switch
        {
            ReaderCommand.NextHeading => "h",
            ReaderCommand.NextLink => "k",
            ReaderCommand.NextButton => "b",
            ReaderCommand.NextLandmark => "d",
            _ => null
        };
        if (_mode == ReaderMode.Focus && shortcut is not null)
            return await DispatchAsync(shortcut, token).ConfigureAwait(false);
        if (command == ReaderCommand.Activate)
            return await ActivateAsync(token).ConfigureAwait(false);
        if (command == ReaderCommand.ReadCurrent)
            return Result(ReaderOutcome.Observed, Describe(CurrentNode()));
        if (_mode != ReaderMode.Browse)
            return Result(ReaderOutcome.Unsupported, "Reading navigation requires browse mode.");
        if (ReaderTextNavigator.IsCommand(command))
        {
            var navigation = _text.Move(_snapshot!, CurrentNode()?.Id, command);
            if (navigation.Range is { } range) _cursorId = range.NodeId;
            return Result(navigation.Outcome, navigation.Announcement) with
            { TextRange = navigation.Range, TextCursorReset = navigation.Reset };
        }
        return MoveCursor(command);
    }

    private ReaderStep MoveCursor(ReaderCommand command)
    {
        var nodes = _snapshot!.Nodes;
        if (command is ReaderCommand.DocumentStart or ReaderCommand.DocumentEnd)
        {
            if (nodes.IsEmpty) return Result(ReaderOutcome.NoMatch, "No exposed content.");
            var boundary = command == ReaderCommand.DocumentStart ? nodes[0] : nodes[^1];
            _text.Clear();
            _cursorId = boundary.Id;
            return Result(ReaderOutcome.CursorMoved, Describe(boundary));
        }
        var start = -1;
        for (var i = 0; i < nodes.Length; i++)
            if (nodes[i].Id == _cursorId)
                start = i;
        var backwards = command is ReaderCommand.MovePrevious or ReaderCommand.PreviousHeading
            or ReaderCommand.PreviousLink or ReaderCommand.PreviousButton or ReaderCommand.PreviousLandmark or ReaderCommand.PreviousFrame;
        if (backwards && start < 0)
            start = nodes.Length;
        // Moving to a previous landmark leaves the current enclosing context.
        // Only observed ancestors are skipped; flat node order cannot prove enclosure.
        var enclosing = command == ReaderCommand.PreviousLandmark
            ? ReaderContextBuilder.Build(_snapshot, CurrentNode())?.Ancestors
                .Select(container => container.NodeId).ToHashSet(StringComparer.Ordinal)
            : null;
        for (var i = start + (backwards ? -1 : 1); i >= 0 && i < nodes.Length; i += backwards ? -1 : 1)
        {
            var node = nodes[i];
            var matches = command switch
            {
                ReaderCommand.MoveNext or ReaderCommand.MovePrevious => true,
                ReaderCommand.NextHeading or ReaderCommand.PreviousHeading => node.Role == "heading",
                ReaderCommand.NextLink or ReaderCommand.PreviousLink => node.Role == "link",
                ReaderCommand.NextButton or ReaderCommand.PreviousButton => node.Role == "button",
                ReaderCommand.NextFrame or ReaderCommand.PreviousFrame => node.Role is "Iframe" or "IframePresentational",
                ReaderCommand.NextLandmark or ReaderCommand.PreviousLandmark => node.Role is "main" or "navigation" or "banner"
                    or "contentinfo" or "complementary" or "search" or "region" or "form",
                _ => false
            };
            if (!matches || enclosing?.Contains(node.Id) == true)
                continue;
            _cursorId = node.Id;
            _text.Clear();
            return Result(ReaderOutcome.CursorMoved, Describe(node));
        }
        return Result(ReaderOutcome.NoMatch, "No matching exposed content.");
    }

    private async Task<ReaderStep> ActivateAsync(CancellationToken token)
    {
        var current = CurrentNode();
        if (current is null)
            return Result(ReaderOutcome.NoMatch, "No current control.");
        if (current.IsDisabled || _snapshot!.FocusedNode?.IsDisabled == true)
            return Result(ReaderOutcome.Blocked, $"{Describe(current)}. Current control is disabled.")
                with
            { BarrierCode = "OBSERVED_DISABLED_CONTROL" };
        if (_snapshot!.EffectiveFocusedNode?.Id != current.Id)
            return Result(ReaderOutcome.FocusRequired, "Move browser focus to the current control first.");
        // A composite's active descendant identifies the announced item. Input still goes to its DOM owner.
        var owner = _snapshot.FocusedNode!;
        var key = owner.Role switch
        {
            "button" or "link" or "menuitem" or "tab" or "combobox" => "Enter",
            "checkbox" or "radio" or "switch" => "Space",
            _ => null
        };
        if (key is null)
            return Result(ReaderOutcome.Unsupported, "Keyboard activation for this role is not implemented.");
        return await DispatchAsync(key, token).ConfigureAwait(false);
    }

    private async Task<ReaderStep> DispatchAsync(string key, CancellationToken token, bool synchronizeMode = false)
    {
        if (!BrowserKeyPolicy.Allows(key))
            return Result(ReaderOutcome.Denied, "Keyboard operation denied.");
        token.ThrowIfCancellationRequested();
        _inputAttempted = true;
        _text.Clear();
        Record(ReaderEventKind.InputAttempted, key, _snapshot?.FocusedNode?.Id);
        await _browser.PressKeyAsync(key, token).ConfigureAwait(false);
        _completedKeystrokes = 1;
        Record(ReaderEventKind.InputCompleted, key, _snapshot?.FocusedNode?.Id);
        // Never abandon an in-flight key. Observation is read-only and can be cancelled separately.
        await RefreshAsync(token).ConfigureAwait(false);
        _cursorId = _snapshot!.EffectiveFocusedNode?.Id;
        if (synchronizeMode)
            _mode = _snapshot.FocusedNode?.Role is "textbox" or "searchbox" or "combobox" or "spinbutton"
                or "checkbox" or "radio" or "listbox" or "slider" or "tree" or "grid"
                ? ReaderMode.Focus : ReaderMode.Browse;
        return Result(ReaderOutcome.Observed, Describe(CurrentNode()));
    }

    private async Task<ReaderStep> VerifyAsync(
        ReaderStep step, ObservationExpectation expectation, CancellationToken token)
    {
        using var deadline = new CancellationTokenSource(_wait.Timeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        try
        {
            for (var poll = 0; poll < _wait.MaxPolls; poll++)
            {
                linked.Token.ThrowIfCancellationRequested();
                if (expectation.IsSatisfiedBy(_snapshot!))
                {
                    Record(ReaderEventKind.GoalVerified, $"{expectation.Role}: {expectation.Name}");
                    return step with
                    {
                        Verification = VerificationStatus.Satisfied,
                        Announcement = Describe(CurrentNode()),
                        CursorNodeId = _cursorId,
                        FocusedNodeId = _snapshot!.FocusedNode?.Id,
                        ActiveDescendantNodeId = _snapshot.FocusedNode?.ActiveDescendantId,
                        Context = ReaderContextBuilder.Build(_snapshot, CurrentNode()),
                        Required = CurrentNode()?.Required,
                        Protection = CurrentNode()?.Protection,
                        Frame = CurrentFrame(),
                        FrameCoverage = _snapshot.Frames,
                        Mode = _mode
                    };
                }
                if (poll == _wait.MaxPolls - 1)
                    break;
                await Task.Delay(_wait.PollInterval, _time, linked.Token).ConfigureAwait(false);
                await RefreshAsync(linked.Token).ConfigureAwait(false);
                if (_inputAttempted || _mode == ReaderMode.Focus)
                    _cursorId = _snapshot!.EffectiveFocusedNode?.Id;
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !token.IsCancellationRequested)
        {
            // Exhausting a wait proves no barrier; it leaves the requested destination unknown.
        }
        Record(ReaderEventKind.WaitTimedOut, "Destination was not observed within the wait bounds.");
        return Result(ReaderOutcome.Unknown, "Destination not verified within observation bounds.",
            inputMayHaveOccurred: _inputAttempted) with
        { Verification = VerificationStatus.TimedOut };
    }

    private async Task RefreshAsync(CancellationToken token)
    {
        var next = await _browser.CaptureAsync(token).ConfigureAwait(false);
        var previous = _snapshot;
        if (previous?.DocumentId != next.DocumentId)
        {
            _cursorId = null;
            _mode = ReaderMode.Browse;
        }
        else if (!next.Nodes.Any(node => node.Id == _cursorId))
            _cursorId = null;
        _snapshot = next;
        // Focus mode announces the effective browser focus. Its returned cursor and
        // announcement event must identify that same node, including external changes.
        if (_mode == ReaderMode.Focus)
            _cursorId = next.EffectiveFocusedNode?.Id;
        Record(ReaderEventKind.Observation, $"Captured {next.Nodes.Length} exposed nodes.");
        ObserveFrames(next, previous);
        ObserveLiveRegions(next, previous);
        if (previous?.FocusedNode?.Id != next.FocusedNode?.Id)
            Record(ReaderEventKind.FocusChanged, Describe(next.FocusedNode), next.FocusedNode?.Id,
                previous?.FocusedNode?.Id);
        if (previous?.FocusedNode?.ActiveDescendantId != next.FocusedNode?.ActiveDescendantId)
            Record(ReaderEventKind.ActiveDescendantChanged, Describe(next.EffectiveFocusedNode),
                next.FocusedNode?.ActiveDescendantId, previous?.FocusedNode?.ActiveDescendantId);
        var oldNodes = previous?.Nodes.ToDictionary(node => node.Id) ?? [];
        foreach (var node in next.Nodes)
        {
            if (node.Role is "dialog" or "alertdialog" && !oldNodes.ContainsKey(node.Id))
                Record(ReaderEventKind.DialogOpened, Describe(node), node.Id);
            if (oldNodes.TryGetValue(node.Id, out var old)
                && (old.Checked != node.Checked || old.Expanded != node.Expanded || old.Selected != node.Selected
                    || old.Invalid != node.Invalid || old.IsDisabled != node.IsDisabled
                    || old.Required != node.Required || old.LogicalSet != node.LogicalSet || old.Protection != node.Protection))
                Record(ReaderEventKind.WidgetStateChanged, Describe(node), node.Id);
        }
        if (previous is not null)
            foreach (var node in previous.Nodes)
                if (node.Role is "dialog" or "alertdialog" && !next.Nodes.Any(current => current.Id == node.Id))
                    Record(ReaderEventKind.DialogClosed, Describe(node), node.Id);
    }

    private AccessibleNode? CurrentNode() =>
        _mode == ReaderMode.Focus ? _snapshot?.EffectiveFocusedNode
            : _snapshot?.Nodes.FirstOrDefault(node => node.Id == _cursorId) ?? _snapshot?.EffectiveFocusedNode;

    private ReaderStep Result(
        ReaderOutcome outcome, string announcement, string? errorCode = null, bool inputMayHaveOccurred = false) =>
        new(_commandSequence, _command, _mode, outcome, announcement, _cursorId,
            _snapshot?.FocusedNode?.Id, _completedKeystrokes, errorCode, inputMayHaveOccurred,
            _snapshot?.FocusedNode?.ActiveDescendantId)
        {
            Context = ReaderContextBuilder.Build(_snapshot, CurrentNode()),
            Required = CurrentNode()?.Required,
            Protection = CurrentNode()?.Protection,
            Frame = CurrentFrame(),
            FrameCoverage = _snapshot?.Frames ?? []
        };

    private FrameObservation? CurrentFrame()
    {
        var frameId = CurrentNode()?.FrameId;
        return frameId is null ? null : _snapshot?.Frames.FirstOrDefault(frame => frame.FrameId == frameId);
    }

    private void ObserveFrames(AccessibilitySnapshot next, AccessibilitySnapshot? previous)
    {
        var old = previous?.Frames.ToDictionary(frame => frame.FrameId) ?? [];
        foreach (var frame in next.Frames)
            if (!old.TryGetValue(frame.FrameId, out var prior) || frame != prior)
                Record(ReaderEventKind.FrameChanged, $"Frame coverage: {frame.Status}; {frame.ReasonCode ?? "document observed"}.",
                    frame.OwnerNodeId ?? frame.RootNodeId, frame: frame);
        var present = next.Frames.Select(frame => frame.FrameId).ToHashSet(StringComparer.Ordinal);
        foreach (var frame in old.Values.Where(frame => !present.Contains(frame.FrameId)))
            Record(ReaderEventKind.FrameChanged, "Frame detached.", frame.OwnerNodeId,
                frame: frame with { Status = FrameObservationStatus.Unavailable, ReasonCode = "FRAME_DETACHED" });
    }

    private void ObserveLiveRegions(AccessibilitySnapshot next, AccessibilitySnapshot? previous)
    {
        var sameDocument = previous?.DocumentId == next.DocumentId;
        if (!sameDocument) _liveText.Clear();
        var observedDocuments = previous?.Nodes.Where(node => node.DocumentId is not null)
            .Select(node => node.DocumentId!).ToHashSet(StringComparer.Ordinal) ?? [];
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in next.Nodes)
        {
            if (node.LiveRegion is not { } live) continue;
            present.Add(node.Id);
            var sameSourceDocument = sameDocument && (node.DocumentId is null || observedDocuments.Contains(node.DocumentId));
            if (sameSourceDocument && live.IsBusy) continue;
            var changed = !_liveText.TryGetValue(node.Id, out var text) || text != live.Text;
            _liveText[node.Id] = live.Text;
            if (sameSourceDocument && changed && !string.IsNullOrWhiteSpace(live.Text)
                && live.Politeness is "polite" or "assertive")
                Record(ReaderEventKind.LiveRegionChanged, live.Text, node.Id, liveRegion: live);
        }
        foreach (var id in _liveText.Keys.Where(id => !present.Contains(id)).ToArray()) _liveText.Remove(id);
    }

    private void Record(ReaderEventKind kind, string detail, string? nodeId = null, string? previousNodeId = null,
        LiveRegionObservation? liveRegion = null, FrameObservation? frame = null)
    {
        lock (_timelineLock)
        {
            if (_timeline.Count >= _maxTimelineEvents)
            {
                TimelineTruncated = true;
                return;
            }
            if (frame is null && nodeId is not null)
            {
                var frameId = _snapshot?.Nodes.FirstOrDefault(node => node.Id == nodeId)?.FrameId;
                if (frameId is not null) frame = _snapshot?.Frames.FirstOrDefault(item => item.FrameId == frameId);
            }
            _timeline.Add(new(++_eventSequence, _commandSequence,
                _time.GetElapsedTime(_started).TotalMilliseconds, kind, _command,
                _snapshot?.DocumentId, nodeId, detail, previousNodeId, liveRegion, frame));
        }
    }

    private static string Describe(AccessibleNode? node)
    {
        if (node is null)
            return "No current exposed content.";
        var role = node.Role == "heading" && node.HeadingLevel is int level
            ? $"heading level {level}" : node.Role;
        var text = string.IsNullOrWhiteSpace(node.Name) ? role : $"{node.Name}, {role}";
        if (node.IsDisabled) text += ", disabled";
        if (node.Protection?.Value == true) text += ", protected";
        if (node.Checked is not null)
            text += $", {node.Checked switch { "true" => "checked", "mixed" => "mixed", _ => "not checked" }}";
        if (node.Expanded is bool expanded) text += expanded ? ", expanded" : ", collapsed";
        if (node.Selected == true) text += ", selected";
        if (node.Required?.Value ?? node.IsRequired) text += ", required";
        if (node.LogicalSet is { IsValid: true, Position: int position } set)
            text += set.Size is > 0 ? $", item {position} of {set.Size}" : $", item {position}, set size unknown";
        if (node.Invalid is not null and not "false") text += ", invalid";
        if (!string.IsNullOrWhiteSpace(node.Description)) text += $". {node.Description}";
        return text;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _inputGate.Dispose();
    }

    public async IAsyncEnumerable<ReaderObservationBatch> ObserveChangesAsync(
        ReaderObservationOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        options ??= new();
        if (options.Duration <= TimeSpan.Zero || options.PollInterval <= TimeSpan.Zero || options.MaxCaptures < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "Observation bounds must be positive.");
        using var deadline = new CancellationTokenSource(options.Duration, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var captures = 0;
        while (captures < options.MaxCaptures)
        {
            ReaderObservationBatch batch;
            try
            {
                if (captures > 0) await Task.Delay(options.PollInterval, _time, linked.Token).ConfigureAwait(false);
                await _inputGate.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    linked.Token.ThrowIfCancellationRequested();
                    if (_commandsUsed >= _maxCommands)
                        batch = new(ObservationStreamStatus.CommandBudgetExceeded, captures, []);
                    else if (TimelineTruncated)
                        batch = new(ObservationStreamStatus.TimelineTruncated, captures, []);
                    else
                    {
                        _commandsUsed++;
                        batch = await CaptureObservationAsync(captures, linked.Token, cancellationToken).ConfigureAwait(false);
                        captures = batch.Captures;
                    }
                }
                finally { _inputGate.Release(); }
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                batch = new(cancellationToken.IsCancellationRequested ? ObservationStreamStatus.Cancelled
                    : ObservationStreamStatus.DurationElapsed, captures, []);
            }
            yield return batch;
            if (batch.Status != ObservationStreamStatus.Observed) yield break;
        }
        yield return new(ObservationStreamStatus.CaptureLimitReached, captures, []);
    }

    private async Task<ReaderObservationBatch> CaptureObservationAsync(
        int captures, CancellationToken token, CancellationToken callerToken)
    {
        _command = ReaderCommand.ObserveChanges;
        _commandSequence++;
        _inputAttempted = false;
        _completedKeystrokes = 0;
        var before = _eventSequence;
        var status = ObservationStreamStatus.Observed;
        string? error = null;
        Record(ReaderEventKind.CommandStarted, "Observe changes");
        try
        {
            await RefreshAsync(token).ConfigureAwait(false);
            captures++;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            status = callerToken.IsCancellationRequested ? ObservationStreamStatus.Cancelled : ObservationStreamStatus.DurationElapsed;
            Record(ReaderEventKind.Cancelled, "Read-only observation interrupted.");
        }
        catch (BrowserOperationException exception)
        {
            status = ObservationStreamStatus.BrowserError;
            error = exception.Code;
            Record(ReaderEventKind.BrowserError, error);
            _snapshot = null;
            _cursorId = null;
        }
        Record(ReaderEventKind.CommandCompleted, status.ToString());
        if (status == ObservationStreamStatus.Observed && TimelineTruncated) status = ObservationStreamStatus.TimelineTruncated;
        return new(status, captures, Timeline.Where(item => item.Sequence > before).ToImmutableArray(), error);
    }
}
