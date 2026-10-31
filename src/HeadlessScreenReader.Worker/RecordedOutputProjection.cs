using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Projects verified retained bytes without exposing host/native ownership. A missing cleanup acknowledgement never releases an export.</summary>
public static class RecordedOutputProjection
{
    public static RetainedRecordingOutput? Manifest(RecordedOutputSnapshot snapshot, FixtureJob expected, Guid worker)
    {
        Require(snapshot is not null && snapshot.Header is not null && snapshot.Header.Native is not null && snapshot.Seal is not null && snapshot.Bundle is not null);
        var header = snapshot!.Header; var seal = snapshot.Seal;
        OwnershipJournal.Validate(header.Native); expected.Validate();
        Require(header.Schema == "hsr-worker-recorded-output-v1" && header.Job == expected && expected.RecordObservation
            && worker != Guid.Empty && header.WorkerId == worker && header.Native.Job == (expected with { RecordObservation = false, TraversalRoute = 0 })
            && seal.Schema == header.Schema && seal.HeaderSha256 == TraversalArchive.Hash(JsonSerializer.SerializeToUtf8Bytes(header, ReaderWorkerWire.Json))
            && seal.Bytes is > 0 and <= RetainedRecordingOutput.MaximumBytes && seal.Bytes == snapshot.Bundle.Length
            && seal.BundleSha256 == TraversalArchive.Hash(snapshot.Bundle));
        if (expected.TraversalRoute > 0) KeyboardRecordingBundle.Decode(snapshot.Bundle, expected);
        else PassiveRecordingBundle.Decode(snapshot.Bundle, expected);
        if (snapshot.NativeRemoval is not { } removal) return null;
        Require(removal.Schema == "hsr-native-removal-v1" && removal.Intent == header.Native && removal.ContainerAbsenceConfirmed);
        var result = new RetainedRecordingOutput(RetainedRecordingOutput.CurrentSchema, expected, worker, seal.BundleSha256,
            seal.Bytes, expected.TraversalRoute > 0 ? "KeyboardTraversal" : "PassiveObservation", true, false, false, "NotAssessed");
        result.Validate(); return result;
    }
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition)
    { if (!condition) throw new BrowserOperationException("WORKER_RECORDED_OUTPUT_INVALID"); }
}
