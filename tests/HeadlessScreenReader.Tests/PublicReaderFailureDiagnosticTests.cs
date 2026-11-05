using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class PublicReaderFailureDiagnosticTests
{
    private static readonly Guid Session = Guid.Parse("a88820e8-2911-49fe-a61c-df52df30308a");
    private static PublicReaderFailureDiagnostic Valid() => PublicReaderFailureDiagnostic.Create("Response",
        new BrowserOperationException("READER_WORKER_FRAME_INVALID"), Session, 1, 712, 1250);

    [Fact]
    public async Task PublicConstructorCannotBypassTheLogAllowlist()
    {
        using var writer = new StringWriter();
        await (Valid() with { Cause = "secret-not-allowed" }).WriteAsync(writer);
        await (Valid() with { PayloadBytes = -1 }).WriteAsync(writer);
        Assert.Equal("", writer.ToString());
    }

    [Fact]
    public async Task KnownFailureKeepsStageCountsAndSessionWithoutExceptionContent()
    {
        var failure = new BrowserOperationException("TARGET_HTTP_ERROR", new InvalidOperationException("bearer-private-do-not-log https://private.example/?secret=value"));
        failure.Data["HttpStatusCode"] = 403; failure.Data["payload"] = "page-text-do-not-log";
        var value = PublicReaderFailureDiagnostic.Create("Navigation", failure, Session, 1, null, 1290);
        using var output = new StringWriter(); await value.WriteAsync(output);
        var text = output.ToString();
        Assert.DoesNotContain("bearer-private", text); Assert.DoesNotContain("private.example", text);
        Assert.DoesNotContain("page-text", text); Assert.DoesNotContain("payload", text);
        using var document = JsonDocument.Parse(text);
        Assert.Equal(new[] { "Schema", "Stage", "Cause", "SessionId", "Sequence", "ObservedNodes", "HttpStatusCode", "PayloadBytes", "MaximumBytes", "ElapsedMilliseconds" },
            document.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Navigation", document.RootElement.GetProperty("Stage").GetString());
        Assert.Equal("TARGET_HTTP_ERROR", document.RootElement.GetProperty("Cause").GetString());
        Assert.Equal(403, document.RootElement.GetProperty("HttpStatusCode").GetInt32());
        Assert.Equal(Session.ToString("D"), document.RootElement.GetProperty("SessionId").GetString());
    }

    [Theory]
    [InlineData("untrusted-code")]
    [InlineData("exception-message")]
    [InlineData("untrusted-stage")]
    public async Task UntrustedDiagnosticStringsCannotForgeOrLeakAnEvent(string scenario)
    {
        const string secret = "secret-do-not-log\nforged-success=true";
        Exception error = scenario == "exception-message" ? new IOException(secret) : new BrowserOperationException(secret);
        var value = PublicReaderFailureDiagnostic.Create(scenario == "untrusted-stage" ? secret : "Capture", error, Session, 1, 12, 2);
        using var output = new StringWriter(); await value.WriteAsync(output);
        Assert.DoesNotContain("secret-do-not-log", output.ToString()); Assert.DoesNotContain("forged-success", output.ToString());
        Assert.Equal(scenario == "exception-message" ? "TRANSPORT_FAILED" : "UNCONFIRMED", value.Cause);
        Assert.Equal(scenario == "untrusted-stage" ? "Unknown" : "Capture", value.Stage);
        Assert.Single(output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task BoundFailureRoundTripPreservesOnlyTypedOperationalFacts()
    {
        using var output = new StringWriter(); await Valid().WriteAsync(output);
        output.WriteLine("PUBLIC_READER_WORKER_UNCONFIRMED");
        var read = PublicReaderFailureDiagnostic.Read(Encoding.UTF8.GetBytes(output.ToString()), Session);
        Assert.Equal(Valid(), read); Assert.Equal(712, read!.ObservedNodes); Assert.Null(read.HttpStatusCode);
    }

    [Theory]
    [InlineData("foreign-session")]
    [InlineData("unknown-field")]
    [InlineData("unknown-code")]
    [InlineData("unknown-stage")]
    [InlineData("wrong-schema")]
    [InlineData("negative-count")]
    [InlineData("bad-http-status")]
    [InlineData("zero-sequence")]
    [InlineData("extra-output")]
    [InlineData("missing-footer")]
    [InlineData("oversized")]
    [InlineData("bad-utf8")]
    public void ForeignMalformedOrUnboundedFailureCannotBecomeTrustedDiagnostics(string scenario)
    {
        var node = JsonSerializer.SerializeToNode(Valid(), ReaderWorkerWire.Json)!.AsObject();
        switch (scenario)
        {
            case "foreign-session": node["SessionId"] = Guid.NewGuid(); break;
            case "unknown-field": node["PrivatePayload"] = "not-allowed"; break;
            case "unknown-code": node["Cause"] = "secret-do-not-log"; break;
            case "unknown-stage": node["Stage"] = "forged-stage"; break;
            case "wrong-schema": node["Schema"] = "different"; break;
            case "negative-count": node["ObservedNodes"] = -1; break;
            case "bad-http-status": node["HttpStatusCode"] = 200; break;
            case "zero-sequence": node["Sequence"] = 0; break;
        }
        var text = node.ToJsonString() + "\nPUBLIC_READER_WORKER_UNCONFIRMED\n";
        if (scenario == "extra-output") text += "additional output\n";
        if (scenario == "missing-footer") text = node.ToJsonString();
        if (scenario == "oversized") text = new string('x', 16385);
        var bytes = scenario == "bad-utf8" ? new byte[] { 0xff } : Encoding.UTF8.GetBytes(text);
        Assert.Null(PublicReaderFailureDiagnostic.Read(bytes, Session));
    }

    [Fact]
    public void NumericMetadataIsBoundedAndUnknownStatusIsNotInvented()
    {
        var failure = new BrowserOperationException("TARGET_HTTP_ERROR"); failure.Data["HttpStatusCode"] = "secret";
        var value = PublicReaderFailureDiagnostic.Create("Navigation", failure, Session, int.MaxValue, int.MaxValue, double.NaN);
        Assert.Null(value.HttpStatusCode); Assert.Null(value.ObservedNodes); Assert.Equal(0, value.ElapsedMilliseconds);
        Assert.Equal(ReaderWorkerRequest.MaximumCaptures + 1, value.Sequence);
    }

    [Fact]
    public async Task FailedLogWriterDoesNotReplaceTheOriginalFailure()
    {
        var value = Valid(); await value.WriteAsync(new BrokenWriter());
        Assert.Equal("READER_WORKER_FRAME_INVALID", value.Cause);
    }

    private sealed class BrokenWriter : StringWriter
    {
        public override Task WriteLineAsync(string? value) => Task.FromException(new IOException("private-sink-detail"));
    }
}
