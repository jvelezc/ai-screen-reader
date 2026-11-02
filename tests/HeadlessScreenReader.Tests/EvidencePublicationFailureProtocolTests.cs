using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

public sealed class EvidencePublicationFailureProtocolTests
{
    [Theory]
    [InlineData(EvidencePublicationFailureProtocol.StorageUnavailable, EvidencePublicationFailureKind.StorageUnavailable, "Unavailable")]
    [InlineData(EvidencePublicationFailureProtocol.WriteUnconfirmed, EvidencePublicationFailureKind.WriteUnconfirmed, "Unavailable")]
    [InlineData(EvidencePublicationFailureProtocol.ContentInvalid, EvidencePublicationFailureKind.ContentInvalid, "Invalid")]
    [InlineData(EvidencePublicationFailureProtocol.OriginalMissing, EvidencePublicationFailureKind.OriginalMissing, "Unavailable")]
    public void ClosedPublicationCodePreservesItsMeaningWithoutClaimingOriginals(string code, EvidencePublicationFailureKind expected, string evidenceState)
    {
        Assert.Equal(expected, EvidencePublicationFailureProtocol.Parse(code));
        Assert.Equal(evidenceState, EvidencePublicationFailureProtocol.EvidenceState(expected));
        var action = EvidencePublicationFailureProtocol.NextAction(expected);
        Assert.Contains("same scan", action); Assert.Contains("if available", action);
        Assert.Contains("storage-only", action); Assert.Contains("website input", action);
        if (expected == EvidencePublicationFailureKind.ContentInvalid)
        {
            Assert.Contains("conflicting bytes", action); Assert.Contains("Do not overwrite", action);
        }
        else
        {
            Assert.Contains("does not prove", action); Assert.Contains("replacement attempt", action);
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("legacy")]
    [InlineData("whitespace")]
    [InlineData("case")]
    [InlineData("forged")]
    [InlineData("control")]
    public void UnknownOrChangedCodeCannotBecomeAPublicationDiagnostic(string caseId)
    {
        var code = caseId switch
        {
            "null" => null,
            "empty" => "",
            "legacy" => "FIXTURE_OBSERVATION_UNCONFIRMED",
            "whitespace" => EvidencePublicationFailureProtocol.StorageUnavailable + " ",
            "case" => EvidencePublicationFailureProtocol.StorageUnavailable.ToLowerInvariant(),
            "forged" => "WORKER_PUBLICATION_STORAGE_RECOVERED",
            "control" => EvidencePublicationFailureProtocol.StorageUnavailable + "\n",
            _ => throw new ArgumentOutOfRangeException(nameof(caseId))
        };
        Assert.Null(EvidencePublicationFailureProtocol.Parse(code));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void UndefinedKindCannotProduceEvidenceStateOrRecoveryAdvice(int raw)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EvidencePublicationFailureProtocol.EvidenceState((EvidencePublicationFailureKind)raw));
        Assert.Throws<ArgumentOutOfRangeException>(() => EvidencePublicationFailureProtocol.NextAction((EvidencePublicationFailureKind)raw));
    }
}
