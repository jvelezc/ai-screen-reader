using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Cli.HostedControl;

namespace HeadlessScreenReader.Tests;

public sealed class ScanCancellationProviderTests
{
    private static PortalIdentity Actor => new(PortalProjectClientTests.Actor, "reader@example.invalid", PortalProjectClientTests.Bearer);
    private static JsonObject Row => new()
    {
        ["organization_id"] = PortalProjectClientTests.Organization.ToString("D"),
        ["scan_id"] = PortalProjectClientTests.Scan.ToString("D"),
        ["job_id"] = PortalProjectClientTests.Job.ToString("D"),
        ["generation"] = 0,
        ["producer_id"] = null,
        ["status"] = "Requested",
        ["input_assessment"] = "NoAttemptStarted",
        ["requested_at"] = "2026-10-31T00:00:00Z"
    };

    [Fact]
    public async Task CancellationRpcUsesOriginalBearerAndOnlyTheAdmittedScopeWithoutClientActorOrProducer()
    {
        var calls = 0;
        using var http = PortalProjectClientTests.Client(async (request, token) =>
        {
            calls++; Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(new Uri(PortalOptions.Project, "rest/v1/rpc/request_scan_cancellation"), request.RequestUri);
            Assert.Equal(PortalProjectClientTests.Bearer, request.Headers.Authorization!.Parameter);
            Assert.Equal("hsr", request.Headers.GetValues("Content-Profile").Single());
            Assert.Equal(PortalProjectClientTests.Key, request.Headers.GetValues("apikey").Single());
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal(2, body.RootElement.EnumerateObject().Count());
            Assert.Equal(PortalProjectClientTests.Organization, body.RootElement.GetProperty("p_organization_id").GetGuid());
            Assert.Equal(PortalProjectClientTests.Scan, body.RootElement.GetProperty("p_scan_id").GetGuid());
            return PortalProjectClientTests.Response(new JsonArray(Row).ToJsonString());
        });
        var value = await new SupabasePortalClient(http, PortalProjectClientTests.Key)
            .RequestScanCancellationAsync(Actor, PortalProjectClientTests.Organization, PortalProjectClientTests.Scan, default);
        Assert.Equal(1, calls); Assert.Equal(0, value.Generation); Assert.Null(value.Producer);
        Assert.Equal("NoAttemptStarted", value.InputAssessment); Assert.Equal("Requested", value.Status);
    }

    [Theory]
    [InlineData("extra-field")]
    [InlineData("missing-field")]
    [InlineData("duplicate-field")]
    [InlineData("wrong-case")]
    [InlineData("generation-string")]
    [InlineData("generation-fraction")]
    [InlineData("generation-negative")]
    [InlineData("generation-without-producer")]
    [InlineData("producer-without-attempt")]
    [InlineData("unscoped-organization")]
    [InlineData("unscoped-scan")]
    [InlineData("missing-time")]
    [InlineData("invented-stop")]
    [InlineData("invented-cleanup")]
    [InlineData("zero-rows")]
    [InlineData("two-rows")]
    public async Task UntrustedRpcShapeOrClaimsCannotBecomeAConfirmedCancellation(string defect)
    {
        var row = Row;
        switch (defect)
        {
            case "extra-field": row["actor"] = "client-asserted"; break;
            case "missing-field": row.Remove("requested_at"); break;
            case "wrong-case": row["Generation"] = row["generation"]!.DeepClone(); row.Remove("generation"); break;
            case "generation-string": row["generation"] = "0"; break;
            case "generation-fraction": row["generation"] = 0.5; break;
            case "generation-negative": row["generation"] = -1; break;
            case "generation-without-producer": row["generation"] = 1; break;
            case "producer-without-attempt": row["producer_id"] = Guid.NewGuid().ToString("D"); break;
            case "unscoped-organization": row["organization_id"] = Guid.NewGuid().ToString("D"); break;
            case "unscoped-scan": row["scan_id"] = Guid.NewGuid().ToString("D"); break;
            case "missing-time": row["requested_at"] = null; break;
            case "invented-stop": row["status"] = "Stopped"; break;
            case "invented-cleanup": row["nativeCleanup"] = "Confirmed"; break;
        }
        var body = new JsonArray(row).ToJsonString();
        if (defect == "zero-rows") body = "[]";
        if (defect == "two-rows") body = new JsonArray(row.DeepClone(), row.DeepClone()).ToJsonString();
        if (defect == "duplicate-field") body = body.Replace("\"generation\":0", "\"generation\":0,\"generation\":0", StringComparison.Ordinal);
        var calls = 0;
        using var http = PortalProjectClientTests.Client((_, _) => { calls++; return Task.FromResult(PortalProjectClientTests.Response(body)); });
        var failure = await Assert.ThrowsAsync<PortalFault>(() => new SupabasePortalClient(http, PortalProjectClientTests.Key)
            .RequestScanCancellationAsync(Actor, PortalProjectClientTests.Organization, PortalProjectClientTests.Scan, default));
        Assert.Equal(503, failure.Status); Assert.StartsWith("CONTROL_", failure.Code, StringComparison.Ordinal);
        Assert.Equal(1, calls); Assert.DoesNotContain(PortalProjectClientTests.Bearer, failure.ToString(), StringComparison.Ordinal);
    }
}
