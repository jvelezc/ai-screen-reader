using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli;

/// <summary>Composes the real reader with the task's existing independently authorised native input.</summary>
internal sealed class PublicTaskReaderController : IDisposable
{
    private readonly ObservingBrowser _browser;
    private readonly ScreenReaderSession _reader;
    private readonly TimeProvider _time;
    private readonly long _origin;
    internal AccessibilitySnapshot? Snapshot => _browser.Snapshot;
    internal PublicTaskFocus? Focus => _browser.Focus;
    internal PublicTaskFocus? BeforeFocus => _browser.BeforeFocus;
    internal bool InputAttempted => _browser.InputAttempted;
    internal bool InputCompleted => _browser.InputCompleted;
    internal PublicTaskReaderRecord? LastRecord { get; private set; }

    internal PublicTaskReaderController(IScreenReaderBrowser browser,
        Func<AccessibilitySnapshot, CancellationToken, Task<PublicTaskFocus?>> focus,
        TimeProvider time, long origin)
    {
        _time = time; _origin = origin;
        _browser = new(browser, focus);
        _reader = new(_browser, maxCommands: int.MaxValue, timeProvider: time);
    }

    internal static PublicTaskReaderController Create(PublicNavigationBrowser browser, PublicTaskGoal goal,
        Func<string, CancellationToken, Task> authorize, TimeProvider time, long origin) =>
        new(new TaskBrowser(browser, goal, authorize), browser.FocusFromSnapshotAsync, time, origin);

    internal async Task<PublicTaskReaderRecord> ExecuteAsync(string action, CancellationToken token)
    {
        var command = action switch
        {
            "Observe" => ReaderCommand.ReadCurrent,
            "Tab" => ReaderCommand.Tab,
            "Enter" => ReaderCommand.Activate,
            _ => throw new BrowserInputDeniedException("PUBLIC_TASK_READER_COMMAND_DENIED")
        };
        _browser.BeginCommand(); LastRecord = null;
        var started = _time.GetElapsedTime(_origin).TotalMilliseconds;
        var step = await _reader.ExecuteAsync(command, cancellationToken: token).ConfigureAwait(false);
        var announcement = step.Announcement;
        var length = Math.Min(announcement.Length, PublicTaskReaderRecord.MaximumAnnouncementCharacters);
        if (length < announcement.Length && length > 0 && char.IsHighSurrogate(announcement[length - 1]) && char.IsLowSurrogate(announcement[length])) length--;
        var snapshot = Snapshot;
        return LastRecord = new(PublicTaskReaderRecord.CurrentSchema, ScreenReaderSession.Profile, step.Sequence,
            step.Command.ToString(), step.Mode.ToString(), step.Outcome.ToString(), announcement[..length],
            PublicTaskRules.Hash(announcement), length != announcement.Length, step.CursorNodeId, step.FocusedNodeId,
            step.BrowserKeystrokes, step.InputMayHaveOccurred, step.Verification.ToString(), step.ErrorCode, step.BarrierCode,
            snapshot?.DocumentId, snapshot?.DocumentUrl,
            snapshot is null ? null : PublicTaskRules.Hash(JsonSerializer.SerializeToUtf8Bytes(snapshot, ReaderWorkerWire.Json)), snapshot?.CaptureEpoch,
            started, _time.GetElapsedTime(_origin).TotalMilliseconds);
    }

    public void Dispose() => _reader.Dispose();

    private sealed class TaskBrowser(PublicNavigationBrowser browser, PublicTaskGoal goal,
        Func<string, CancellationToken, Task> authorize) : IScreenReaderBrowser
    {
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default) => browser.CaptureAsync(cancellationToken);
        public async Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            if (key is not ("Tab" or "Enter")) throw new BrowserInputDeniedException("PUBLIC_TASK_KEY_DENIED");
            // Reader refresh happens before this grant. No earlier observation is input authority.
            try { await authorize(key, cancellationToken).ConfigureAwait(false); }
            catch (Exception) { throw new BrowserInputDeniedException("PUBLIC_TASK_AUTHORIZATION_UNCONFIRMED"); }
            await browser.PressAsync(key, goal, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class ObservingBrowser(IScreenReaderBrowser browser,
        Func<AccessibilitySnapshot, CancellationToken, Task<PublicTaskFocus?>> focus) : IScreenReaderBrowser
    {
        public AccessibilitySnapshot? Snapshot { get; private set; }
        public PublicTaskFocus? Focus { get; private set; }
        public PublicTaskFocus? BeforeFocus { get; private set; }
        public bool InputAttempted { get; private set; }
        public bool InputCompleted { get; private set; }
        private int _captures;
        public void BeginCommand() { _captures = 0; InputAttempted = false; InputCompleted = false; BeforeFocus = null; }
        public async Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            Snapshot = await browser.CaptureAsync(cancellationToken).ConfigureAwait(false);
            Focus = await focus(Snapshot, cancellationToken).ConfigureAwait(false);
            if (_captures++ == 0) BeforeFocus = Focus;
            return Snapshot;
        }
        public async Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            try
            {
                await browser.PressKeyAsync(key, cancellationToken).ConfigureAwait(false);
                InputAttempted = true; InputCompleted = true;
            }
            catch (BrowserInputDeniedException) { throw; }
            catch { InputAttempted = true; throw; }
        }
    }
}
