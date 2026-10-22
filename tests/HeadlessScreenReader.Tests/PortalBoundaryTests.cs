using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

public sealed class PortalBoundaryTests
{
    [Theory]
    [InlineData("public-origin")]
    [InlineData("origin-user")]
    [InlineData("origin-path")]
    [InlineData("origin-query")]
    [InlineData("origin-fragment")]
    [InlineData("relative-root")]
    [InlineData("negative-port")]
    [InlineData("large-port")]
    [InlineData("commit-newline")]
    [InlineData("secret-key")]
    [InlineData("service-role")]
    [InlineData("other-project")]
    public void InvalidOperatorConfigurationCannotStartListener(string scenario)
    {
        var options = Good();
        options = scenario switch
        {
            "public-origin" => options with { PublicOrigin = "https://public.example" },
            "origin-user" => options with { PublicOrigin = "http://user@localhost:6086" },
            "origin-path" => options with { PublicOrigin = "http://localhost:6086/path" },
            "origin-query" => options with { PublicOrigin = "http://localhost:6086/?token=private-canary" },
            "origin-fragment" => options with { PublicOrigin = "http://localhost:6086/#token" },
            "relative-root" => options with { EvidenceRoot = "relative" },
            "negative-port" => options with { Port = -1 },
            "large-port" => options with { Port = 65536 },
            "commit-newline" => options with { ReleaseCommit = new string('a', 40) + "\n" },
            "secret-key" => options with { PublishableKey = "sb_secret_configuration_canary" },
            "service-role" => options with { PublishableKey = LegacyKey("service_role", PortalOptions.ProjectRef) },
            "other-project" => options with { PublishableKey = LegacyKey("anon", "different-project") },
            _ => throw new InvalidOperationException()
        };
        var error = Assert.Throws<ArgumentException>(options.Validate);
        Assert.Equal("CONTROL_CONFIGURATION_INVALID", error.Message);
        Assert.DoesNotContain(options.PublishableKey, options.ToString());
    }

    [Theory]
    [InlineData("modern")]
    [InlineData("legacy-anon")]
    public void ValidPublicCredentialDoesNotBecomeAnActorAssertion(string scenario)
    {
        var options = Good() with { PublishableKey = scenario == "modern" ? PortalProjectClientTests.Key : LegacyKey("anon", PortalOptions.ProjectRef) };
        options.Validate();
        Assert.DoesNotContain(options.PublishableKey, JsonSerializer.Serialize(new PortalIdentity(Guid.NewGuid(), "reader@example.invalid", options.PublishableKey)));
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("scan")]
    [InlineData("zero-job")]
    [InlineData("negative-generation")]
    [InlineData("fourth-generation")]
    [InlineData("completed-zero")]
    [InlineData("completed-digest")]
    [InlineData("completed-reference")]
    [InlineData("unfinished-digest")]
    [InlineData("unfinished-reference")]
    [InlineData("unknown-state")]
    [InlineData("raw-error")]
    [InlineData("error-newline")]
    [InlineData("multiple-rows")]
    public async Task JobMetadataCannotChangeScopeOrSmuggleUnverifiedEvidence(string scenario)
    {
        var job = new FixtureJob(PortalProjectClientTests.Organization, PortalProjectClientTests.Scan, PortalProjectClientTests.Job, 1);
        var row = new Dictionary<string, object?>
        {
            ["organization_id"] = job.OrganizationId,
            ["scan_id"] = job.ScanId,
            ["id"] = job.JobId,
            ["generation"] = 1,
            ["state"] = "completed",
            ["result_digest"] = new string('a', 64),
            ["result_reference"] = job.Reference,
            ["last_error_code"] = null
        };
        switch (scenario)
        {
            case "tenant": row["organization_id"] = Guid.NewGuid(); break;
            case "scan": row["scan_id"] = Guid.NewGuid(); break;
            case "zero-job": row["id"] = Guid.Empty; break;
            case "negative-generation": row["generation"] = -1; break;
            case "fourth-generation": row["generation"] = 4; break;
            case "completed-zero": row["generation"] = 0; break;
            case "completed-digest": row["result_digest"] = "incorrect"; break;
            case "completed-reference": row["result_reference"] = "../unrelated.json"; break;
            case "unfinished-digest": row["state"] = "leased"; row["result_reference"] = null; break;
            case "unfinished-reference": row["state"] = "leased"; row["result_digest"] = null; break;
            case "unknown-state": row["state"] = "successful"; break;
            case "raw-error": row["last_error_code"] = "private raw provider message"; break;
            case "error-newline": row["last_error_code"] = "ERROR_CODE\n"; break;
        }
        var body = JsonSerializer.Serialize(scenario == "multiple-rows" ? new[] { row, row } : [row]);
        using var http = PortalProjectClientTests.Client((request, _) =>
        {
            Assert.Equal("hsr", request.Headers.GetValues("Accept-Profile").Single());
            Assert.Contains("stage=eq.observe-fixture", request.RequestUri!.Query);
            Assert.DoesNotContain("lease_token", request.RequestUri.Query);
            return Task.FromResult(PortalProjectClientTests.Response(body));
        });
        var error = await Assert.ThrowsAsync<PortalFault>(() => new SupabasePortalClient(http, PortalProjectClientTests.Key)
            .JobAsync(new(PortalProjectClientTests.Actor, "reader@example.invalid", PortalProjectClientTests.Bearer), job.OrganizationId, job.ScanId, default));
        Assert.Equal(503, error.Status); Assert.Equal("CONTROL_PROVIDER_RESPONSE_UNCONFIRMED", error.Code);
        Assert.DoesNotContain("private raw", error.ToString());
    }

    [Theory]
    [InlineData(403, "", 403, "CONTROL_WORKSPACE_ACCESS_DENIED")]
    [InlineData(400, "22023", 409, "CONTROL_REQUEST_CONFLICT")]
    [InlineData(500, "", 503, "CONTROL_DATABASE_UNAVAILABLE")]
    public async Task DatabaseDenialConflictAndOutageHaveDifferentSafeOutcomes(int upstream, string code, int expected, string safeCode)
    {
        using var http = PortalProjectClientTests.Client((_, _) => Task.FromResult(PortalProjectClientTests.Response(
            JsonSerializer.Serialize(new { code, message = "private-provider-canary" }), upstream)));
        var error = await Assert.ThrowsAsync<PortalFault>(() => new SupabasePortalClient(http, PortalProjectClientTests.Key)
            .EnsureWorkspaceAsync(new(PortalProjectClientTests.Actor, "reader@example.invalid", PortalProjectClientTests.Bearer), default));
        Assert.Equal(expected, error.Status); Assert.Equal(safeCode, error.Code);
        Assert.DoesNotContain("private-provider-canary", error.ToString());
    }

    private static PortalOptions Good() => new(PortalProjectClientTests.Key, Path.GetFullPath("artifacts/portal-validation/evidence"), new string('a', 40));
    private static string LegacyKey(string role, string project) => "eyJhbGciOiJIUzI1NiJ9." + Convert.ToBase64String(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { role, @ref = project }))).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".c2ln";
}
