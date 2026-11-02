using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public sealed record StoredPublicWebsiteObservation(string Text, string Sha256)
{
    public override string ToString() => $"StoredPublicWebsiteObservation({Sha256}, captured bytes omitted)";
}

/// <summary>Strict retained bytes and original producer scope; this cannot authorize an imported website claim.</summary>
public static class PublicWebsiteObservationCodec
{
    public static StoredPublicWebsiteObservation Encode(PublicWebsiteObservation value, PublicWebsiteJob expected, Guid worker)
    {
        value.Validate(expected, worker);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, ObservationStore.Json);
        if (bytes.Length is 0 or > PublicWebsiteObservation.MaximumBytes) throw new InvalidDataException("PUBLIC_WEBSITE_OBSERVATION_SIZE_INVALID");
        return new(Encoding.UTF8.GetString(bytes), Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    public static PublicWebsiteObservation Decode(ReadOnlySpan<byte> bytes, string digest, PublicWebsiteJob expected, Guid worker)
    {
        if (bytes.Length is 0 or > PublicWebsiteObservation.MaximumBytes) throw new InvalidDataException("PUBLIC_WEBSITE_OBSERVATION_SIZE_INVALID");
        if (digest is null || digest.Length != 64 || digest.Any(c => !char.IsAsciiDigit(c) && c is not (>= 'a' and <= 'f'))
            || Convert.ToHexStringLower(SHA256.HashData(bytes)) != digest)
            throw new InvalidDataException("PUBLIC_WEBSITE_OBSERVATION_DIGEST_MISMATCH");
        try
        {
            var value = JsonSerializer.Deserialize<PublicWebsiteObservation>(bytes, ObservationStore.Json)
                ?? throw new InvalidDataException("PUBLIC_WEBSITE_OBSERVATION_INVALID");
            value.Validate(expected, worker); return value;
        }
        catch (Exception error) when (error is JsonException or BrowserOperationException)
        {
            throw new InvalidDataException("PUBLIC_WEBSITE_OBSERVATION_INVALID");
        }
    }
}
