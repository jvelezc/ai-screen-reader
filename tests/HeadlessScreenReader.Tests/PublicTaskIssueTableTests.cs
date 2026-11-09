using System.Collections.Immutable;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

public sealed class PublicTaskIssueTableTests
{
    internal static PublicTaskReport WithNetwork(PublicTaskReport report)
    {
        var at = DateTimeOffset.Parse("2026-11-09T00:00:00Z");
        var network = new PublicTaskNetworkLog(PublicTaskNetworkTrace.PublicDependencyPolicy, at);
        var id = network.Begin("https://api.example.net/pricing?token=redacted", "POST", "fetch", 1);
        network.Record(id, "Response", 2, 503, "HTTP_ERROR"); network.Record(id, "Finished", 3);
        var proxy = new PublicTaskNetworkLog(PublicTaskNetworkTrace.PublicDependencyPolicy, at);
        var probe = proxy.Begin("https://unapproved.invalid/", "CONNECT", "connect", 0);
        proxy.Record(probe, "PolicyDenied", 1, code: "DEPENDENCY_URL_UNSUPPORTED");
        var actual = proxy.Begin("https://api.example.net/", "CONNECT", "connect", 2);
        proxy.Record(actual, "Response", 3, 200);
        return report with
        {
            NetworkPolicy = PublicTaskNetworkTrace.PublicDependencyPolicy,
            Egress = proxy.Snapshot(),
            EgressIsolationProbeThroughSequence = 2,
            Attempts = report.Attempts.Select(a => a with
            {
                Failures = [],
                Network = a.Number == 1 ? network.Snapshot()
                : new PublicTaskNetworkLog(PublicTaskNetworkTrace.PublicDependencyPolicy, at).Snapshot()
            }).ToImmutableArray()
        };
    }

    [Fact]
    public void IssueTableExplainsSameAttemptHttpCauseAndExcludesExpectedIsolationProbes()
    {
        var report = WithNetwork(PublicTaskTests.Report());
        var table = PublicTaskIssueTable.From(report); var issue = Assert.Single(table.Issues);
        Assert.Equal(1, table.KnownIssueEvents); Assert.Equal(1, table.ExpectedIsolationProbeDenials);
        Assert.Equal(0, table.MissingTraces); Assert.Equal(0, table.DroppedTraceEvents); Assert.False(table.RowsTruncated);
        Assert.Equal("PSR-HTTP-001", issue.CaseId); Assert.Equal("Browser", issue.Source); Assert.Equal(1, issue.Attempt);
        Assert.Equal(1, issue.RequestId); Assert.Equal(2, issue.Sequence); Assert.Equal(503, issue.Status);
        Assert.Equal("POST", issue.Method); Assert.Equal("https://api.example.net", issue.Origin);
        Assert.Equal("/Attempts/0/Network/Events/1", issue.EvidencePointer);
        Assert.Contains("HTTP 503", issue.Observation, StringComparison.Ordinal);
        Assert.Contains("server logs", issue.NextAction, StringComparison.Ordinal);
        Assert.Equal(DateTimeOffset.Parse("2026-11-09T00:00:00.002Z"), issue.OccurredAt);
    }

    [Fact]
    public void LegacyAbsenceDoesNotPretendThatDiagnosticsWereCollected()
    {
        var table = PublicTaskIssueTable.From(PublicTaskTests.Report());
        Assert.Equal(4, table.MissingTraces); Assert.Empty(table.Issues); Assert.Equal(0, table.KnownIssueEvents);
    }

    [Fact]
    public void FailedKeyboardTaskWithNetworkErrorsCannotBeCalledAProvenBarrier()
    {
        var report = WithNetwork(PublicTaskTests.Report());
        var result = PublicTaskAssessment.Evaluate(report, true, true);
        Assert.Equal("InsufficientEvidence", result.Outcome);
        Assert.False(result.Conditions.Single(c => c.Id == "RecordedBarrier").Satisfied);
        Assert.True(result.Conditions.Single(c => c.Id == "OriginalArtifacts").Satisfied);
    }

    [Fact]
    public void ObservedSuccessfulGoalsRemainSuccessfulDespiteIncidentalRequestErrors()
    {
        var report = WithNetwork(PublicTaskTests.Report());
        report = report with { Attempts = report.Attempts.Select(a => a with { Outcome = "Reached", FinalUrl = report.Goal!.DestinationUrl }).ToImmutableArray() };
        Assert.Equal("NoBlockingBarrierInTestedTask", PublicTaskAssessment.Evaluate(report, true, true).Outcome);
    }

    [Theory]
    [InlineData("websocket")]
    [InlineData("truncated")]
    public void UnconfirmedDependencyOrTruncatedTraceCannotProveAKeyboardBarrier(string scenario)
    {
        var report = WithNetwork(PublicTaskTests.Report());
        var log = new PublicTaskNetworkLog(PublicTaskNetworkTrace.PublicDependencyPolicy, DateTimeOffset.Parse("2026-11-09T00:00:00Z"));
        if (scenario == "websocket")
        {
            var id = log.Begin("wss://api.example.net/realtime", "GET", "websocket", 0);
            log.Record(id, "WebSocketRequested", 1);
        }
        else for (var i = 0; i < 300; i++) { var id = log.Begin("https://example.com/asset", "GET", "image", i); log.Record(id, "Finished", i); }
        report = report with { Attempts = report.Attempts.SetItem(0, report.Attempts[0] with { Network = log.Snapshot() }) };
        Assert.Equal("InsufficientEvidence", PublicTaskAssessment.Evaluate(report, true, true).Outcome);
        if (scenario == "websocket") Assert.Equal("WEBSOCKET_OUTCOME_UNCONFIRMED", Assert.Single(PublicTaskIssueTable.From(report).Issues).Code);
        else Assert.True(PublicTaskIssueTable.From(report).DroppedTraceEvents > 0);
    }

    [Fact]
    public void DiscoveryTraceIsRetainedEvenWhenNoSupportedTaskWasFound()
    {
        var report = WithNetwork(PublicTaskTests.Report()); var trace = report.Attempts[0].Network;
        report = report with { Goal = null, Attempts = [], Files = [], DiscoveryNetwork = trace };
        report.Validate(report.Start);
        var issue = Assert.Single(PublicTaskIssueTable.From(report).Issues);
        Assert.Equal("Discovery", issue.Source); Assert.Equal(0, issue.Attempt);
        Assert.Equal("/DiscoveryNetwork/Events/1", issue.EvidencePointer);
        Assert.Equal("InsufficientEvidence", PublicTaskAssessment.Evaluate(report, true, true).Outcome);
        Assert.Throws<BrowserOperationException>(() => (report with { DiscoveryNetwork = null }).Validate(report.Start));
    }

    [Fact]
    public void IssueTableBoundsRowsAndReportsBothTraceAndTableTruncation()
    {
        var report = WithNetwork(PublicTaskTests.Report());
        var log = new PublicTaskNetworkLog(PublicTaskNetworkTrace.PublicDependencyPolicy, DateTimeOffset.Parse("2026-11-09T00:00:00Z"));
        for (var i = 0; i < 100; i++) { var id = log.Begin("https://api.example.net/", "GET", "fetch", i); log.Record(id, "Response", i, 500, "HTTP_ERROR"); log.Record(id, "Finished", i); }
        report = report with { Attempts = report.Attempts.SetItem(0, report.Attempts[0] with { Network = log.Snapshot() }) };
        var table = PublicTaskIssueTable.From(report);
        Assert.Equal(100, table.KnownIssueEvents); Assert.Equal(32, table.Issues.Length);
        Assert.True(table.RowsTruncated); Assert.True(table.DroppedTraceEvents > 0);
        Assert.All(table.Issues, e => Assert.Equal("HTTP_ERROR", e.Code));
    }
}
