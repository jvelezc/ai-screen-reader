using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Read-only access to a protected descriptor and a fixed loopback protocol. No native execution or journal writes.</summary>
public sealed class ReaderHostClient(string root, string image)
{
    public ReaderHostEndpoint Endpoint() => ReaderHostEndpoint.Read(root, image);

    public async Task<bool> ReadyAsync()
    {
        try
        {
            var endpoint = Endpoint();
            var response = await ExchangeAsync(endpoint, ReaderHostOperation.Ready, Guid.Empty, Guid.Empty, Guid.Empty,
                timeout: TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            return response.ErrorCode is null;
        }
        catch { return false; }
    }

    public async Task<ReaderHostResponse> ExchangeAsync(ReaderHostEndpoint endpoint, ReaderHostOperation operation,
        Guid subject, Guid organization, Guid session, string? document = null, TimeSpan? timeout = null)
    {
        endpoint.Validate(image);
        using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(45));
        using var client = new TcpClient(AddressFamily.InterNetwork);
        var request = new ReaderHostRequest(ReaderHostEndpoint.CurrentSchema, endpoint.Instance, Guid.NewGuid(),
            endpoint.Capability, operation, subject, organization, session);
        request.Validate(endpoint);
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, endpoint.Port, deadline.Token).ConfigureAwait(false);
            var stream = client.GetStream();
            await ReaderWorkerWire.WriteAsync(stream, JsonSerializer.SerializeToUtf8Bytes(request, ReaderWorkerWire.Json),
                ReaderWorkerRequest.MaximumRequestBytes, deadline.Token).ConfigureAwait(false);
            var bytes = await ReaderWorkerWire.ReadAsync(stream, ReaderWorkerRequest.MaximumResponseBytes, deadline.Token).ConfigureAwait(false)
                ?? throw new IOException();
            var response = JsonSerializer.Deserialize<ReaderHostResponse>(bytes, ReaderWorkerWire.Json) ?? throw new IOException();
            response.Validate(request, document);
            if (await stream.ReadAsync(new byte[1], deadline.Token).ConfigureAwait(false) != 0) throw new IOException();
            return response;
        }
        catch { throw new BrowserOperationException("READER_HOST_EXCHANGE_UNCONFIRMED"); }
    }
}
