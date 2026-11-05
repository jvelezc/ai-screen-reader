using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Public-source protocol on the existing bounded, strict frame transport.</summary>
public static class PublicReaderWorkerWire
{
    public static async Task<PublicReaderWorkerRequest> ReadRequestAsync(Stream input, CancellationToken token = default)
    {
        var bytes = await ReaderWorkerWire.ReadAsync(input, ReaderWorkerRequest.MaximumRequestBytes, token).ConfigureAwait(false)
            ?? throw new BrowserOperationException("PUBLIC_READER_WORKER_DISCONNECTED");
        var request = JsonSerializer.Deserialize<PublicReaderWorkerRequest>(bytes, ReaderWorkerWire.Json)
            ?? throw new BrowserOperationException("PUBLIC_READER_WORKER_REQUEST_INVALID");
        request.Validate();
        return request;
    }

    public static Task WriteRequestAsync(Stream output, PublicReaderWorkerRequest request, CancellationToken token = default)
    {
        request.Validate();
        return ReaderWorkerWire.WriteAsync(output, JsonSerializer.SerializeToUtf8Bytes(request, ReaderWorkerWire.Json),
            ReaderWorkerRequest.MaximumRequestBytes, token);
    }

    public static Task WriteResponseAsync(Stream output, PublicReaderWorkerResponse response, CancellationToken token = default) =>
        ReaderWorkerWire.WriteAsync(output, JsonSerializer.SerializeToUtf8Bytes(response, ReaderWorkerWire.Json),
            PublicReaderWorkerResponse.MaximumResponseBytes, token);

    public static async Task<PublicReaderWorkerResponse> ReadResponseAsync(Stream input, PublicReaderWorkerRequest expected,
        PublicReaderSource source, string? expectedDocument = null, CancellationToken token = default)
    {
        expected.Validate(); source.Validate();
        var bytes = await ReaderWorkerWire.ReadAsync(input, PublicReaderWorkerResponse.MaximumResponseBytes, token).ConfigureAwait(false)
            ?? throw new BrowserOperationException("PUBLIC_READER_WORKER_DISCONNECTED");
        var response = JsonSerializer.Deserialize<PublicReaderWorkerResponse>(bytes, ReaderWorkerWire.Json)
            ?? throw new BrowserOperationException("PUBLIC_READER_WORKER_RESPONSE_INVALID");
        response.Validate(expected, source, expectedDocument);
        return response;
    }
}
