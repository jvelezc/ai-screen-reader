using System.Runtime.CompilerServices;
using System.Text.Json;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

public sealed class PublicTaskFailureTests
{
    private static class GenericFailure<T>
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Throw() => throw new IOException("private-message");
    }

    [Fact]
    public void GenericCodeLocationsUseDefinitionsWithoutRuntimeTypeArguments()
    {
        try { GenericFailure<string>.Throw(); }
        catch (IOException error)
        {
            var failure = PublicTaskFailureCapture.Create(error, "Snapshot", DateTimeOffset.Parse("2026-11-09T00:00:00Z"), 10, 0);
            failure.Validate(0);
            Assert.Contains(failure.Frames, f => f.Type.EndsWith("GenericFailure`1", StringComparison.Ordinal));
            Assert.DoesNotContain("System.String", JsonSerializer.Serialize(failure), StringComparison.Ordinal);
            return;
        }
        Assert.Fail("The generic source operation did not throw.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowPrivateDetails() => throw new IOException("Authorization: Bearer never-store-this; C:\\private\\credentials.env");

    [Fact]
    public void FailureTracePinsTheCodeLocationAndInputEventWithoutExceptionTextOrMachinePaths()
    {
        Exception original;
        try { ThrowPrivateDetails(); throw new InvalidOperationException(); }
        catch (IOException error) { original = error; }
        var failure = PublicTaskFailureCapture.Create(original, "Input", DateTimeOffset.Parse("2026-11-09T00:00:00Z"), 1234, 13);
        failure.Validate(13);
        Assert.Equal("Io", failure.Category); Assert.Equal("Input", failure.Stage); Assert.Equal(13, failure.EventSequence);
        Assert.Contains(failure.Frames, f => f.Type == typeof(PublicTaskFailureTests).FullName && f.Method == "ThrowPrivateDetails");
        Assert.All(failure.Frames, f => { Assert.DoesNotContain('/', f.SourceFile ?? ""); Assert.DoesNotContain('\\', f.SourceFile ?? ""); });
        var json = JsonSerializer.Serialize(failure);
        foreach (var secret in new[] { "Authorization", "never-store-this", "credentials.env", "C:\\private" }) Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureIssueTablePointsToTheSameOriginalOperationAndInputSequence()
    {
        var report = PublicTaskIssueTableTests.WithNetwork(PublicTaskTests.Report());
        var failure = new PublicTaskFailure("Recording", "Io", "PUBLIC_TASK_VIDEO_UNCONFIRMED", DateTimeOffset.Parse("2026-11-09T00:00:01Z"), 1000, 1, []);
        var attempt = report.Attempts[0] with { Outcome = "Unconfirmed", ErrorCode = "PUBLIC_TASK_ATTEMPT_UNCONFIRMED", Failures = [failure] };
        report = report with { Attempts = report.Attempts.SetItem(0, attempt) };
        var issue = PublicTaskIssueTable.From(report).Issues.First();
        Assert.Equal("Collector", issue.Source); Assert.Equal("RECORDING", issue.Method); Assert.Equal(0, issue.RequestId);
        Assert.Equal(1, issue.InputEventSequence); Assert.Equal("/Attempts/0/Failures/0", issue.EvidencePointer);
        Assert.Contains("media-decoder", issue.NextAction, StringComparison.Ordinal);
        Assert.Equal("InsufficientEvidence", PublicTaskAssessment.Evaluate(report, true, true).Outcome);
    }
}
