using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class ReaderHostProtocolTests
{
    internal const string Image = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal static ReaderHostEndpoint Endpoint() => new(ReaderHostEndpoint.CurrentSchema, Guid.NewGuid(), 12345, Image,
        Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()));
    internal static ReaderHostRequest Request(ReaderHostEndpoint endpoint, ReaderHostOperation operation = ReaderHostOperation.Observe) =>
        new(endpoint.Schema, endpoint.Instance, Guid.NewGuid(), endpoint.Capability, operation, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    private static AccessibilitySnapshot Snapshot() => new("document", [new("node", "button", "Fixture action")], []) { CaptureEpoch = 1 };
    private static ReaderHostResponse Response(ReaderHostRequest request) => new(request.Schema, request.Instance, request.RequestId,
        request.Operation, request.Subject, request.Organization, request.Session, null, "document", Snapshot(), null, 0, false);

    [Theory]
    [InlineData("schema")]
    [InlineData("instance")]
    [InlineData("zero-port")]
    [InlineData("port")]
    [InlineData("mutable-image")]
    [InlineData("capability-size")]
    [InlineData("capability-format")]
    public void EndpointRejectsUnboundedOrUnconfirmedAuthority(string scenario)
    {
        var endpoint = Endpoint();
        endpoint = scenario switch
        {
            "schema" => endpoint with { Schema = "other" },
            "instance" => endpoint with { Instance = Guid.Empty },
            "zero-port" => endpoint with { Port = 0 },
            "port" => endpoint with { Port = 65536 },
            "mutable-image" => endpoint with { Image = "image:latest" },
            "capability-size" => endpoint with { Capability = Convert.ToBase64String(new byte[31]) },
            _ => endpoint with { Capability = new string('!', 44) }
        };
        Assert.Equal("READER_HOST_IDENTITY_UNCONFIRMED", Assert.Throws<BrowserOperationException>(() => endpoint.Validate()).Code);
    }

    [Fact]
    public void EndpointCannotSubstituteAnotherReleaseOrPrintItsCapability()
    {
        var endpoint = Endpoint(); endpoint.Validate(Image);
        Assert.Equal("READER_HOST_IDENTITY_UNCONFIRMED", Assert.Throws<BrowserOperationException>(() => endpoint.Validate("sha256:" + new string('b', 64))).Code);
        Assert.DoesNotContain(endpoint.Capability, endpoint.ToString());
        Assert.DoesNotContain(endpoint.Capability, Request(endpoint).ToString());
    }

    [Theory]
    [InlineData("instance")]
    [InlineData("request")]
    [InlineData("authority")]
    [InlineData("operation")]
    [InlineData("subject")]
    [InlineData("organization")]
    [InlineData("session")]
    [InlineData("ready-scope")]
    public void RequestRejectsWrongAuthorityCorrelationOrMissingCallerScope(string scenario)
    {
        var endpoint = Endpoint(); var request = Request(endpoint);
        request = scenario switch
        {
            "instance" => request with { Instance = Guid.NewGuid() },
            "request" => request with { RequestId = Guid.Empty },
            "authority" => request with { Capability = Convert.ToBase64String(new byte[32]) },
            "operation" => request with { Operation = (ReaderHostOperation)100 },
            "subject" => request with { Subject = Guid.Empty },
            "organization" => request with { Organization = Guid.Empty },
            "session" => request with { Session = Guid.Empty },
            _ => request with { Operation = ReaderHostOperation.Ready }
        };
        Assert.Equal("READER_HOST_REQUEST_DENIED", Assert.Throws<BrowserOperationException>(() => request.Validate(endpoint)).Code);
    }

    [Theory]
    [InlineData("instance")]
    [InlineData("request")]
    [InlineData("operation")]
    [InlineData("subject")]
    [InlineData("organization")]
    [InlineData("session")]
    [InlineData("document")]
    [InlineData("epoch")]
    [InlineData("empty")]
    [InlineData("input")]
    [InlineData("goal")]
    [InlineData("retirement")]
    public void ResponseCannotCrossScopeOrInventAFreshObservation(string scenario)
    {
        var request = Request(Endpoint()); var response = Response(request);
        response = scenario switch
        {
            "instance" => response with { Instance = Guid.NewGuid() },
            "request" => response with { RequestId = Guid.NewGuid() },
            "operation" => response with { Operation = ReaderHostOperation.Open },
            "subject" => response with { Subject = Guid.NewGuid() },
            "organization" => response with { Organization = Guid.NewGuid() },
            "session" => response with { Session = Guid.NewGuid() },
            "document" => response with { DocumentId = "substituted-document" },
            "epoch" => response with { Snapshot = Snapshot() with { CaptureEpoch = 2 } },
            "empty" => response with { Snapshot = Snapshot() with { Nodes = [] } },
            "input" => response with { DispatchedInputs = 1 },
            "goal" => response with { GoalVerified = true },
            _ => response with { Retirement = new(true, true) }
        };
        Assert.Equal("READER_HOST_RESPONSE_UNCONFIRMED", Assert.Throws<BrowserOperationException>(() => response.Validate(request, "document")).Code);
    }

    [Fact]
    public void NativeAcknowledgementAndPhysicalAbsenceRemainIndependentCloseFacts()
    {
        var request = Request(Endpoint(), ReaderHostOperation.Retire);
        var response = Response(request) with { DocumentId = null, Snapshot = null, Retirement = new(false, true) };
        response.Validate(request);
        Assert.False(response.Retirement!.NativeShutdownConfirmed); Assert.True(response.Retirement.ContainerAbsenceConfirmed);
        var unconfirmed = response with { Retirement = new(true, false) };
        Assert.Equal("READER_HOST_RESPONSE_UNCONFIRMED", Assert.Throws<BrowserOperationException>(() => unconfirmed.Validate(request)).Code);
        var error = response with { Retirement = null, ErrorCode = "READER_HOST_SESSION_NOT_FOUND" }; error.Validate(request);
        Assert.Throws<BrowserOperationException>(() => (error with { Snapshot = Snapshot() }).Validate(request));
        Assert.Throws<BrowserOperationException>(() => (error with { ErrorCode = "private-error-detail" }).Validate(request));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    public void WireRejectsUnknownDuplicateOrMissingFieldsInsteadOfGuessingAuthority(string scenario)
    {
        var serialized = JsonSerializer.Serialize(Endpoint(), ReaderWorkerWire.Json);
        serialized = scenario switch
        {
            "unknown" => serialized[..^1] + ",\"Extra\":true}",
            "duplicate" => serialized[..^1] + ",\"Port\":12345}",
            _ => "{}"
        };
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReaderHostEndpoint>(serialized, ReaderWorkerWire.Json));
    }
}
