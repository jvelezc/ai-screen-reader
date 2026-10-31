using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public enum FixtureRecordingFailureReason
{
    Unconfirmed, CaptureBudgetExceeded, CaptureFrameInvalid, CaptureWriteFailed, RecordingFailed, Cancelled,
    SourceMetadataMissing, SourceHashChanged, SourceArtifactMissing, UnrecognizedArtifact, OriginalChanged, AcknowledgementRejected,
    SourceExportUnconfirmed,
    CollectionUnconfirmed, CalibrationUnconfirmed, TraversalUnconfirmed, FinalizationUnconfirmed,
    AlignmentUnconfirmed, PreparationUnconfirmed, ValidationUnconfirmed, CleanupUnconfirmed,
    CaptureStopUnconfirmed, BrowserShutdownUnconfirmed, OutputTransferUnconfirmed
}

/// <summary>Trusted child operation only; never inferred from an exception message or target page.</summary>
public enum FixtureRecordingBoundary { Unknown, Collection, Calibration, Traversal, Finalization, Alignment, Preparation, Validation, Cleanup, OutputTransfer }

/// <summary>Bounded child-reported diagnostic only. It conveys no input, cleanup or completion authority.</summary>
public sealed record FixtureRecordingFailure(string Schema, Guid ExecutionId, int CheckpointSequence, int SourceSequence,
    FixtureRecordingFailureReason Reason);

public static class FixtureRecordingFailureProtocol
{
    public const string Schema = "hsr-fixture-recording-failure-v1";
    public const int MaximumBytes = 1024;

    public static FixtureRecordingFailure FromException(FixtureProgressStart start, int checkpoints, int sources, Exception failure) =>
        FromException(start, checkpoints, sources, failure, FixtureRecordingBoundary.Unknown);

    public static FixtureRecordingFailure FromException(FixtureProgressStart start, int checkpoints, int sources, Exception failure,
        FixtureRecordingBoundary boundary)
    {
        var specific = failure is BrowserOperationException
        {
            Code: "CAPTURE_STOP_UNCONFIRMED" or "CHROMIUM_COLLECTOR_STOP_UNCONFIRMED", InnerException: BrowserOperationException
            {
                Code: "CAPTURE_FRAME_LIMIT" or "CAPTURE_FRAME_INVALID" or "CAPTURE_FRAME_WRITE_FAILED"
                or "WORKER_PROGRESS_MEDIA_EXPORT_UNCONFIRMED"
            } cause
        } ? cause : failure;
        var reason = specific is BrowserOperationException known ? known.Code switch
        {
            "CAPTURE_FRAME_LIMIT" => FixtureRecordingFailureReason.CaptureBudgetExceeded,
            "CAPTURE_FRAME_INVALID" => FixtureRecordingFailureReason.CaptureFrameInvalid,
            "CAPTURE_FRAME_WRITE_FAILED" => FixtureRecordingFailureReason.CaptureWriteFailed,
            "TRAVERSAL_RECORDING_FAILED" => FixtureRecordingFailureReason.RecordingFailed,
            "TRAVERSAL_RECORDING_CANCELLED" => FixtureRecordingFailureReason.Cancelled,
            "RECORDING_SOURCE_METADATA_MISSING" => FixtureRecordingFailureReason.SourceMetadataMissing,
            "RECORDING_SOURCE_HASH_CHANGED" => FixtureRecordingFailureReason.SourceHashChanged,
            "RECORDING_SOURCE_ARTIFACT_MISSING" => FixtureRecordingFailureReason.SourceArtifactMissing,
            "RECORDING_ARTIFACT_UNRECOGNIZED" => FixtureRecordingFailureReason.UnrecognizedArtifact,
            "WORKER_KEYBOARD_ORIGINAL_CHANGED" => FixtureRecordingFailureReason.OriginalChanged,
            "WORKER_KEYBOARD_RECORDING_UNCONFIRMED" => FixtureRecordingFailureReason.Unconfirmed,
            "WORKER_PROGRESS_INVALID" or "WORKER_PROGRESS_MEDIA_INVALID" => FixtureRecordingFailureReason.AcknowledgementRejected,
            "WORKER_PROGRESS_MEDIA_EXPORT_UNCONFIRMED" => FixtureRecordingFailureReason.SourceExportUnconfirmed,
            _ => FixtureRecordingFailureReason.Unconfirmed
        } : FixtureRecordingFailureReason.Unconfirmed;
        if (reason == FixtureRecordingFailureReason.Unconfirmed && boundary != FixtureRecordingBoundary.Unknown && Enum.IsDefined(boundary))
            reason = specific is BrowserOperationException uncertain ? uncertain.Code switch
            {
                "CAPTURE_STOP_UNCONFIRMED" => FixtureRecordingFailureReason.CaptureStopUnconfirmed,
                "CHROMIUM_COLLECTOR_STOP_UNCONFIRMED" or "CHROMIUM_BROWSER_CLOSE_UNCONFIRMED"
                    or "CHROMIUM_PROCESS_EXIT_UNCONFIRMED" or "CHROMIUM_SHUTDOWN_UNCONFIRMED"
                    or "CHROMIUM_OWNED_ROOT_STOP_UNCONFIRMED" or "CHROMIUM_TRANSPORT_CLOSE_UNCONFIRMED"
                    or "CHROMIUM_PROCESS_WATCH_RELEASE_UNCONFIRMED" => FixtureRecordingFailureReason.BrowserShutdownUnconfirmed,
                _ => BoundaryReason(boundary)
            } : BoundaryReason(boundary);
        return new(Schema, start.ExecutionId, checkpoints, sources, reason);
    }

    private static FixtureRecordingFailureReason BoundaryReason(FixtureRecordingBoundary boundary) => boundary switch
    {
        FixtureRecordingBoundary.Collection => FixtureRecordingFailureReason.CollectionUnconfirmed,
        FixtureRecordingBoundary.Calibration => FixtureRecordingFailureReason.CalibrationUnconfirmed,
        FixtureRecordingBoundary.Traversal => FixtureRecordingFailureReason.TraversalUnconfirmed,
        FixtureRecordingBoundary.Finalization => FixtureRecordingFailureReason.FinalizationUnconfirmed,
        FixtureRecordingBoundary.Alignment => FixtureRecordingFailureReason.AlignmentUnconfirmed,
        FixtureRecordingBoundary.Preparation => FixtureRecordingFailureReason.PreparationUnconfirmed,
        FixtureRecordingBoundary.Validation => FixtureRecordingFailureReason.ValidationUnconfirmed,
        FixtureRecordingBoundary.Cleanup => FixtureRecordingFailureReason.CleanupUnconfirmed,
        FixtureRecordingBoundary.OutputTransfer => FixtureRecordingFailureReason.OutputTransferUnconfirmed,
        _ => FixtureRecordingFailureReason.Unconfirmed
    };

    public static bool IsFailure(byte[] bytes)
    {
        if (bytes.Length > MaximumBytes) return false;
        using var document = JsonDocument.Parse(bytes, new() { AllowDuplicateProperties = false });
        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("Schema", out var schema) && schema.ValueKind == JsonValueKind.String
            && schema.GetString() == Schema;
    }

    public static FixtureRecordingFailure Read(byte[] bytes, FixtureProgressStart start, int checkpoints, int sources)
    {
        FixtureProgressProtocol.Validate(start);
        if (bytes.Length > MaximumBytes) throw Invalid();
        var value = JsonSerializer.Deserialize<FixtureRecordingFailure>(bytes, ReaderWorkerWire.Json) ?? throw Invalid();
        if (value.Schema != Schema || value.ExecutionId != start.ExecutionId || value.ExecutionId == Guid.Empty
            || value.CheckpointSequence != checkpoints || checkpoints < 0
            || checkpoints > 1 + 2 * AuthoredKeyboardRoutes.Plans[start.Job.TraversalRoute - 1].Instructions.Length
            || value.SourceSequence != sources || sources < 0 || sources > FixtureSourceProtocol.MaximumFrames
            || (!start.RetainSourceFrames && sources != 0) || !Enum.IsDefined(value.Reason)) throw Invalid();
        return value;
    }

    public static string ErrorCode(FixtureRecordingFailureReason reason) => reason switch
    {
        FixtureRecordingFailureReason.CaptureBudgetExceeded => "WORKER_PROGRESS_RECORDING_CAPTURE_BUDGET_EXCEEDED",
        FixtureRecordingFailureReason.CaptureFrameInvalid => "WORKER_PROGRESS_RECORDING_CAPTURE_FRAME_INVALID",
        FixtureRecordingFailureReason.CaptureWriteFailed => "WORKER_PROGRESS_RECORDING_CAPTURE_WRITE_FAILED",
        FixtureRecordingFailureReason.RecordingFailed => "WORKER_PROGRESS_RECORDING_FAILED",
        FixtureRecordingFailureReason.Cancelled => "WORKER_PROGRESS_RECORDING_CANCELLED",
        FixtureRecordingFailureReason.SourceMetadataMissing => "WORKER_PROGRESS_RECORDING_SOURCE_METADATA_MISSING",
        FixtureRecordingFailureReason.SourceHashChanged => "WORKER_PROGRESS_RECORDING_SOURCE_HASH_CHANGED",
        FixtureRecordingFailureReason.SourceArtifactMissing => "WORKER_PROGRESS_RECORDING_SOURCE_ARTIFACT_MISSING",
        FixtureRecordingFailureReason.UnrecognizedArtifact => "WORKER_PROGRESS_RECORDING_ARTIFACT_UNRECOGNIZED",
        FixtureRecordingFailureReason.OriginalChanged => "WORKER_PROGRESS_RECORDING_ORIGINAL_CHANGED",
        FixtureRecordingFailureReason.Unconfirmed => "WORKER_PROGRESS_RECORDING_UNCONFIRMED",
        FixtureRecordingFailureReason.AcknowledgementRejected => "WORKER_PROGRESS_RECORDING_ACK_REJECTED",
        FixtureRecordingFailureReason.SourceExportUnconfirmed => "WORKER_PROGRESS_RECORDING_SOURCE_EXPORT_UNCONFIRMED",
        FixtureRecordingFailureReason.CollectionUnconfirmed => "WORKER_PROGRESS_RECORDING_COLLECTION_UNCONFIRMED",
        FixtureRecordingFailureReason.CalibrationUnconfirmed => "WORKER_PROGRESS_RECORDING_CALIBRATION_UNCONFIRMED",
        FixtureRecordingFailureReason.TraversalUnconfirmed => "WORKER_PROGRESS_RECORDING_TRAVERSAL_UNCONFIRMED",
        FixtureRecordingFailureReason.FinalizationUnconfirmed => "WORKER_PROGRESS_RECORDING_FINALIZATION_UNCONFIRMED",
        FixtureRecordingFailureReason.AlignmentUnconfirmed => "WORKER_PROGRESS_RECORDING_ALIGNMENT_UNCONFIRMED",
        FixtureRecordingFailureReason.PreparationUnconfirmed => "WORKER_PROGRESS_RECORDING_PREPARATION_UNCONFIRMED",
        FixtureRecordingFailureReason.ValidationUnconfirmed => "WORKER_PROGRESS_RECORDING_VALIDATION_UNCONFIRMED",
        FixtureRecordingFailureReason.CleanupUnconfirmed => "WORKER_PROGRESS_RECORDING_CLEANUP_UNCONFIRMED",
        FixtureRecordingFailureReason.CaptureStopUnconfirmed => "WORKER_PROGRESS_RECORDING_CAPTURE_STOP_UNCONFIRMED",
        FixtureRecordingFailureReason.BrowserShutdownUnconfirmed => "WORKER_PROGRESS_RECORDING_BROWSER_SHUTDOWN_UNCONFIRMED",
        FixtureRecordingFailureReason.OutputTransferUnconfirmed => "WORKER_PROGRESS_RECORDING_OUTPUT_TRANSFER_UNCONFIRMED",
        _ => throw Invalid()
    };

    public static bool IsPublicCode(string code) => Enum.GetValues<FixtureRecordingFailureReason>().Any(reason => ErrorCode(reason) == code);
    private static BrowserOperationException Invalid() => new("WORKER_PROGRESS_RECORDING_FAILURE_INVALID");
}
