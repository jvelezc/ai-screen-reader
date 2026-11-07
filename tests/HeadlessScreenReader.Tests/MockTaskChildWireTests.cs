using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed partial class MockTaskWorkerTests
{
    [Theory]
    [InlineData("extra-url")]
    [InlineData("duplicate-schema")]
    [InlineData("missing-worker")]
    [InlineData("bad-generation")]
    [InlineData("big-frame")]
    public async Task ChildRejectsMalformedOrOversizedStartBeforeAnyAuthorityRequest(string mutation)
    {
        var json = JsonSerializer.SerializeToNode(Start(), ReaderWorkerWire.Json)!.AsObject();
        if (mutation == "extra-url") json["Url"] = "https://example.com";
        if (mutation == "missing-worker") json.Remove("WorkerId");
        if (mutation == "bad-generation") json["Job"]!["Generation"] = 2;
        var text = json.ToJsonString();
        if (mutation == "duplicate-schema") text = text[..^1] + ",\"Schema\":\"hsr-mock-task-control-v1\"}";
        using var input = new MemoryStream(); using var output = new MemoryStream(); using var progress = new MemoryStream();
        if (mutation == "big-frame") { var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, 4097); input.Write(header); }
        else await ReaderWorkerWire.WriteAsync(input, Encoding.UTF8.GetBytes(text), 4096);
        input.Position = 0;
        Assert.Equal(3, await MockTaskChildWorker.RunAsync(input, output, progress));
        Assert.Equal(0, output.Length); Assert.Equal(0, progress.Length);
    }

    [Fact]
    public async Task AnEarlierOpenAcknowledgementCannotAuthorizeTheNextKeyboardInput()
    {
        var start = Start();
        var first = new MockTaskIntent(MockTaskControl.Schema, start, 1, MockTaskControl.Boundaries[0]);
        var ack = new MockTaskAck(MockTaskControl.Schema, start.ExecutionId, 1, TraversalArchive.Hash(JsonSerializer.SerializeToUtf8Bytes(first, ReaderWorkerWire.Json)));
        using var input = new MemoryStream(); using var output = new MemoryStream(); using var progress = new MemoryStream();
        await ReaderWorkerWire.WriteAsync(input, JsonSerializer.SerializeToUtf8Bytes(start, ReaderWorkerWire.Json), 4096);
        for (var i = 0; i < 2; i++) await ReaderWorkerWire.WriteAsync(input, JsonSerializer.SerializeToUtf8Bytes(ack, ReaderWorkerWire.Json), 4096);
        input.Position = 0;
        Assert.Equal(3, await MockTaskChildWorker.RunAsync(input, output, progress)); Assert.Equal(0, output.Length);
        progress.Position = 0;
        var open = JsonSerializer.Deserialize<MockTaskIntent>((await ReaderWorkerWire.ReadAsync(progress, 4096))!, ReaderWorkerWire.Json)!;
        var tab = JsonSerializer.Deserialize<MockTaskIntent>((await ReaderWorkerWire.ReadAsync(progress, 4096))!, ReaderWorkerWire.Json)!;
        Assert.Equal(new MockTaskBoundary(1, "Open"), open.Boundary); Assert.Equal(1, open.Sequence);
        Assert.Equal(new MockTaskBoundary(1, "Tab"), tab.Boundary); Assert.Equal(2, tab.Sequence);
        Assert.Null(await ReaderWorkerWire.ReadAsync(progress, 4096));
    }
}
