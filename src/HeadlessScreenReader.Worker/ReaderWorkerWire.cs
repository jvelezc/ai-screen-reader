using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Length-prefixed UTF-8 frames. A declared size is checked before allocating its payload.</summary>
public static class ReaderWorkerWire
{
    public static JsonSerializerOptions Json { get; } = CreateJson();

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            RespectNullableAnnotations = true,
            AllowDuplicateProperties = false,
            Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    public static async Task<byte[]?> ReadAsync(Stream input, int maximumBytes, CancellationToken token = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        var header = new byte[4];
        var first = await input.ReadAsync(header.AsMemory(0, 1), token).ConfigureAwait(false);
        if (first == 0) return null;
        await input.ReadExactlyAsync(header.AsMemory(1), token).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length <= 0 || length > maximumBytes)
            throw new BrowserOperationException("READER_WORKER_FRAME_INVALID");
        var payload = new byte[length];
        await input.ReadExactlyAsync(payload, token).ConfigureAwait(false);
        return payload;
    }

    public static async Task WriteAsync(Stream output, byte[] payload, int maximumBytes, CancellationToken token = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        if (payload.Length == 0 || payload.Length > maximumBytes)
            throw new BrowserOperationException("READER_WORKER_FRAME_INVALID");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await output.WriteAsync(header, token).ConfigureAwait(false);
        await output.WriteAsync(payload, token).ConfigureAwait(false);
        await output.FlushAsync(token).ConfigureAwait(false);
    }

    public static async Task<ReaderWorkerRequest> ReadRequestAsync(Stream input, CancellationToken token = default)
    {
        var bytes = await ReadAsync(input, ReaderWorkerRequest.MaximumRequestBytes, token).ConfigureAwait(false)
            ?? throw new BrowserOperationException("READER_WORKER_DISCONNECTED");
        var request = JsonSerializer.Deserialize<ReaderWorkerRequest>(bytes, Json)
            ?? throw new BrowserOperationException("READER_WORKER_REQUEST_INVALID");
        request.Validate();
        return request;
    }

    public static Task WriteResponseAsync(Stream output, ReaderWorkerResponse response, CancellationToken token = default) =>
        WriteAsync(output, JsonSerializer.SerializeToUtf8Bytes(response, Json), ReaderWorkerRequest.MaximumResponseBytes, token);

    public static async Task<ReaderWorkerResponse> ReadResponseAsync(Stream input, ReaderWorkerRequest expected,
        string? expectedDocument = null, CancellationToken token = default)
    {
        expected.Validate();
        var bytes = await ReadAsync(input, ReaderWorkerRequest.MaximumResponseBytes, token).ConfigureAwait(false)
            ?? throw new BrowserOperationException("READER_WORKER_DISCONNECTED");
        var response = JsonSerializer.Deserialize<ReaderWorkerResponse>(bytes, Json)
            ?? throw new BrowserOperationException("READER_WORKER_RESPONSE_INVALID");
        response.Validate(expected, expectedDocument);
        return response;
    }
}
