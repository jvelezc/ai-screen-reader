using System.Collections.Immutable;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

public sealed class PublicWebsiteObservationTests
{
    internal static PublicWebsiteJob Job() => new(Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"), Guid.Parse("33333333-3333-3333-3333-333333333333"), 1,
        PublicReaderSource.FromUrl("https://screenreader.privensic.com/ci/v1/source"));
    internal static readonly Guid Worker = Guid.Parse("44444444-4444-4444-4444-444444444444");
    internal static PublicWebsiteObservation Observation()
    {
        var job = Job();
        var snapshot = new AccessibilitySnapshot("actual-document", [new("actual-node", "RootWebArea", "Actual exposed name")], [])
        { CaptureEpoch = 1, DocumentUrl = job.Source.TargetUrl };
        return new(PublicWebsiteObservation.CurrentSchema, job, Worker, [snapshot, snapshot],
            DateTimeOffset.Parse("2026-11-02T19:00:00Z"), true, true, 0, false);
    }

    [Theory]
    [InlineData("https://EXAMPLE.com:443", "https://example.com/", "example.com")]
    [InlineData("https://example.com/A?token=Case", "https://example.com/A?token=Case", "example.com")]
    [InlineData("https://xn--bcher-kva.example/path", "https://xn--bcher-kva.example/path", "xn--bcher-kva.example")]
    public void SourceFactoryDerivesCanonicalHttpsSourceWithoutChangingPathOrQuery(string input, string canonical, string host)
    {
        var source = PublicReaderSource.FromUrl(input);
        Assert.Equal("hsr-approved-public-readonly-v1", source.Profile); Assert.Equal(canonical, source.TargetUrl); Assert.Equal(host, source.AllowedHost);
        source.Validate();
    }

    [Theory]
    [InlineData("")]
    [InlineData("example.com")]
    [InlineData("https://example.com/ ")]
    [InlineData("http://example.com/")]
    [InlineData("https://user:password@example.com/")]
    [InlineData("https://example.com:444/")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://example.com/#fragment")]
    [InlineData("https://example.com/\\path")]
    public void SourceFactoryCannotNormalizeAnUnsupportedSubmittedUrlIntoAdmission(string input)
    {
        Assert.Equal("PUBLIC_READER_SOURCE_INVALID", Assert.Throws<BrowserOperationException>(() => PublicReaderSource.FromUrl(input)).Code);
    }

    [Theory]
    [InlineData(2048, true)]
    [InlineData(2049, false)]
    public void SourceFactoryPreservesTheExactUrlLengthCeiling(int length, bool accepted)
    {
        var target = "https://example.com/".PadRight(length, 'a');
        if (accepted) Assert.Equal(target, PublicReaderSource.FromUrl(target).TargetUrl);
        else Assert.Equal("PUBLIC_READER_SOURCE_INVALID", Assert.Throws<BrowserOperationException>(() => PublicReaderSource.FromUrl(target)).Code);
    }

    [Fact]
    public void BoundPassiveResultAcceptsOnlyItsActualProducerAndRetainsUnverifiedGoal()
    {
        var value = Observation(); value.Validate(Job(), Worker);
        Assert.Equal(2, value.Captures.Length); Assert.Equal("actual-document", value.Captures[1].DocumentId);
        Assert.Equal("Actual exposed name", value.Captures[1].Nodes[0].Name);
        Assert.Equal(0, value.DispatchedInputs); Assert.False(value.GoalVerified);
        Assert.DoesNotContain("https", Job().ToString()); Assert.DoesNotContain("privensic", Job().ToString());
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("scan")]
    [InlineData("job")]
    [InlineData("generation-zero")]
    [InlineData("generation-negative")]
    [InlineData("generation-high")]
    [InlineData("source")]
    public void JobRejectsEmptyIdentityAndUnsupportedAttemptOrSource(string scenario)
    {
        var job = Job(); job = scenario switch
        {
            "organization" => job with { OrganizationId = Guid.Empty },
            "scan" => job with { ScanId = Guid.Empty },
            "job" => job with { JobId = Guid.Empty },
            "generation-zero" => job with { Generation = 0 },
            "generation-negative" => job with { Generation = -1 },
            "generation-high" => job with { Generation = 4 },
            _ => job with { Source = null! }
        };
        Assert.Equal("PUBLIC_WEBSITE_JOB_INVALID", Assert.Throws<BrowserOperationException>(job.Validate).Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void JobAcceptsEverySupportedPassiveAttemptGeneration(int generation)
    {
        var job = Job() with { Generation = generation };
        Assert.Null(Record.Exception(job.Validate));
    }

    [Fact]
    public void JobRejectsAValidButUncanonicalDeclaredSource()
    {
        var job = Job() with { Source = new(PublicReaderSource.CurrentProfile, "https://SCREENREADER.privensic.com:443/ci/v1/source", "screenreader.privensic.com") };
        job.Source.Validate();
        Assert.Equal("PUBLIC_WEBSITE_SOURCE_NOT_CANONICAL", Assert.Throws<BrowserOperationException>(job.Validate).Code);
    }

    [Fact]
    public void EmptyExpectedProducerCannotAuthorizeAnEquallyEmptyStoredProducer()
    {
        var value = Observation() with { WorkerId = Guid.Empty };
        Assert.Equal("PUBLIC_WEBSITE_OBSERVATION_INVALID", Assert.Throws<BrowserOperationException>(() => value.Validate(Job(), Guid.Empty)).Code);
    }

    [Fact]
    public void ResultAcceptsTheExactPerCaptureNodeCeiling()
    {
        var value = Observation();
        var snapshot = value.Captures[0] with { Nodes = Enumerable.Repeat(value.Captures[0].Nodes[0], 2048).ToImmutableArray() };
        var bounded = value with { Captures = [snapshot, snapshot] };
        Assert.Null(Record.Exception(() => bounded.Validate(Job(), Worker)));
        Assert.Equal(2048, bounded.Captures[1].Nodes.Length);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("tenant")]
    [InlineData("source")]
    [InlineData("generation")]
    [InlineData("worker")]
    [InlineData("empty-worker")]
    [InlineData("received-time")]
    [InlineData("native-close")]
    [InlineData("resources")]
    [InlineData("input")]
    [InlineData("negative-input")]
    [InlineData("goal")]
    [InlineData("default-captures")]
    [InlineData("empty-captures")]
    [InlineData("one-capture")]
    [InlineData("three-captures")]
    public void ResultRejectsChangedScopeProducerInventedOutcomeAndIncompleteClosure(string scenario)
    {
        var value = Observation(); value = scenario switch
        {
            "schema" => value with { Schema = "fixture-observation" },
            "tenant" => value with { Job = value.Job with { OrganizationId = Guid.NewGuid() } },
            "source" => value with { Job = value.Job with { Source = PublicReaderSource.FromUrl("https://example.com/") } },
            "generation" => value with { Job = value.Job with { Generation = 2 } },
            "worker" => value with { WorkerId = Guid.NewGuid() },
            "empty-worker" => value with { WorkerId = Guid.Empty },
            "received-time" => value with { ReceivedAt = default },
            "native-close" => value with { NativeShutdownConfirmed = false },
            "resources" => value with { ResourcesAbsenceConfirmed = false },
            "input" => value with { DispatchedInputs = 1 },
            "negative-input" => value with { DispatchedInputs = -1 },
            "goal" => value with { GoalVerified = true },
            "default-captures" => value with { Captures = default },
            "empty-captures" => value with { Captures = [] },
            "one-capture" => value with { Captures = [value.Captures[0]] },
            _ => value with { Captures = [value.Captures[0], value.Captures[0], value.Captures[0]] }
        };
        Assert.Equal("PUBLIC_WEBSITE_OBSERVATION_INVALID", Assert.Throws<BrowserOperationException>(() => value.Validate(Job(), Worker)).Code);
    }

    [Theory]
    [InlineData("document")]
    [InlineData("empty-document")]
    [InlineData("epoch")]
    [InlineData("url")]
    [InlineData("null-snapshot")]
    [InlineData("default-nodes")]
    [InlineData("empty-nodes")]
    [InlineData("too-many-nodes")]
    public void ResultRequiresTwoBoundedSameDocumentPublicCaptures(string scenario)
    {
        var value = Observation(); var last = value.Captures[1];
        last = scenario switch
        {
            "document" => last with { DocumentId = "other-document" },
            "empty-document" => last with { DocumentId = "" },
            "epoch" => last with { CaptureEpoch = 2 },
            "url" => last with { DocumentUrl = "https://foreign.example/" },
            "null-snapshot" => null!,
            "default-nodes" => last with { Nodes = default },
            "empty-nodes" => last with { Nodes = [] },
            _ => last with { Nodes = Enumerable.Repeat(last.Nodes[0], 2049).ToImmutableArray() }
        };
        Assert.Equal("PUBLIC_WEBSITE_OBSERVATION_INVALID", Assert.Throws<BrowserOperationException>(() =>
            (value with { Captures = [value.Captures[0], last] }).Validate(Job(), Worker)).Code);
    }
}
