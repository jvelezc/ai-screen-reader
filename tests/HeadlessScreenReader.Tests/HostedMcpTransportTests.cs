using System.Net;
using System.Net.Sockets;
using System.Text;
using HeadlessScreenReader.Mcp;

namespace HeadlessScreenReader.Tests;

public sealed partial class HostedMcpTests
{
    [Fact]
    public async Task BodyReadHasOwnDeadlineAfterHeadersComplete()
    {
        var body = new BlockedBody();
        var handler = Queue(() => new(System.Net.HttpStatusCode.Created) { Content = new StreamContent(body) });
        await using var client = new HostedReaderClient(Options(), handler);
        var reply = await client.OpenAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal("HOSTED_OPERATION_UNCONFIRMED", reply.ErrorCode);
        Assert.True(body.ReadCancelled); Assert.False(client.CleanupConfirmed); Assert.Single(handler.Requests);
    }

    private sealed class BlockedBody : Stream
    {
        public bool ReadCancelled { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { ReadCancelled = true; throw; }
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task RealHttpRedirectNeverForwardsOriginalBearerToDestination()
    {
        using var origin = new TcpListener(IPAddress.Loopback, 0); origin.Start();
        using var destination = new TcpListener(IPAddress.Loopback, 0); destination.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var originPort = ((IPEndPoint)origin.LocalEndpoint).Port; var destinationPort = ((IPEndPoint)destination.LocalEndpoint).Port;
        var receipt = Serve();
        async Task<List<string>> Serve()
        {
            using var socket = await origin.AcceptTcpClientAsync(deadline.Token);
            await using var stream = socket.GetStream(); using var reader = new StreamReader(stream, leaveOpen: true);
            var lines = new List<string>();
            while (await reader.ReadLineAsync(deadline.Token) is { Length: > 0 } line) lines.Add(line);
            var response = Encoding.ASCII.GetBytes($"HTTP/1.1 307 Temporary Redirect\r\nLocation: http://127.0.0.1:{destinationPort}/trap\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response, deadline.Token); return lines;
        }
        var options = new HostedReaderOptions(new($"http://127.0.0.1:{originPort}/"), Organization, "original-caller-canary");
        await using var client = new HostedReaderClient(options);
        var reply = await client.OpenAsync(); Assert.Equal("HOSTED_REDIRECT_DENIED", reply.ErrorCode);
        var request = await receipt; Assert.Contains("Authorization: Bearer original-caller-canary", request);
        Assert.StartsWith($"POST /api/workspaces/{Organization:D}/reader-sessions HTTP/1.1", request[0]);
        Assert.False(destination.Pending()); Assert.DoesNotContain("original-caller-canary", reply.ToJson()); Assert.False(client.CleanupConfirmed);
    }
}
