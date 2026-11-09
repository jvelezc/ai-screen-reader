using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class PublicTaskTransportAttributionTests
{
    internal static PublicTaskReport CleanReport()
    {
        var report = PublicTaskIssueTableTests.WithNetwork(PublicTaskTests.Report());
        return report with
        {
            AssessmentPolicy = PublicTaskReport.TransportAttributionPolicy,
            Attempts = report.Attempts.Select(a => a with { Network = EmptyTrace() }).ToImmutableArray()
        };
    }

    private static PublicTaskNetworkTrace EmptyTrace() =>
        new PublicTaskNetworkLog(PublicTaskNetworkTrace.PublicDependencyPolicy, DateTimeOffset.Parse("2026-11-09T00:00:00Z")).Snapshot();

    private static PublicTaskReport ProxyFailure(PublicTaskReport report) => report with
    {
        Egress = report.Egress! with
        {
            Events = report.Egress.Events.Add(new(5, 2, 4, "Failed", "CONNECT", "connect",
                "https://api.example.net", new string('d', 64), null, "EGRESS_UPSTREAM_UNCONFIRMED"))
        }
    };

    private static PublicTaskReport BrowserAbort(PublicTaskReport report, string resource)
    {
        var log = new PublicTaskNetworkLog(PublicTaskNetworkTrace.PublicDependencyPolicy, report.Egress!.StartedAt);
        var id = log.Begin("https://example.com/resource", "GET", resource, 1);
        log.Record(id, "Failed", 2, code: "ERR_ABORTED");
        return report with { Attempts = report.Attempts.SetItem(0, report.Attempts[0] with { Network = log.Snapshot() }) };
    }

    [Fact]
    public void PostProbeProxyFailureCannotProveAKeyboardBarrier()
    {
        var report = ProxyFailure(CleanReport());
        var result = PublicTaskAssessment.Evaluate(report, true, true);
        Assert.Equal("InsufficientEvidence", result.Outcome);
        Assert.False(result.Conditions.Single(c => c.Id == "RecordedBarrier").Satisfied);
        Assert.False(result.Conditions.Single(c => c.Id == "RepeatedFailure").Satisfied);
        Assert.True(result.Conditions.Single(c => c.Id == "OriginalArtifacts").Satisfied);
        var issue = Assert.Single(PublicTaskIssueTable.From(report).Issues);
        Assert.Equal("Egress", issue.Source); Assert.Equal("/Egress/Events/4", issue.EvidencePointer);
    }

    [Fact]
    public void DroppedProxyEventsCannotProveAKeyboardBarrier()
    {
        var report = CleanReport(); report = report with { Egress = report.Egress! with { DroppedEvents = 1 } };
        var result = PublicTaskAssessment.Evaluate(report, true, true);
        Assert.Equal("InsufficientEvidence", result.Outcome);
        Assert.False(result.Conditions.Single(c => c.Id == "RecordedBarrier").Satisfied);
        Assert.Equal(1, PublicTaskIssueTable.From(report).DroppedTraceEvents);
    }

    [Fact]
    public void ExpectedPreflightDenialDoesNotInvalidateACleanRepeatedBarrier()
    {
        var report = CleanReport();
        var assessment = PublicTaskAssessment.Evaluate(report, true, true);
        Assert.Equal("WebsiteEvidenceReady", assessment.Outcome);
        Assert.All(assessment.Conditions, c => Assert.True(c.Satisfied, c.Id));
        Assert.Equal(1, PublicTaskIssueTable.From(report).ExpectedIsolationProbeDenials);
        // The same denial lies outside the recorded preflight when its ending marker is one earlier.
        report = report with { EgressIsolationProbeThroughSequence = 1 };
        Assert.Equal("InsufficientEvidence", PublicTaskAssessment.Evaluate(report, true, true).Outcome);
    }

    [Theory]
    [InlineData("document")]
    [InlineData("xhr")]
    [InlineData("fetch")]
    [InlineData("script")]
    [InlineData("stylesheet")]
    [InlineData("image")]
    [InlineData("media")]
    [InlineData("font")]
    [InlineData("texttrack")]
    [InlineData("eventsource")]
    [InlineData("websocket")]
    [InlineData("manifest")]
    [InlineData("beacon")]
    [InlineData("ping")]
    [InlineData("cspreport")]
    [InlineData("other")]
    public void AbortedRequestCannotProveAFailedGoalIsAKeyboardBarrier(string resource)
    {
        var report = BrowserAbort(CleanReport(), resource);
        var assessment = PublicTaskAssessment.Evaluate(report, true, true);
        Assert.Equal("InsufficientEvidence", assessment.Outcome);
        Assert.False(assessment.Conditions.Single(c => c.Id == "RecordedBarrier").Satisfied);
        Assert.Equal("ERR_ABORTED", Assert.Single(PublicTaskIssueTable.From(report).Issues).Code);
    }

    [Fact]
    public void ObservedGoalsSurviveTransportErrorsUnderCurrentPolicy()
    {
        var report = BrowserAbort(ProxyFailure(CleanReport()), "media");
        report = report with
        {
            Egress = report.Egress! with { DroppedEvents = 1 },
            Attempts = report.Attempts.Select(a => a with { Outcome = "Reached", FinalUrl = report.Goal!.DestinationUrl }).ToImmutableArray()
        };
        var assessment = PublicTaskAssessment.Evaluate(report, true, true);
        Assert.Equal("NoBlockingBarrierInTestedTask", assessment.Outcome);
        Assert.False(assessment.Conditions.Single(c => c.Id == "RecordedBarrier").Satisfied);
        Assert.True(assessment.Conditions.Single(c => c.Id == "PointerComparison").Satisfied);
        Assert.Equal(2, PublicTaskIssueTable.From(report).KnownIssueEvents);
    }

    [Fact]
    public void AssessmentPolicyRequiresCompleteMatchingNetworkTraces()
    {
        var report = CleanReport(); report.Validate(report.Start);
        foreach (var invalid in new[]
        {
            report with { AssessmentPolicy = "unknown" }, report with { AssessmentPolicy = "" },
            report with { NetworkPolicy = null }, report with { Egress = null },
            report with { EgressIsolationProbeThroughSequence = null },
            report with { Attempts = report.Attempts.SetItem(0, report.Attempts[0] with { Network = null }) },
            report with { Attempts = report.Attempts.SetItem(0, report.Attempts[0] with { Failures = null }) },
            PublicTaskTests.Report() with { AssessmentPolicy = PublicTaskReport.TransportAttributionPolicy }
        }) Assert.Equal("PUBLIC_TASK_REPORT_INVALID", Assert.Throws<BrowserOperationException>(() => invalid.Validate(invalid.Start)).Code);
    }

    [Fact]
    public void MissingPolicyRetainsLegacyAssessmentAndWireFormat()
    {
        var report = BrowserAbort(ProxyFailure(CleanReport()), "document") with { AssessmentPolicy = null };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(report, ReaderWorkerWire.Json);
        using var json = JsonDocument.Parse(bytes);
        Assert.False(json.RootElement.TryGetProperty("AssessmentPolicy", out _));
        var legacy = JsonSerializer.Deserialize<PublicTaskReport>(bytes, ReaderWorkerWire.Json)!;
        Assert.Null(legacy.AssessmentPolicy);
        Assert.Equal("WebsiteEvidenceReady", PublicTaskAssessment.Evaluate(legacy, true, true).Outcome);
        Assert.Equal("InsufficientEvidence", PublicTaskAssessment.Evaluate(legacy with { AssessmentPolicy = PublicTaskReport.TransportAttributionPolicy }, true, true).Outcome);
    }

    [Fact]
    public void CurrentPolicySurvivesOriginalRoundTripWithoutPromisingABarrierWhenDiscoveryFindsNoGoal()
    {
        var report = CleanReport();
        report = report with { Goal = null, Attempts = [], Files = [], DiscoveryNetwork = EmptyTrace() };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(report, ReaderWorkerWire.Json);
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal("TransportAttributionV2", json.RootElement.GetProperty("AssessmentPolicy").GetString());
        var restored = JsonSerializer.Deserialize<PublicTaskReport>(bytes, ReaderWorkerWire.Json)!;
        restored.Validate(report.Start);
        var assessment = PublicTaskAssessment.Evaluate(restored, true, true);
        Assert.Equal("InsufficientEvidence", assessment.Outcome);
        Assert.False(assessment.Conditions.Single(c => c.Id == "MeaningfulTask").Satisfied);
        Assert.False(assessment.Conditions.Single(c => c.Id == "RecordedBarrier").Satisfied);
    }

    [Fact]
    public async Task FrozenLegacyPublicationRemainsReadableWithoutReassessingItUnderNewPolicy()
    {
        using var rig = await PublicTaskHttpTests.Rig.Create(r =>
            BrowserAbort(ProxyFailure(CleanReport()), "document") with { AssessmentPolicy = null });
        string[] ids = ["MeaningfulTask", "RecordedBarrier", "ExactTrigger", "AccessibilityContext", "RepeatedFailure", "PointerComparison", "ReproducibleEnvironment", "OriginalArtifacts"];
        rig.Status = rig.Status with { Publication = rig.Status.Publication! with { Assessment = new("WebsiteEvidenceReady", ids.Select(id => new PublicTaskCondition(id, true)).ToImmutableArray()) } };
        var original = rig.Files["report.json"].ToArray();
        await using var server = await rig.Server(); using var http = rig.Http(server);
        using var response = await http.GetAsync(rig.Path + "/result");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<PortalTaskResult>(PortalJson.Options))!;
        Assert.Equal("WebsiteEvidenceReady", result.Assessment.Outcome);
        Assert.All(result.Assessment.Conditions, c => Assert.True(c.Satisfied, c.Id));
        Assert.Equal(original, rig.Files["report.json"]); Assert.Equal(0, rig.Creates);
    }
}
