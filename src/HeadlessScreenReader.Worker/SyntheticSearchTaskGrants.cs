using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public sealed record SyntheticSearchTaskGrant(SyntheticSearchTaskIntent Intent, string IntentSha256,
    DateTimeOffset PreparedAtUtc, SyntheticSearchInputDecision DecisionBeforeWrite, bool InputMayHaveOccurred);

/// <summary>Append-only permission preparation. Neither a durable intent nor a response proves input completion.</summary>
public sealed partial class SyntheticSearchTaskGrants
{
    private readonly SyntheticSearchTaskStart _start;
    private readonly string _directory;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly Dictionary<int, string> _documents = [];
    private readonly Dictionary<int, string> _fields = [];
    private int _sequence;
    private bool _stopped;
    private int _currentTrial;

    private SyntheticSearchTaskGrants(SyntheticSearchTaskStart start, string directory, TimeProvider time)
    { _start = start; _directory = directory; _time = time; }

    public string DirectoryPath => _directory;
    internal SyntheticSearchTaskStart Start => _start;
    public int PreparedResponses => _sequence;
    public bool CanAdvance => !_stopped && _sequence < SyntheticSearchTaskControl.Boundaries.Length;
    internal void Stop() => _stopped = true;

    public static async Task<SyntheticSearchTaskGrants> BeginAsync(string root, SyntheticSearchTaskStart start,
        CancellationToken token = default, TimeProvider? time = null)
    {
        start.Validate();
        token.ThrowIfCancellationRequested();
        root = Path.GetFullPath(root);
        Directory.CreateDirectory(root);
        RegularDirectory(root);
        // The job fence deliberately excludes execution/worker identity: a new worker cannot replay generation one.
        var directory = Path.Combine(root, $"job-{start.Job.OrganizationId:N}-{start.Job.ScanId:N}-{start.Job.JobId:N}-1");
        Directory.CreateDirectory(directory);
        RegularDirectory(directory);
        try
        {
            await WriteNewAsync(Path.Combine(directory, "start.json"), JsonSerializer.SerializeToUtf8Bytes(start, ReaderWorkerWire.Json), token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { throw new BrowserOperationException("SEARCH_TASK_REPLAY_OR_FENCE_UNCONFIRMED"); }
        return new(start, directory, time ?? TimeProvider.System);
    }

    public async Task<SyntheticSearchTaskAck> PrepareAsync(SyntheticSearchTaskIntent intent,
        Func<CancellationToken, Task<bool>> authorize, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(authorize);
        await _serial.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_stopped) throw new BrowserOperationException("SEARCH_TASK_GRANTS_STOPPED");
            token.ThrowIfCancellationRequested();
            SyntheticSearchTaskControl.Validate(intent, _start, _sequence + 1);
            ValidateScope(intent);
            var payload = JsonSerializer.SerializeToUtf8Bytes(intent, ReaderWorkerWire.Json);
            if (payload.Length > SyntheticSearchTaskControl.MaximumFrameBytes)
                throw new BrowserOperationException("SEARCH_TASK_INTENT_INVALID");
            var digest = TraversalArchive.Hash(payload);
            var decision = await DecideAsync(authorize, token).ConfigureAwait(false);
            var prepared = new SyntheticSearchTaskGrant(intent, digest, _time.GetUtcNow(), decision,
                intent.Input is not null && decision == SyntheticSearchInputDecision.Granted);
            RegularDirectory(_directory);
            await WriteNewAsync(Path.Combine(_directory, $"intent-{intent.Sequence:D4}.json"),
                JsonSerializer.SerializeToUtf8Bytes(prepared, ReaderWorkerWire.Json), token).ConfigureAwait(false);
            // A successful write must not outlive current authority. Never rewrite the prepared original.
            token.ThrowIfCancellationRequested();
            if (decision == SyntheticSearchInputDecision.Granted)
                decision = await DecideAsync(authorize, token).ConfigureAwait(false);
            var acknowledgement = new SyntheticSearchTaskAck(SyntheticSearchTaskControl.Schema, _start.ExecutionId, intent.Sequence, digest, decision);
            await WriteNewAsync(Path.Combine(_directory, $"response-{intent.Sequence:D4}.json"),
                JsonSerializer.SerializeToUtf8Bytes(acknowledgement, ReaderWorkerWire.Json), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            _sequence = intent.Sequence;
            if (decision != SyntheticSearchInputDecision.Granted) _stopped = true;
            else CommitScope(intent);
            return acknowledgement;
        }
        catch (OperationCanceledException) { _stopped = true; throw; }
        catch (BrowserOperationException) { _stopped = true; throw; }
        catch (BrowserInputDeniedException) { _stopped = true; throw; }
        catch (Exception)
        {
            _stopped = true;
            throw new BrowserOperationException("SEARCH_TASK_GRANT_WRITE_UNCONFIRMED");
        }
        finally { _serial.Release(); }
    }

    public void RequireComplete()
    {
        if (_stopped || _sequence != 85) throw new BrowserOperationException("SEARCH_TASK_GRANTS_INCOMPLETE");
    }

    private void ValidateScope(SyntheticSearchTaskIntent intent)
    {
        if (intent.Input is not { } input) return;
        var trial = intent.Boundary.Trial;
        if (_documents.TryGetValue(trial, out var document) ? document != input.DocumentId
            : _documents.Values.Contains(input.DocumentId, StringComparer.Ordinal))
            throw new BrowserOperationException("SEARCH_TASK_DOCUMENT_CHANGED_OR_REUSED");
        if (input.Kind == SyntheticSearchInputKind.Character && _fields.TryGetValue(trial, out var field) && input.NodeId != field)
            throw new BrowserOperationException("SEARCH_TASK_FIELD_CHANGED");
        if (input.NativeSequence > 14 && _fields.TryGetValue(trial, out field))
        {
            var requiresField = trial <= 2 || input.NativeKey == "Tab";
            if (requiresField ? input.NodeId != field : input.NodeId is null || input.NodeId == field)
                throw new BrowserOperationException("SEARCH_TASK_ACTIVATION_SCOPE_INVALID");
        }
    }

    private void CommitScope(SyntheticSearchTaskIntent intent)
    {
        if (intent.Boundary.Action == "Open") _currentTrial = intent.Boundary.Trial;
        if (intent.Input is not { } input) return;
        _documents.TryAdd(intent.Boundary.Trial, input.DocumentId);
        if (input.Kind is SyntheticSearchInputKind.Character or SyntheticSearchInputKind.MouseFocus)
            _fields.TryAdd(intent.Boundary.Trial, input.NodeId!);
    }

    private static async Task<SyntheticSearchInputDecision> DecideAsync(Func<CancellationToken, Task<bool>> authorize, CancellationToken token)
    {
        try { return await authorize(token).ConfigureAwait(false) ? SyntheticSearchInputDecision.Granted : SyntheticSearchInputDecision.Revoked; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return SyntheticSearchInputDecision.Unconfirmed; }
    }

    private static void RegularDirectory(string path)
    {
        var info = new DirectoryInfo(path);
        if (!info.Exists || info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new BrowserOperationException("SEARCH_TASK_GRANT_PATH_INVALID");
    }

    private static async Task WriteNewAsync(string path, byte[] bytes, CancellationToken token)
    {
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await file.WriteAsync(bytes, token).ConfigureAwait(false);
        await file.FlushAsync(token).ConfigureAwait(false);
        file.Flush(flushToDisk: true);
    }
}
