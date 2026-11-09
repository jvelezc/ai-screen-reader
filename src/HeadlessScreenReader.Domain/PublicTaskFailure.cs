using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

/// <summary>First-party code location only. No machine path, exception message, locals or request content.</summary>
public sealed record PublicTaskFailureFrame(string Type, string Method, string? SourceFile, int Line, int IlOffset);

public sealed record PublicTaskFailure(string Stage, string Category, string? Code, DateTimeOffset OccurredAt,
    double ElapsedMilliseconds, int EventSequence, ImmutableArray<PublicTaskFailureFrame> Frames)
{
    public void Validate(int maximumEvent)
    {
        bool Symbol(string value) => value.Length is > 0 and <= 300
            && value.All(c => char.IsAsciiLetterOrDigit(c) || "._+`<>|".Contains(c));
        if (Stage is not ("Start" or "Isolation" or "Open" or "Discover" or "Snapshot" or "Input" or "Recording" or "Report")
            || Category is not ("Browser" or "Playwright" or "Protocol" or "Timeout" or "Io" or "Canceled" or "Unexpected")
            || Code is not null && (Code.Length is < 1 or > 100 || Code.Any(c => !char.IsAsciiLetterUpper(c) && c != '_'))
            || OccurredAt == default || !double.IsFinite(ElapsedMilliseconds) || ElapsedMilliseconds is < 0 or > 180000
            || EventSequence < 0 || EventSequence > maximumEvent || Frames.IsDefault || Frames.Length > 8
            || Frames.Any(f => !f.Type.StartsWith("HeadlessScreenReader.", StringComparison.Ordinal) || !Symbol(f.Type) || !Symbol(f.Method)
                || f.SourceFile is { } file && (file.Length > 120 || !file.EndsWith(".cs", StringComparison.Ordinal)
                    || file.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '_' and not '-'))
                || f.Line is < 0 or > 1000000 || f.IlOffset < -1))
            throw new BrowserOperationException("PUBLIC_TASK_FAILURE_TRACE_INVALID");
    }
}
