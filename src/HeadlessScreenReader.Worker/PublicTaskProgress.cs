using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Durable possible-input records precede acknowledgements. No grant can authorize another job, goal or action.</summary>
internal sealed class PublicTaskProgress(PublicTaskStart start, string root, Func<string, CancellationToken, Task<bool>> authorize)
{
    private int _sequence, _attempt, _tabs;
    private bool _open, _activated, _published;
    private PublicTaskGoal? _goal;
    internal string Phase { get; private set; } = "Starting";
    internal async Task HandleAsync(byte[] bytes, Stream input, CancellationToken token)
    {
        var intent = JsonSerializer.Deserialize<PublicTaskIntent>(bytes, ReaderWorkerWire.Json);
        void Require(bool condition) { if (!condition) throw new BrowserOperationException("PUBLIC_TASK_CONTROL_UNCONFIRMED"); }
        Require(intent is not null && intent.Start == start && intent.Sequence == _sequence + 1 && !_published);
        if (intent!.Goal is { } goal)
        {
            goal.Validate(start.Job.Source);
            Require(_goal is null || _goal == goal); _goal = goal;
        }
        else Require(_goal is null);
        switch (intent.Action)
        {
            case "Open": Require(!_open && intent.Attempt == _attempt + 1 && intent.Attempt <= 3); _attempt++; _open = true; _tabs = 0; _activated = false; break;
            case "Tab": Require(_open && _goal is not null && intent.Attempt == _attempt && _attempt <= 2 && !_activated && ++_tabs <= PublicTaskJob.MaximumTabs); break;
            case "Enter": Require(_open && _goal is not null && intent.Attempt == _attempt && _attempt <= 2 && !_activated); _activated = true; break;
            case "Pointer": Require(_open && _goal is not null && intent.Attempt == 3 && _attempt == 3 && !_activated); _activated = true; break;
            case "Close": Require(_open && intent.Attempt == _attempt); _open = false; break;
            case "Publish": Require(!_open && intent.Attempt == 0 && (_goal is null ? _attempt == 1 : _attempt == 3)); _published = true; break;
            default: throw new BrowserOperationException("PUBLIC_TASK_CONTROL_UNCONFIRMED");
        }
        Phase = intent.Action == "Publish" ? "Finalizing" : _attempt switch { 1 => "KeyboardAttempt1", 2 => "KeyboardAttempt2", _ => "PointerComparison" };
        if (!await authorize(Phase, token).ConfigureAwait(false)) throw new BrowserOperationException("PUBLIC_TASK_AUTHORITY_LOST");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, $"{start.ExecutionId:N}-{intent.Sequence:0000}.json");
        await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        { await file.WriteAsync(bytes, token).ConfigureAwait(false); await file.FlushAsync(token).ConfigureAwait(false); file.Flush(true); }
        if (!await authorize(Phase, token).ConfigureAwait(false)) throw new BrowserOperationException("PUBLIC_TASK_AUTHORITY_LOST");
        var acknowledgement = new PublicTaskAck(start.ExecutionId, intent.Sequence, PublicTaskRules.Hash(bytes));
        await ReaderWorkerWire.WriteAsync(input, JsonSerializer.SerializeToUtf8Bytes(acknowledgement, ReaderWorkerWire.Json), 4096, token).ConfigureAwait(false);
        _sequence++;
    }
    internal bool Published => _published;
    internal void Validate(PublicTaskReport report)
    {
        if (!_published || _open || report.Start != start || report.Goal != _goal || report.Attempts.Length != (_goal is null ? 0 : 3))
            throw new BrowserOperationException("PUBLIC_TASK_CONTROL_INCOMPLETE");
    }
}
