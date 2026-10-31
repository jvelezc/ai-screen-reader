using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;
using HeadlessScreenReader.Cli;

namespace HeadlessScreenReader.Tests;

public sealed class FixtureRecordingFailureTests
{
    private static FixtureProgressStart Start(bool sources = true) => new(FixtureProgressProtocol.Schema,
        FixtureWorkerTests.Job() with { RecordObservation = true, TraversalRoute = 1 }, Guid.NewGuid())
    { RetainSourceFrames = sources };
    private static FixtureRecordingFailure Failure(FixtureProgressStart start, int checkpoints = 3, int sources = 2) =>
        new(FixtureRecordingFailureProtocol.Schema, start.ExecutionId, checkpoints, sources, FixtureRecordingFailureReason.CaptureBudgetExceeded);

    [Fact]
    public void InternalCollectorCauseCannotBeSerializedIntoTraversalReports()
    {
        var result = new HeadlessScreenReader.Cli.RecordedTraversalResult(null,
            new("FinalizeAndDecodeOriginalMedia", "CAPTURE_STOP_UNCONFIRMED", null, [], null, null), null)
        { CollectorFailureCode = "CAPTURE_FRAME_LIMIT" };
        var json = JsonSerializer.Serialize(result);
        Assert.Contains("CAPTURE_STOP_UNCONFIRMED", json, StringComparison.Ordinal);
        Assert.DoesNotContain("CollectorFailureCode", json, StringComparison.Ordinal);
        Assert.DoesNotContain("CAPTURE_FRAME_LIMIT", json, StringComparison.Ordinal);
        Assert.Null(result.Receipt); Assert.Null(result.Recording.Media); Assert.Null(result.Recording.Alignment);
    }

    [Theory]
    [InlineData("CAPTURE_FRAME_LIMIT", FixtureRecordingFailureReason.CaptureBudgetExceeded)]
    [InlineData("CAPTURE_FRAME_INVALID", FixtureRecordingFailureReason.CaptureFrameInvalid)]
    [InlineData("CAPTURE_FRAME_WRITE_FAILED", FixtureRecordingFailureReason.CaptureWriteFailed)]
    [InlineData("TRAVERSAL_RECORDING_FAILED", FixtureRecordingFailureReason.RecordingFailed)]
    [InlineData("TRAVERSAL_RECORDING_CANCELLED", FixtureRecordingFailureReason.Cancelled)]
    [InlineData("RECORDING_SOURCE_METADATA_MISSING", FixtureRecordingFailureReason.SourceMetadataMissing)]
    [InlineData("RECORDING_SOURCE_HASH_CHANGED", FixtureRecordingFailureReason.SourceHashChanged)]
    [InlineData("RECORDING_SOURCE_ARTIFACT_MISSING", FixtureRecordingFailureReason.SourceArtifactMissing)]
    [InlineData("RECORDING_ARTIFACT_UNRECOGNIZED", FixtureRecordingFailureReason.UnrecognizedArtifact)]
    [InlineData("WORKER_KEYBOARD_ORIGINAL_CHANGED", FixtureRecordingFailureReason.OriginalChanged)]
    [InlineData("WORKER_KEYBOARD_RECORDING_UNCONFIRMED", FixtureRecordingFailureReason.Unconfirmed)]
    [InlineData("WORKER_PROGRESS_INVALID", FixtureRecordingFailureReason.AcknowledgementRejected)]
    [InlineData("WORKER_PROGRESS_MEDIA_INVALID", FixtureRecordingFailureReason.AcknowledgementRejected)]
    [InlineData("WORKER_PROGRESS_MEDIA_EXPORT_UNCONFIRMED", FixtureRecordingFailureReason.SourceExportUnconfirmed)]
    public void KnownFaultCarriesOnlyBoundExecutionAndClosedReasonWithoutExceptionData(string code, FixtureRecordingFailureReason reason)
    {
        var start = Start(); var original = new BrowserOperationException(code); original.Data["Private"] = "private-browser-detail";
        var failure = Assert.IsType<FixtureRecordingFailure>(FixtureRecordingFailureProtocol.FromException(start, 3, 2, original));
        var bytes = FixtureProgressProtocol.Encode(failure);
        Assert.True(FixtureRecordingFailureProtocol.IsFailure(bytes));
        Assert.Equal(start.ExecutionId, failure.ExecutionId); Assert.Equal(3, failure.CheckpointSequence); Assert.Equal(2, failure.SourceSequence);
        Assert.Equal(reason, failure.Reason); Assert.Equal(failure, FixtureRecordingFailureProtocol.Read(bytes, start, 3, 2));
        Assert.DoesNotContain("private-browser-detail", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        using var document = JsonDocument.Parse(bytes);
        Assert.Equal(["Schema", "ExecutionId", "CheckpointSequence", "SourceSequence", "Reason"], document.RootElement.EnumerateObject().Select(p => p.Name));
        var publicCode = FixtureRecordingFailureProtocol.ErrorCode(reason);
        Assert.Matches("^[A-Z_]{3,64}$", publicCode); Assert.True(FixtureRecordingFailureProtocol.IsPublicCode(publicCode));
        Assert.False(FixtureRecordingFailureProtocol.IsPublicCode(publicCode + "_PRIVATE"));
    }

    [Theory]
    [InlineData("CAPTURE_STOP_UNCONFIRMED", "CAPTURE_FRAME_LIMIT", FixtureRecordingFailureReason.CaptureBudgetExceeded)]
    [InlineData("CAPTURE_STOP_UNCONFIRMED", "CAPTURE_FRAME_INVALID", FixtureRecordingFailureReason.CaptureFrameInvalid)]
    [InlineData("CAPTURE_STOP_UNCONFIRMED", "CAPTURE_FRAME_WRITE_FAILED", FixtureRecordingFailureReason.CaptureWriteFailed)]
    [InlineData("CAPTURE_STOP_UNCONFIRMED", "WORKER_PROGRESS_MEDIA_EXPORT_UNCONFIRMED", FixtureRecordingFailureReason.SourceExportUnconfirmed)]
    [InlineData("CHROMIUM_COLLECTOR_STOP_UNCONFIRMED", "CAPTURE_FRAME_LIMIT", FixtureRecordingFailureReason.CaptureBudgetExceeded)]
    [InlineData("CHROMIUM_COLLECTOR_STOP_UNCONFIRMED", "CAPTURE_FRAME_INVALID", FixtureRecordingFailureReason.CaptureFrameInvalid)]
    [InlineData("CHROMIUM_COLLECTOR_STOP_UNCONFIRMED", "CAPTURE_FRAME_WRITE_FAILED", FixtureRecordingFailureReason.CaptureWriteFailed)]
    [InlineData("CHROMIUM_COLLECTOR_STOP_UNCONFIRMED", "WORKER_PROGRESS_MEDIA_EXPORT_UNCONFIRMED", FixtureRecordingFailureReason.SourceExportUnconfirmed)]
    public void KnownCollectorStopCauseMapsWithoutLosingUncertaintyOrDisclosingDetails(string outer, string code, FixtureRecordingFailureReason reason)
    {
        var start = Start(); var cause = new BrowserOperationException(code, new IOException("private-path-and-payload"));
        cause.Data["Private"] = "private-path-and-payload";
        var failure = new BrowserOperationException(outer, cause);
        var diagnostic = FixtureRecordingFailureProtocol.FromException(start, 3, 2, failure);
        Assert.Equal(reason, diagnostic.Reason);
        var bytes = FixtureProgressProtocol.Encode(diagnostic);
        Assert.Equal(diagnostic, FixtureRecordingFailureProtocol.Read(bytes, start, 3, 2));
        Assert.DoesNotContain("private-path-and-payload", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        using var document = JsonDocument.Parse(bytes);
        Assert.Equal(["Schema", "ExecutionId", "CheckpointSequence", "SourceSequence", "Reason"],
            document.RootElement.EnumerateObject().Select(p => p.Name));
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("deep")]
    [InlineData("private")]
    [InlineData("raw")]
    [InlineData("not-collector")]
    [InlineData("wrong-shutdown")]
    [InlineData("missing")]
    public void ForeignDeepUnknownOrRawStopCausesRemainUnconfirmed(string kind)
    {
        var known = new BrowserOperationException("CAPTURE_FRAME_LIMIT");
        var failure = kind switch
        {
            "foreign" => new BrowserOperationException("PRIVATE_BROWSER_DETAIL", known),
            "deep" => new BrowserOperationException("CAPTURE_STOP_UNCONFIRMED", new BrowserOperationException("CAPTURE_STOP_UNCONFIRMED", known)),
            "private" => new BrowserOperationException("CAPTURE_STOP_UNCONFIRMED", new BrowserOperationException("CAPTURE_FRAME_LIMIT_PRIVATE")),
            "raw" => new BrowserOperationException("CAPTURE_STOP_UNCONFIRMED", new IOException("private-path-and-payload")),
            "wrong-shutdown" => new BrowserOperationException("CHROMIUM_BROWSER_CLOSE_UNCONFIRMED", known),
            "missing" => new BrowserOperationException("CAPTURE_STOP_UNCONFIRMED"),
            _ => new BrowserOperationException("CAPTURE_STOP_UNCONFIRMED", new BrowserOperationException("WORKER_PROGRESS_INVALID"))
        };
        var start = Start(); var diagnostic = FixtureRecordingFailureProtocol.FromException(start, 0, 0, failure);
        Assert.Equal(FixtureRecordingFailureReason.Unconfirmed, diagnostic.Reason);
        var bytes = FixtureProgressProtocol.Encode(diagnostic);
        Assert.Equal(diagnostic, FixtureRecordingFailureProtocol.Read(bytes, start, 0, 0));
        Assert.DoesNotContain("PRIVATE", Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-path-and-payload", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("browser")]
    [InlineData("io")]
    [InlineData("cancelled")]
    [InlineData("other")]
    public void UnknownOrRawExceptionsCanReleaseOnlyBoundUnconfirmedStatusWithoutDetails(string kind)
    {
        Exception failure = kind switch
        {
            "browser" => new BrowserOperationException("PRIVATE_BROWSER_DETAIL"),
            "io" => new IOException("private-browser-detail"),
            "cancelled" => new OperationCanceledException("private-browser-detail"),
            _ => new InvalidOperationException("private-browser-detail")
        };
        var start = Start(); var diagnostic = FixtureRecordingFailureProtocol.FromException(start, 0, 0, failure);
        Assert.Equal(FixtureRecordingFailureReason.Unconfirmed, diagnostic.Reason); Assert.Equal(start.ExecutionId, diagnostic.ExecutionId);
        var bytes = FixtureProgressProtocol.Encode(diagnostic);
        Assert.DoesNotContain("private-browser-detail", Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PRIVATE_BROWSER_DETAIL", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        Assert.Equal(diagnostic, FixtureRecordingFailureProtocol.Read(bytes, start, 0, 0));
        Assert.False(FixtureRecordingFailureProtocol.IsPublicCode("WORKER_PROGRESS_RECORDING_PRIVATE_BROWSER_DETAIL"));
    }

    [Theory]
    [InlineData(FixtureRecordingBoundary.Collection, FixtureRecordingFailureReason.CollectionUnconfirmed)]
    [InlineData(FixtureRecordingBoundary.Calibration, FixtureRecordingFailureReason.CalibrationUnconfirmed)]
    [InlineData(FixtureRecordingBoundary.Traversal, FixtureRecordingFailureReason.TraversalUnconfirmed)]
    [InlineData(FixtureRecordingBoundary.Finalization, FixtureRecordingFailureReason.FinalizationUnconfirmed)]
    [InlineData(FixtureRecordingBoundary.Alignment, FixtureRecordingFailureReason.AlignmentUnconfirmed)]
    [InlineData(FixtureRecordingBoundary.Preparation, FixtureRecordingFailureReason.PreparationUnconfirmed)]
    [InlineData(FixtureRecordingBoundary.Validation, FixtureRecordingFailureReason.ValidationUnconfirmed)]
    [InlineData(FixtureRecordingBoundary.Cleanup, FixtureRecordingFailureReason.CleanupUnconfirmed)]
    [InlineData(FixtureRecordingBoundary.OutputTransfer, FixtureRecordingFailureReason.OutputTransferUnconfirmed)]
    public void TrustedRecordingBoundaryRetainsOnlyUncertaintyAndTheOriginalFrameScope(
        FixtureRecordingBoundary boundary, FixtureRecordingFailureReason reason)
    {
        var start = Start(); var error = new IOException("private-path-and-payload", new Exception("private-inner"));
        error.Data["Boundary"] = "private-page-boundary";
        var failure = FixtureRecordingFailureProtocol.FromException(start, 3, 2, error, boundary);
        Assert.Equal(reason, failure.Reason); Assert.Equal(start.ExecutionId, failure.ExecutionId);
        Assert.Equal(3, failure.CheckpointSequence); Assert.Equal(2, failure.SourceSequence);
        var bytes = FixtureProgressProtocol.Encode(failure);
        Assert.InRange(bytes.Length, 1, FixtureRecordingFailureProtocol.MaximumBytes);
        Assert.Equal(failure, FixtureRecordingFailureProtocol.Read(bytes, start, 3, 2));
        Assert.DoesNotContain("private", Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal(["Schema", "ExecutionId", "CheckpointSequence", "SourceSequence", "Reason"], json.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.EndsWith("_UNCONFIRMED", FixtureRecordingFailureProtocol.ErrorCode(reason), StringComparison.Ordinal);
        Assert.True(FixtureRecordingFailureProtocol.IsPublicCode(FixtureRecordingFailureProtocol.ErrorCode(reason)));
    }

    [Theory]
    [InlineData("CAPTURE_STOP_UNCONFIRMED", FixtureRecordingFailureReason.CaptureStopUnconfirmed)]
    [InlineData("CHROMIUM_COLLECTOR_STOP_UNCONFIRMED", FixtureRecordingFailureReason.BrowserShutdownUnconfirmed)]
    [InlineData("CHROMIUM_BROWSER_CLOSE_UNCONFIRMED", FixtureRecordingFailureReason.BrowserShutdownUnconfirmed)]
    [InlineData("CHROMIUM_PROCESS_EXIT_UNCONFIRMED", FixtureRecordingFailureReason.BrowserShutdownUnconfirmed)]
    [InlineData("CHROMIUM_SHUTDOWN_UNCONFIRMED", FixtureRecordingFailureReason.BrowserShutdownUnconfirmed)]
    [InlineData("CHROMIUM_OWNED_ROOT_STOP_UNCONFIRMED", FixtureRecordingFailureReason.BrowserShutdownUnconfirmed)]
    [InlineData("CHROMIUM_TRANSPORT_CLOSE_UNCONFIRMED", FixtureRecordingFailureReason.BrowserShutdownUnconfirmed)]
    [InlineData("CHROMIUM_PROCESS_WATCH_RELEASE_UNCONFIRMED", FixtureRecordingFailureReason.BrowserShutdownUnconfirmed)]
    [InlineData("CHROMIUM_BROWSER_CLOSE_UNCONFIRMED_PRIVATE", FixtureRecordingFailureReason.FinalizationUnconfirmed)]
    [InlineData("CAPTURE_STOP_UNCONFIRMED_PRIVATE", FixtureRecordingFailureReason.FinalizationUnconfirmed)]
    public void OnlyExactKnownStopCodesRefineTheTrustedBoundary(string code, FixtureRecordingFailureReason reason)
    {
        var start = Start(); var error = new BrowserOperationException(code, new IOException("private-path-and-payload"));
        var failure = FixtureRecordingFailureProtocol.FromException(start, 3, 2, error, FixtureRecordingBoundary.Finalization);
        Assert.Equal(reason, failure.Reason);
        Assert.Equal(FixtureRecordingFailureReason.Unconfirmed, FixtureRecordingFailureProtocol.FromException(start, 3, 2, error).Reason);
        var bytes = FixtureProgressProtocol.Encode(failure);
        Assert.Equal(failure, FixtureRecordingFailureProtocol.Read(bytes, start, 3, 2));
        Assert.DoesNotContain("PRIVATE", Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(FixtureRecordingBoundary.Unknown)]
    [InlineData((FixtureRecordingBoundary)(-1))]
    [InlineData((FixtureRecordingBoundary)999)]
    public void ExceptionTextAndDataCannotSupplyAnUnknownOrInvalidRecordingBoundary(FixtureRecordingBoundary boundary)
    {
        var error = new BrowserOperationException("CAPTURE_STOP_UNCONFIRMED"); error.Data["Boundary"] = FixtureRecordingBoundary.Cleanup;
        var failure = FixtureRecordingFailureProtocol.FromException(Start(), 0, 0, error, boundary);
        Assert.Equal(FixtureRecordingFailureReason.Unconfirmed, failure.Reason);
    }

    [Theory]
    [InlineData(FixtureRecordingBoundary.Collection)]
    [InlineData(FixtureRecordingBoundary.Finalization)]
    [InlineData(FixtureRecordingBoundary.Validation)]
    [InlineData(FixtureRecordingBoundary.Cleanup)]
    public void SpecificCollectorFailureWinsOverATrustedBoundaryWithoutClaimingStop(FixtureRecordingBoundary boundary)
    {
        var error = new BrowserOperationException("CAPTURE_STOP_UNCONFIRMED", new BrowserOperationException("CAPTURE_FRAME_LIMIT"));
        var failure = FixtureRecordingFailureProtocol.FromException(Start(), 3, 2, error, boundary);
        Assert.Equal(FixtureRecordingFailureReason.CaptureBudgetExceeded, failure.Reason);
    }

    [Theory]
    [InlineData(FixtureRecordingBoundary.Collection, FixtureRecordingFailureReason.CollectionUnconfirmed)]
    [InlineData(FixtureRecordingBoundary.Finalization, FixtureRecordingFailureReason.FinalizationUnconfirmed)]
    [InlineData(FixtureRecordingBoundary.Preparation, FixtureRecordingFailureReason.PreparationUnconfirmed)]
    public void GenericRecordingUncertaintyCannotOverrideTheActualTrustedBoundary(
        FixtureRecordingBoundary boundary, FixtureRecordingFailureReason reason)
    {
        var error = new BrowserOperationException("WORKER_KEYBOARD_RECORDING_UNCONFIRMED");
        var failure = FixtureRecordingFailureProtocol.FromException(Start(), 3, 2, error, boundary);
        Assert.Equal(reason, failure.Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(64)]
    [InlineData(-1)]
    public async Task ActualOutputTransferFailureCannotMasqueradeAsValidationOrAcknowledgeCompletion(int acceptedPrefix)
    {
        var start = Start(false); using var input = new MemoryStream(); using var progress = new MemoryStream(); using var output = new FailingRecordingOutput(acceptedPrefix);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await ReaderWorkerWire.WriteAsync(input, FixtureProgressProtocol.Encode(start), 4096, timeout.Token);
        for (var sequence = 1; sequence <= 5; sequence++)
            await ReaderWorkerWire.WriteAsync(input, FixtureProgressProtocol.Encode(new FixtureProgressAck(FixtureProgressProtocol.Schema,
                start.ExecutionId, sequence)), 4096, timeout.Token);
        input.Position = 0;
        Assert.Equal(3, await FixtureProgressWorker.RunAsync(input, output, progress).WaitAsync(timeout.Token));
        Assert.Equal(1, output.WriteCalls);
        if (acceptedPrefix >= 0) Assert.Equal(acceptedPrefix, output.Length);
        else
        {
            Assert.True(output.Length > 64);
            using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(output.AcceptedBytes));
            Assert.Equal(4, zip.Entries.Count);
        }
        progress.Position = 0;
        FixtureProgressMessage? previous = null;
        for (var sequence = 1; sequence <= 5; sequence++)
        {
            var checkpoint = await FixtureProgressProtocol.ReadAsync<FixtureProgressMessage>(progress, FixtureProgressProtocol.MaximumFrameBytes, timeout.Token);
            FixtureProgressProtocol.Validate(checkpoint, start, previous); Assert.Equal(sequence, checkpoint.Sequence); previous = checkpoint;
        }
        var bytes = Assert.IsType<byte[]>(await ReaderWorkerWire.ReadAsync(progress, FixtureRecordingFailureProtocol.MaximumBytes, timeout.Token));
        var failure = FixtureRecordingFailureProtocol.Read(bytes, start, 5, 0);
        Assert.Equal(FixtureRecordingFailureReason.OutputTransferUnconfirmed, failure.Reason);
        Assert.DoesNotContain("private-output-payload", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        Assert.Null(await ReaderWorkerWire.ReadAsync(progress, FixtureProgressProtocol.MaximumFrameBytes, timeout.Token));
    }

    private sealed class FailingRecordingOutput(int acceptedPrefix) : Stream
    {
        private readonly MemoryStream _accepted = new();
        public int WriteCalls { get; private set; }
        public byte[] AcceptedBytes => _accepted.ToArray();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _accepted.Length;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        private void Accept(ReadOnlySpan<byte> buffer)
        { WriteCalls++; _accepted.Write(buffer[..(acceptedPrefix < 0 ? buffer.Length : Math.Min(acceptedPrefix, buffer.Length))]); }
        public override void Write(byte[] buffer, int offset, int count)
        { Accept(buffer.AsSpan(offset, count)); throw new IOException("private-output-payload"); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        { Accept(buffer.Span); return ValueTask.FromException(new IOException("private-output-payload")); }
        protected override void Dispose(bool disposing) { if (disposing) _accepted.Dispose(); base.Dispose(disposing); }
    }

    [Fact]
    public async Task ActualUnknownCheckpointFailureRetainsTheTrustedTraversalBoundaryAfterCleanup()
    {
        var start = Start(); var boundary = FixtureRecordingBoundary.Unknown;
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => KeyboardFixtureWorker.RecordAsync(start.Job, default,
            (_, _) => Task.FromException(new BrowserOperationException("PRIVATE_RECORDING_DETAIL")), null, value => boundary = value));
        Assert.Equal("PRIVATE_RECORDING_DETAIL", error.Code); Assert.Equal(FixtureRecordingBoundary.Traversal, boundary);
        var failure = FixtureRecordingFailureProtocol.FromException(start, 0, 0, error, boundary);
        Assert.Equal(FixtureRecordingFailureReason.TraversalUnconfirmed, failure.Reason);
        Assert.DoesNotContain("PRIVATE_RECORDING_DETAIL", Encoding.UTF8.GetString(FixtureProgressProtocol.Encode(failure)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActualSuccessfulKeyboardRecordingKeepsItsBoundaryTraceOutOfTheEvidence()
    {
        var job = Start().Job with { TraversalRoute = 2 }; var boundaries = new List<FixtureRecordingBoundary>();
        var bytes = await KeyboardFixtureWorker.RecordAsync(job, default, null, null, boundaries.Add);
        Assert.Equal([FixtureRecordingBoundary.Collection, FixtureRecordingBoundary.Preparation, FixtureRecordingBoundary.Validation], boundaries);
        var recording = KeyboardRecordingBundle.Decode(bytes, job).Validate(job);
        Assert.Equal("Complete", recording.Stage); Assert.Null(recording.ErrorCode); Assert.Equal(CapturePhase.Closed, recording.Capture.Phase);
        Assert.Equal(TraversalReachability.NotReached, recording.Receipt.Reachability); Assert.Equal(TraversalBarrier.Observed, recording.Receipt.Barrier);
        Assert.Equal(0, recording.Receipt.AttemptedKeystrokes); Assert.Equal(0, recording.Receipt.CompletedKeystrokes);
        Assert.NotEmpty(recording.SourceFiles); Assert.Equal(recording.SourceFiles.Length, recording.SourceFrames.Length);
        using var json = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(recording, ObservationStore.Json));
        Assert.False(json.RootElement.TryGetProperty("Boundary", out _)); Assert.False(json.RootElement.TryGetProperty("observeBoundary", out _));
    }

    [Fact]
    public async Task ActualSuccessfulCleanupCannotReplaceThePriorFailedRecordingBoundary()
    {
        var job = Start().Job with { TraversalRoute = 2 }; var boundary = FixtureRecordingBoundary.Unknown;
        var error = await Assert.ThrowsAsync<IOException>(() => KeyboardFixtureWorker.RecordAsync(job, default, null, null, value =>
        {
            boundary = value;
            if (value == FixtureRecordingBoundary.Preparation) throw new IOException("private-path-and-payload");
        }));
        Assert.Equal(FixtureRecordingBoundary.Preparation, boundary);
        var failure = FixtureRecordingFailureProtocol.FromException(Start(), 0, 0, error, boundary);
        Assert.Equal(FixtureRecordingFailureReason.PreparationUnconfirmed, failure.Reason);
        Assert.DoesNotContain("private-path-and-payload", Encoding.UTF8.GetString(FixtureProgressProtocol.Encode(failure)), StringComparison.Ordinal);
    }

    [Fact]
    public void CleanupScopeFailurePreservesForeignFilesAndReportsOnlyCleanupUncertainty()
    {
        var folder = Path.Combine(Path.GetTempPath(), "hsr-boundary-foreign-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); var file = Path.Combine(folder, "sentinel.txt"); File.WriteAllText(file, "owned-test-data");
        var boundary = FixtureRecordingBoundary.Preparation;
        try
        {
            var error = Assert.Throws<IOException>(() => KeyboardFixtureWorker.CleanupRecordingFolder(folder, value => boundary = value));
            Assert.Equal("WORKER_KEYBOARD_CLEANUP_SCOPE_INVALID", error.Message);
            Assert.Equal("owned-test-data", File.ReadAllText(file)); Assert.Equal(FixtureRecordingBoundary.Cleanup, boundary);
            var failure = FixtureRecordingFailureProtocol.FromException(Start(), 0, 0, error, boundary);
            Assert.Equal(FixtureRecordingFailureReason.CleanupUnconfirmed, failure.Reason);
        }
        finally { File.Delete(file); Directory.Delete(folder); }
    }

    [Fact]
    public void SuccessfulOwnedFolderCleanupCannotInventAFailureBoundary()
    {
        var folder = Path.Combine(Path.GetTempPath(), "hsr-keyboard-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "sentinel.txt"), "owned-test-data"); var boundary = FixtureRecordingBoundary.Validation;
        KeyboardFixtureWorker.CleanupRecordingFolder(folder, value => boundary = value);
        Assert.False(Directory.Exists(folder)); Assert.Equal(FixtureRecordingBoundary.Validation, boundary);
    }

    [Theory]
    [InlineData("schema", false)]
    [InlineData("execution", false)]
    [InlineData("empty-execution", false)]
    [InlineData("checkpoint", false)]
    [InlineData("source", false)]
    [InlineData("unknown", true)]
    [InlineData("missing", true)]
    [InlineData("null", true)]
    [InlineData("enum", true)]
    [InlineData("numeric-enum", true)]
    [InlineData("quoted-checkpoint", true)]
    [InlineData("duplicate", true)]
    public void ForgedMalformedOrUnboundFailureCannotReleaseAnApprovedReason(string change, bool jsonError)
    {
        var start = Start(); var bytes = FixtureProgressProtocol.Encode(Failure(start));
        var node = JsonNode.Parse(bytes)!.AsObject();
        switch (change)
        {
            case "schema": node["Schema"] = "unknown"; break;
            case "execution": node["ExecutionId"] = Guid.NewGuid(); break;
            case "empty-execution": node["ExecutionId"] = Guid.Empty; break;
            case "checkpoint": node["CheckpointSequence"] = 2; break;
            case "source": node["SourceSequence"] = 1; break;
            case "unknown": node["PrivateDetail"] = "private-browser-detail"; break;
            case "missing": node.Remove("Reason"); break;
            case "null": node["Reason"] = null; break;
            case "enum": node["Reason"] = "Success"; break;
            case "numeric-enum": node["Reason"] = 1; break;
            case "quoted-checkpoint": node["CheckpointSequence"] = "3"; break;
        }
        bytes = Encoding.UTF8.GetBytes(node.ToJsonString());
        if (change == "duplicate") bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes)[..^1] + ",\"Reason\":\"CaptureBudgetExceeded\"}");
        var error = Record.Exception(() => FixtureRecordingFailureProtocol.Read(bytes, start, 3, 2));
        if (jsonError) Assert.IsType<JsonException>(error);
        else Assert.Equal("WORKER_PROGRESS_RECORDING_FAILURE_INVALID", Assert.IsType<BrowserOperationException>(error).Code);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(5, 250, true)]
    [InlineData(-1, 0, false)]
    [InlineData(6, 0, false)]
    [InlineData(0, -1, false)]
    [InlineData(0, 251, false)]
    public void FailureCountersRespectActualPlanAndAdmittedSourceBounds(int checkpoints, int sources, bool valid)
    {
        var start = Start(); var failure = Failure(start, checkpoints, sources); var bytes = FixtureProgressProtocol.Encode(failure);
        if (valid) Assert.Equal(failure, FixtureRecordingFailureProtocol.Read(bytes, start, checkpoints, sources));
        else Assert.Equal("WORKER_PROGRESS_RECORDING_FAILURE_INVALID", Assert.Throws<BrowserOperationException>(
            () => FixtureRecordingFailureProtocol.Read(bytes, start, checkpoints, sources)).Code);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void DiagnosticSizeIsBoundedBeforeDeserialization(int offset)
    {
        var start = Start(); var failure = Failure(start); var bytes = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(FixtureProgressProtocol.Encode(failure)).PadRight(FixtureRecordingFailureProtocol.MaximumBytes + offset));
        if (offset <= 0) Assert.Equal(failure, FixtureRecordingFailureProtocol.Read(bytes, start, 3, 2));
        else
        {
            Assert.False(FixtureRecordingFailureProtocol.IsFailure(bytes));
            Assert.Equal("WORKER_PROGRESS_RECORDING_FAILURE_INVALID", Assert.Throws<BrowserOperationException>(
                () => FixtureRecordingFailureProtocol.Read(bytes, start, 3, 2)).Code);
        }
    }

    [Fact]
    public void UnadmittedSourcesOrUnknownInternalReasonCannotBecomeAPublicDiagnostic()
    {
        var start = Start(false);
        Assert.Throws<BrowserOperationException>(() => FixtureRecordingFailureProtocol.Read(FixtureProgressProtocol.Encode(Failure(start, 0, 1)), start, 0, 1));
        Assert.Throws<BrowserOperationException>(() => FixtureRecordingFailureProtocol.ErrorCode((FixtureRecordingFailureReason)999));
        var fault = Failure(start, 0, 0); var frame = FixtureProgressProtocol.Encode(fault with { Schema = FixtureProgressProtocol.Schema });
        Assert.False(FixtureRecordingFailureProtocol.IsFailure(frame));
    }
}
