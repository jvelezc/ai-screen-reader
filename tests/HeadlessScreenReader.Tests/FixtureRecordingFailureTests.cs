using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class FixtureRecordingFailureTests
{
    private static FixtureProgressStart Start(bool sources = true) => new(FixtureProgressProtocol.Schema,
        FixtureWorkerTests.Job() with { RecordObservation = true, TraversalRoute = 1 }, Guid.NewGuid())
    { RetainSourceFrames = sources };
    private static FixtureRecordingFailure Failure(FixtureProgressStart start, int checkpoints = 3, int sources = 2) =>
        new(FixtureRecordingFailureProtocol.Schema, start.ExecutionId, checkpoints, sources, FixtureRecordingFailureReason.CaptureBudgetExceeded);

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
