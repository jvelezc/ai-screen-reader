using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class PublicWebsiteObservationCodecTests
{
    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static PublicWebsiteObservation Read(byte[] bytes, string? digest = null) =>
        PublicWebsiteObservationCodec.Decode(bytes, digest ?? Digest(bytes), PublicWebsiteObservationTests.Job(), PublicWebsiteObservationTests.Worker);
    private static StoredPublicWebsiteObservation Stored() => PublicWebsiteObservationCodec.Encode(PublicWebsiteObservationTests.Observation(),
        PublicWebsiteObservationTests.Job(), PublicWebsiteObservationTests.Worker);

    [Fact]
    public void StoredOriginalBytesRetainEscapedNamesProducerSourceAndUnverifiedOutcome()
    {
        var value = PublicWebsiteObservationTests.Observation();
        var snapshot = value.Captures[1] with { Nodes = [value.Captures[1].Nodes[0] with { Name = "A \"quoted\" cafÃ© <control>" }] };
        var encoded = PublicWebsiteObservationCodec.Encode(value with { Captures = [value.Captures[0], snapshot] }, value.Job, value.WorkerId);
        var bytes = Encoding.UTF8.GetBytes(encoded.Text); var restored = Read(bytes, encoded.Sha256);
        Assert.Equal(Digest(bytes), encoded.Sha256); Assert.Equal("A \"quoted\" cafÃ© <control>", restored.Captures[1].Nodes[0].Name);
        Assert.Equal("https://screenreader.privensic.com/ci/v1/source", restored.Job.Source.TargetUrl);
        Assert.Equal(PublicWebsiteObservationTests.Worker, restored.WorkerId); Assert.Equal(0, restored.DispatchedInputs); Assert.False(restored.GoalVerified);
        Assert.DoesNotContain("quoted", encoded.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(PublicWebsiteObservation.MaximumSupportedBytes + 1)]
    public void DecoderRejectsMissingAndOversizedBytesBeforeParsing(int length)
    {
        Assert.Equal("PUBLIC_WEBSITE_OBSERVATION_SIZE_INVALID", Assert.Throws<InvalidDataException>(() => Read(new byte[length])).Message);
    }

    [Fact]
    public void LargeCapturedContentRoundTripsAboveLegacyLimitWithExactDigestAndProducer()
    {
        var value = PublicWebsiteObservationTests.Observation();
        var name = new string('x', 1_048_577);
        var capture = value.Captures[1] with { Nodes = [value.Captures[1].Nodes[0] with { Name = name }] };
        var stored = PublicWebsiteObservationCodec.Encode(value with { Captures = [value.Captures[0], capture] }, value.Job, value.WorkerId);
        var bytes = Encoding.UTF8.GetBytes(stored.Text);
        Assert.Equal(1_048_576, PublicWebsiteObservation.MaximumBytes);
        Assert.InRange(bytes.Length, 1_048_577, 4_194_304);
        var restored = Read(bytes, stored.Sha256);
        Assert.Equal(name, restored.Captures[1].Nodes[0].Name); Assert.Equal(value.WorkerId, restored.WorkerId);
        Assert.Equal(Digest(bytes), stored.Sha256); Assert.False(restored.GoalVerified); Assert.Equal(0, restored.DispatchedInputs);
    }

    [Fact]
    public void DecoderAcceptsExactByteCeilingWithAValidOriginalDocument()
    {
        var raw = Stored().Text.PadRight(PublicWebsiteObservation.MaximumSupportedBytes, ' '); var bytes = Encoding.UTF8.GetBytes(raw);
        var decoded = Read(bytes);
        Assert.Equal(PublicWebsiteObservation.MaximumSupportedBytes, bytes.Length); Assert.Equal(2, decoded.Captures.Length);
        Assert.Equal("actual-document", decoded.Captures[1].DocumentId);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("short")]
    [InlineData("upper")]
    [InlineData("nonhex")]
    [InlineData("different")]
    public void DecoderRequiresTheExactLowercaseOriginalByteDigest(string scenario)
    {
        var bytes = Encoding.UTF8.GetBytes(Stored().Text); var digest = scenario switch
        {
            "empty" => "",
            "short" => "a",
            "upper" => Digest(bytes).ToUpperInvariant(),
            "nonhex" => new string('z', 64),
            _ => new string('0', 64)
        };
        Assert.Equal("PUBLIC_WEBSITE_OBSERVATION_DIGEST_MISMATCH", Assert.Throws<InvalidDataException>(() => Read(bytes, digest)).Message);
    }

    [Theory]
    [InlineData("extra")]
    [InlineData("missing-close")]
    [InlineData("missing-worker")]
    [InlineData("string-close")]
    [InlineData("goal")]
    [InlineData("input")]
    [InlineData("tenant")]
    [InlineData("source")]
    [InlineData("producer")]
    public void MatchingHashCannotAuthorizeChangedScopeOrUnconfirmedPublicFacts(string scenario)
    {
        var root = JsonNode.Parse(Stored().Text)!.AsObject();
        switch (scenario)
        {
            case "extra": root["CallerCredential"] = "not-permitted"; break;
            case "missing-close": root.Remove("NativeShutdownConfirmed"); break;
            case "missing-worker": root.Remove("WorkerId"); break;
            case "string-close": root["NativeShutdownConfirmed"] = "true"; break;
            case "goal": root["GoalVerified"] = true; break;
            case "input": root["DispatchedInputs"] = 1; break;
            case "tenant": root["Job"]!["OrganizationId"] = Guid.NewGuid(); break;
            case "source": root["Job"]!["Source"]!["TargetUrl"] = "https://example.com/"; break;
            case "producer": root["WorkerId"] = Guid.NewGuid(); break;
        }
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString());
        var error = Assert.Throws<InvalidDataException>(() => Read(bytes));
        Assert.Equal("PUBLIC_WEBSITE_OBSERVATION_INVALID", error.Message);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("{\"Schema\":\"one\",\"Schema\":\"two\"}")]
    public void DecoderCannotTurnMalformedNullArrayOrDuplicateFieldsIntoOriginalFacts(string json)
    {
        Assert.Equal("PUBLIC_WEBSITE_OBSERVATION_INVALID", Assert.Throws<InvalidDataException>(() => Read(Encoding.UTF8.GetBytes(json))).Message);
    }

    [Fact]
    public void EncoderRejectsAValidatedShapeThatExceedsTheActualByteBudget()
    {
        var value = PublicWebsiteObservationTests.Observation();
        var capture = value.Captures[1] with { Nodes = [value.Captures[1].Nodes[0] with { Name = new string('a', PublicWebsiteObservation.MaximumSupportedBytes) }] };
        Assert.Equal("PUBLIC_WEBSITE_OBSERVATION_SIZE_INVALID", Assert.Throws<InvalidDataException>(() =>
            PublicWebsiteObservationCodec.Encode(value with { Captures = [value.Captures[0], capture] }, value.Job, value.WorkerId)).Message);
    }

    [Fact]
    public void PublicLeaseNeverSerializesOrFormatsItsPrivateCapability()
    {
        var capability = Guid.NewGuid(); var lease = new PublicWebsiteLease(PublicWebsiteObservationTests.Job(), PublicWebsiteObservationTests.Worker, capability);
        var json = JsonSerializer.Serialize(lease, ObservationStore.Json);
        Assert.DoesNotContain(capability.ToString("D"), json); Assert.DoesNotContain(capability.ToString("D"), lease.ToString());
        Assert.DoesNotContain("Token", json); Assert.Contains("capability redacted", lease.ToString());
    }
}
