using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class ReaderWorkerWireTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    public async Task FrameRoundTripsExactBytesAndCleanEof(int length)
    {
        var expected = Enumerable.Range(0, length).Select(i => (byte)i).ToArray();
        using var stream = new MemoryStream();
        await ReaderWorkerWire.WriteAsync(stream, expected, 32);
        Assert.Equal(length, BinaryPrimitives.ReadInt32BigEndian(stream.ToArray().AsSpan(0, 4)));
        stream.Position = 0;
        Assert.Equal(expected, await ReaderWorkerWire.ReadAsync(stream, 32));
        Assert.Null(await ReaderWorkerWire.ReadAsync(stream, 32));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(33)]
    [InlineData(int.MaxValue)]
    public async Task InvalidDeclaredLengthIsRejectedBeforeReadingPayload(int length)
    {
        var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, length);
        using var stream = new MemoryStream(header);
        Assert.Equal("READER_WORKER_FRAME_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(
            () => ReaderWorkerWire.ReadAsync(stream, 32))).Code);
        Assert.Equal(4, stream.Position);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public async Task PartialHeaderOrPayloadIsNeverACompleteFrame(int length)
    {
        using var complete = new MemoryStream();
        await ReaderWorkerWire.WriteAsync(complete, [1, 2, 3], 32);
        using var partial = new MemoryStream(complete.ToArray()[..length]);
        await Assert.ThrowsAsync<EndOfStreamException>(() => ReaderWorkerWire.ReadAsync(partial, 32));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(33)]
    public async Task RejectedWriteEmitsNoPartialHeader(int length)
    {
        using var output = new MemoryStream();
        Assert.Equal("READER_WORKER_FRAME_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(
            () => ReaderWorkerWire.WriteAsync(output, new byte[length], 32))).Code);
        Assert.Empty(output.ToArray());
    }

    [Theory]
    [InlineData("unknown-url")]
    [InlineData("unknown-key")]
    [InlineData("duplicate")]
    [InlineData("numeric-operation")]
    [InlineData("unknown-operation")]
    [InlineData("missing-operation")]
    [InlineData("null")]
    [InlineData("invalid-json")]
    public async Task RequestDecoderRejectsExtensionsDuplicatesCoercionAndMissingFields(string scenario)
    {
        var valid = JsonSerializer.Serialize(ReaderWorkerContractTests.Request(), ReaderWorkerWire.Json);
        var payload = scenario switch
        {
            "unknown-url" => valid[..^1] + ",\"Url\":\"https://example.com\"}",
            "unknown-key" => valid[..^1] + ",\"Key\":\"Tab\"}",
            "duplicate" => valid[..^1] + ",\"Sequence\":1}",
            "numeric-operation" => valid.Replace("\"Open\"", "0", StringComparison.Ordinal),
            "unknown-operation" => valid.Replace("\"Open\"", "\"Navigate\"", StringComparison.Ordinal),
            "missing-operation" => valid.Replace(",\"Operation\":\"Open\"", "", StringComparison.Ordinal),
            "null" => "null",
            _ => "{"
        };
        using var input = new MemoryStream();
        await ReaderWorkerWire.WriteAsync(input, Encoding.UTF8.GetBytes(payload), 1024); input.Position = 0;
        if (scenario == "null") Assert.Equal("READER_WORKER_REQUEST_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(
            () => ReaderWorkerWire.ReadRequestAsync(input))).Code);
        else await Assert.ThrowsAsync<JsonException>(() => ReaderWorkerWire.ReadRequestAsync(input));
    }

    [Fact]
    public async Task DecoderEnforcesRequestLimitAndDisconnect()
    {
        var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, 1025);
        using var oversized = new MemoryStream(header);
        Assert.Equal("READER_WORKER_FRAME_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(
            () => ReaderWorkerWire.ReadRequestAsync(oversized))).Code);
        using var empty = new MemoryStream();
        Assert.Equal("READER_WORKER_DISCONNECTED", (await Assert.ThrowsAsync<BrowserOperationException>(
            () => ReaderWorkerWire.ReadRequestAsync(empty))).Code);
    }

    [Fact]
    public async Task ResponseDecoderBindsExpectedSessionSequenceAndDocument()
    {
        var request = ReaderWorkerContractTests.Request(); var expected = ReaderWorkerContractTests.OpenResponse(request);
        using var input = new MemoryStream(); await ReaderWorkerWire.WriteResponseAsync(input, expected);
        input.Position = 0;
        var response = await ReaderWorkerWire.ReadResponseAsync(input, request, "owned-document");
        Assert.Equal(ReaderWorkerState.Open, response.State);
        Assert.Equal("Accessibility fixture", Assert.Single(response.Snapshot!.Nodes).Name);
        input.Position = 0;
        Assert.Equal("READER_WORKER_RESPONSE_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(
            () => ReaderWorkerWire.ReadResponseAsync(input, request with { SessionId = Guid.NewGuid() }))).Code);
        input.Position = 0;
        Assert.Equal("READER_WORKER_RESPONSE_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(
            () => ReaderWorkerWire.ReadResponseAsync(input, request, "other-document"))).Code);
        input.Position = 0;
        Assert.Equal("READER_WORKER_RESPONSE_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(
            () => ReaderWorkerWire.ReadResponseAsync(input, request with { Sequence = 2, Operation = ReaderWorkerOperation.Observe }))).Code);
    }

    [Theory]
    [InlineData("unknown-field")]
    [InlineData("duplicate")]
    [InlineData("null")]
    [InlineData("oversized")]
    [InlineData("eof")]
    public async Task ResponseDecoderRejectsUntrustedFramesAndIncompleteContracts(string scenario)
    {
        var request = ReaderWorkerContractTests.Request();
        var json = JsonSerializer.Serialize(ReaderWorkerContractTests.OpenResponse(request), ReaderWorkerWire.Json);
        using var input = new MemoryStream();
        if (scenario == "oversized")
        {
            var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, 262145); await input.WriteAsync(header);
        }
        else if (scenario != "eof")
        {
            var content = scenario switch
            {
                "unknown-field" => json[..^1] + ",\"Token\":\"untrusted-token-canary\"}",
                "duplicate" => json[..^1] + ",\"GoalVerified\":false}",
                _ => "null"
            };
            await ReaderWorkerWire.WriteAsync(input, Encoding.UTF8.GetBytes(content), 262144);
        }
        input.Position = 0;
        if (scenario is "unknown-field" or "duplicate")
            await Assert.ThrowsAsync<JsonException>(() => ReaderWorkerWire.ReadResponseAsync(input, request));
        else
        {
            var expected = scenario == "eof" ? "READER_WORKER_DISCONNECTED"
                : scenario == "oversized" ? "READER_WORKER_FRAME_INVALID" : "READER_WORKER_RESPONSE_INVALID";
            Assert.Equal(expected, (await Assert.ThrowsAsync<BrowserOperationException>(
                () => ReaderWorkerWire.ReadResponseAsync(input, request))).Code);
        }
    }

    [Fact]
    public async Task OversizedResponseEmitsNoBytes()
    {
        var response = ReaderWorkerContractTests.OpenResponse(ReaderWorkerContractTests.Request());
        response = response with { DocumentId = new string('x', 262144) };
        using var output = new MemoryStream();
        Assert.Equal("READER_WORKER_FRAME_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(
            () => ReaderWorkerWire.WriteResponseAsync(output, response))).Code);
        Assert.Empty(output.ToArray());
    }
}
