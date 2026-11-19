namespace HeadlessScreenReader.Domain;

/// <summary>Actual AI reader output, not external AT speech or a compliance verdict.</summary>
public sealed record PublicTaskReaderRecord(string Schema, string Profile, long Sequence, string Command,
    string Mode, string Outcome, string Announcement, string AnnouncementSha256, bool AnnouncementTruncated,
    string? CursorNodeId, string? FocusedNodeId, int BrowserKeystrokes, bool InputMayHaveOccurred,
    string Verification, string? ErrorCode, string? BarrierCode, string? DocumentId, string? DocumentUrl,
    string? LastSnapshotSha256, long? CaptureEpoch, double StartedMilliseconds, double FinishedMilliseconds)
{
    public const string CurrentSchema = "hsr-public-task-reader-v1";
    public const int MaximumAnnouncementCharacters = 4096;
    public string SnapshotCoverage { get; init; } = "LastSnapshotFingerprintOnly";
    public string OutputKind { get; init; } = "AIReaderTextNotRecordedSpeech";

    public void Validate(string profile)
    {
        static bool Code(string? value) => value is null || value.Length is > 0 and <= 100
            && value.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_');
        static bool Id(string? value) => value is null || PublicTaskRules.Text(value, 200);
        if (Schema != CurrentSchema || Profile != profile || !PublicTaskRules.Text(Profile, 100)
            || SnapshotCoverage != "LastSnapshotFingerprintOnly" || OutputKind != "AIReaderTextNotRecordedSpeech"
            || Sequence is < 1 or > int.MaxValue
            || Command is not ("ReadCurrent" or "Tab" or "Activate")
            || !Enum.GetNames<ReaderMode>().Contains(Mode, StringComparer.Ordinal)
            || !Enum.GetNames<ReaderOutcome>().Contains(Outcome, StringComparer.Ordinal)
            || !Enum.GetNames<VerificationStatus>().Contains(Verification, StringComparer.Ordinal)
            || Announcement is null || Announcement.Length > MaximumAnnouncementCharacters || !PublicTaskRules.Digest(AnnouncementSha256)
            || !AnnouncementTruncated && PublicTaskRules.Hash(Announcement) != AnnouncementSha256
            || !Id(CursorNodeId) || !Id(FocusedNodeId) || !Id(DocumentId) || BrowserKeystrokes is < 0 or > 1
            || !Code(ErrorCode) || !Code(BarrierCode)
            || (DocumentUrl is null) != (DocumentId is null) || (LastSnapshotSha256 is null) != (DocumentId is null)
            || (CaptureEpoch is null) != (DocumentId is null) || CaptureEpoch < 0
            || LastSnapshotSha256 is not null && !PublicTaskRules.Digest(LastSnapshotSha256)
            || !double.IsFinite(StartedMilliseconds) || !double.IsFinite(FinishedMilliseconds)
            || StartedMilliseconds < 0 || FinishedMilliseconds < StartedMilliseconds)
            throw new BrowserOperationException("PUBLIC_TASK_READER_RECORD_INVALID");
    }
}
