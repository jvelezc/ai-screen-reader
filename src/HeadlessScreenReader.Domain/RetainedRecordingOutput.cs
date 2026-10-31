namespace HeadlessScreenReader.Domain;

/// <summary>A verified complete child-output ZIP retained under its original producer, distinct from queue completion or archive publication.</summary>
public sealed record RetainedRecordingOutput(string Schema, FixtureJob Job, Guid WorkerId, string BundleSha256,
    int Bytes, string Recording, bool NativeRemovalAcknowledged, bool IsCompletionReceipt,
    bool CanReplay, string GoalVerification)
{
    public const string CurrentSchema = "hsr-retained-recording-output-v1";
    public const int MaximumBytes = 25 * 1024 * 1024;
    public void Validate()
    {
        Require(Job is not null); Job!.Validate();
        Require(Schema == CurrentSchema && Job.RecordObservation && WorkerId != Guid.Empty
            && BundleSha256 is { Length: 64 } && BundleSha256.All(char.IsAsciiHexDigitLower)
            && Bytes is > 0 and <= MaximumBytes && Recording == (Job.TraversalRoute > 0 ? "KeyboardTraversal" : "PassiveObservation")
            && NativeRemovalAcknowledged && !IsCompletionReceipt && !CanReplay && GoalVerification == "NotAssessed");
    }
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition)
    { if (!condition) throw new BrowserOperationException("WORKER_RECORDED_OUTPUT_INVALID"); }
}

/// <summary>A bounded byte page of the exact retained child-output ZIP; no private native-ownership wrapper is included.</summary>
public sealed record RetainedRecordingOutputPage(RetainedRecordingOutput Output, int Offset, byte[] Data,
    string PageSha256, int? NextOffset)
{
    public const int PageBytes = 65536;
}
