using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class PublicReaderWorkerWireTests
{
    private static async Task<MemoryStream> Frame(string text)
    {
        var stream = new MemoryStream(); await ReaderWorkerWire.WriteAsync(stream, Encoding.UTF8.GetBytes(text), ReaderWorkerRequest.MaximumResponseBytes);
        stream.Position = 0; return stream;
    }

    [Fact]
    public async Task RoundTripBindsPublicSourceWithoutFixtureDigest()
    {
        var request = PublicReaderWorkerContractTests.Request();
        using var input = new MemoryStream(); await PublicReaderWorkerWire.WriteRequestAsync(input, request); input.Position = 0;
        Assert.Equal(request, await PublicReaderWorkerWire.ReadRequestAsync(input));
        using var output = new MemoryStream(); await PublicReaderWorkerWire.WriteResponseAsync(output, PublicReaderWorkerContractTests.Response());
        var text = Encoding.UTF8.GetString(output.ToArray()[4..]);
        Assert.Contains("hsr-public-reader-worker-v1", text); Assert.Contains("hsr-approved-public-readonly-v1", text);
        Assert.DoesNotContain("FixtureSha256", text);
        output.Position = 0; var response = await PublicReaderWorkerWire.ReadResponseAsync(output, request, PublicReaderWorkerContractTests.Source());
        Assert.Equal("public-document", response.DocumentId); Assert.Equal("https://screenreader.privensic.com/ci/v1/source", response.Snapshot!.DocumentUrl);
        Assert.Equal(0, response.DispatchedInputs); Assert.False(response.GoalVerified);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("numeric")]
    public async Task RequestRejectsUnconfirmedJsonFields(string scenario)
    {
        var json = JsonSerializer.Serialize(PublicReaderWorkerContractTests.Request(), ReaderWorkerWire.Json);
        json = scenario switch
        {
            "missing" => json.Replace("\"Sequence\":1,", "", StringComparison.Ordinal),
            "null" => json.Replace("\"Schema\":\"hsr-public-reader-worker-v1\"", "\"Schema\":null", StringComparison.Ordinal),
            "unknown" => json.Replace("{", "{\"Credentials\":\"must-not-be-accepted\",", StringComparison.Ordinal),
            "duplicate" => json.Replace("{", "{\"Sequence\":1,", StringComparison.Ordinal),
            _ => json.Replace("\"Operation\":\"Open\"", "\"Operation\":0", StringComparison.Ordinal)
        };
        using var input = await Frame(json); await Assert.ThrowsAsync<JsonException>(() => PublicReaderWorkerWire.ReadRequestAsync(input));
    }

    [Fact]
    public async Task RequestAcceptsExactFrameBoundButRejectsNextByteBeforePayload()
    {
        var json = JsonSerializer.Serialize(PublicReaderWorkerContractTests.Request(), ReaderWorkerWire.Json);
        using var maximum = await Frame(json.PadRight(1024)); Assert.Equal(1, (await PublicReaderWorkerWire.ReadRequestAsync(maximum)).Sequence);
        using var oversized = new MemoryStream([0, 0, 4, 1]);
        Assert.Equal("READER_WORKER_FRAME_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() => PublicReaderWorkerWire.ReadRequestAsync(oversized))).Code);
    }

    [Fact]
    public async Task PartialFramesAndEmptyStreamCannotBecomeResponses()
    {
        using var partial = new MemoryStream([0, 0]); await Assert.ThrowsAsync<EndOfStreamException>(() => PublicReaderWorkerWire.ReadRequestAsync(partial));
        using var empty = new MemoryStream(); Assert.Equal("PUBLIC_READER_WORKER_DISCONNECTED", (await Assert.ThrowsAsync<BrowserOperationException>(() => PublicReaderWorkerWire.ReadResponseAsync(empty, PublicReaderWorkerContractTests.Request(), PublicReaderWorkerContractTests.Source()))).Code);
    }

    [Fact]
    public async Task FoundationAndPublicFramesCannotCrossValidate()
    {
        var legacy = ReaderWorkerContractTests.Request(); var current = PublicReaderWorkerContractTests.Request();
        using var legacyRequest = await Frame(JsonSerializer.Serialize(legacy, ReaderWorkerWire.Json));
        Assert.Equal("PUBLIC_READER_WORKER_REQUEST_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() => PublicReaderWorkerWire.ReadRequestAsync(legacyRequest))).Code);
        using var publicRequest = await Frame(JsonSerializer.Serialize(current, ReaderWorkerWire.Json));
        Assert.Equal("READER_WORKER_REQUEST_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() => ReaderWorkerWire.ReadRequestAsync(publicRequest))).Code);
        using var legacyResponse = await Frame(JsonSerializer.Serialize(ReaderWorkerContractTests.OpenResponse(legacy), ReaderWorkerWire.Json));
        await Assert.ThrowsAsync<JsonException>(() => PublicReaderWorkerWire.ReadResponseAsync(legacyResponse, current, PublicReaderWorkerContractTests.Source()));
        using var publicResponse = await Frame(JsonSerializer.Serialize(PublicReaderWorkerContractTests.Response(), ReaderWorkerWire.Json));
        await Assert.ThrowsAsync<JsonException>(() => ReaderWorkerWire.ReadResponseAsync(publicResponse, legacy));
    }
}
