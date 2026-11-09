using System.Collections.Immutable;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

public sealed class PublicTaskNetworkTraceTests
{
    private static PublicTaskNetworkLog Log() => new(PublicTaskNetworkTrace.PublicDependencyPolicy, DateTimeOffset.Parse("2026-11-09T00:00:00Z"));

    [Fact]
    public void TimelineRetainsRequestResponseFailureAndPolicyCauseWithoutSecrets()
    {
        var log = Log();
        var first = log.Begin("https://api.example.net/private/person?token=never-store-this#secret", "POST", "fetch", 10);
        log.Record(first, "Response", 15, 401, "HTTP_ERROR");
        log.Record(first, "Finished", 20);
        var second = log.Begin("https://cdn.example.net/script.js?key=another-secret", "GET", "script", 25);
        log.Record(second, "PolicyDenied", 30, code: "DEPENDENCY_URL_DENIED");
        log.Record(second, "Failed", 31, code: "ERR_FAILED");
        var trace = log.Snapshot(); trace.Validate();
        Assert.Equal(2, trace.Requests); Assert.Equal(1, trace.PolicyDenials); Assert.Equal(0, trace.DroppedEvents);
        Assert.Equal(new[] { "Request", "Response", "Finished", "Request", "PolicyDenied", "Failed" }, trace.Events.Select(e => e.Phase));
        Assert.Equal(new[] { 1, 1, 1, 2, 2, 2 }, trace.Events.Select(e => e.RequestId));
        Assert.Equal(401, trace.Events[1].Status); Assert.Equal("POST", trace.Events[0].Method);
        Assert.Equal("https://api.example.net", trace.Events[0].Origin);
        Assert.Equal(PublicTaskRules.Hash("https://api.example.net/private/person?token=never-store-this#secret"), trace.Events[0].UrlSha256);
        var json = JsonSerializer.Serialize(trace);
        foreach (var secret in new[] { "private/person", "never-store-this", "another-secret", "#secret", "token=" }) Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
    }

    [Fact]
    public void OverflowIsExplicitAndLaterFailuresDisplaceRoutineEntries()
    {
        var log = Log();
        for (var i = 0; i < 300; i++) { var id = log.Begin("https://example.com/asset", "GET", "image", i); log.Record(id, "Finished", i); }
        var failure = log.Begin("https://api.example.net/data", "POST", "fetch", 301);
        log.Record(failure, "Failed", 302, code: "ERR_CONNECTION_REFUSED");
        var trace = log.Snapshot(); trace.Validate();
        Assert.Equal(301, trace.Requests); Assert.Equal(PublicTaskNetworkTrace.MaximumEvents, trace.Events.Length);
        Assert.True(trace.DroppedEvents > 0);
        Assert.Contains(trace.Events, e => e.RequestId == failure && e.Code == "ERR_CONNECTION_REFUSED");
        Assert.Equal(trace.Events.OrderBy(e => e.Sequence), trace.Events);
    }

    [Fact]
    public void MalformedOrSecretBearingObservationsAreRejected()
    {
        var log = Log(); var id = log.Begin("https://example.com/", "GET", "document", 0);
        log.Record(id, "Response", 1, 200);
        var trace = log.Snapshot();
        foreach (var bad in new[]
        {
            trace.Events[0] with { Origin = "https://example.com/path?token=secret" },
            trace.Events[0] with { Code = "Cookie: secret" },
            trace.Events[0] with { ElapsedMilliseconds = double.NaN },
            trace.Events[0] with { RequestId = 100 },
            trace.Events[0] with { Status = 999 },
            trace.Events[0] with { UrlSha256 = "invalid" },
            trace.Events[0] with { ResourceType = "private arbitrary text" }
        }) Assert.Throws<BrowserOperationException>(() => (trace with { Events = [bad] }).Validate());
        Assert.Null(PublicTaskNetworkTrace.OriginOf("https://user:password@example.com/private"));
    }

    [Fact]
    public void NewPolicyRequiresBoundTracesAndOldOriginalsStayLegacy()
    {
        var report = PublicTaskTests.Report();
        report.Validate(report.Start);
        Assert.Null(report.NetworkPolicy);
        var json = JsonSerializer.Serialize(report);
        Assert.DoesNotContain("NetworkPolicy", json, StringComparison.Ordinal);
        var missing = report with { NetworkPolicy = PublicTaskNetworkTrace.PublicDependencyPolicy };
        Assert.Throws<BrowserOperationException>(() => missing.Validate(missing.Start));
    }
}
