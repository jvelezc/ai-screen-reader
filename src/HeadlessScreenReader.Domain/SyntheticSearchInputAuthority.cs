namespace HeadlessScreenReader.Domain;

public enum SyntheticSearchInputKind { Key, Character, MouseFocus, MouseActivation }
public enum SyntheticSearchInputDecision { Granted, Revoked, Expired, Unconfirmed }

/// <summary>One native input request. A grant is neither completed input nor a verified goal.</summary>
public sealed record SyntheticSearchInputIntent(long NativeSequence, SyntheticSearchInputKind Kind,
    string? NativeKey, int CharacterIndex, string SourceSha256, string DocumentId, long CaptureEpoch, string? NodeId)
{
    public string Schema => "hsr-synthetic-search-input-intent-v1";

    public void Validate()
    {
        static bool Text(string? value) => value is { Length: > 0 and <= 128 } && !value.Any(char.IsControl);
        var valid = NativeSequence > 0 && Enum.IsDefined(Kind) && SourceSha256 is { Length: 64 }
            && SourceSha256.All(char.IsAsciiHexDigitLower) && Text(DocumentId) && CaptureEpoch > 0
            && (NodeId is null || Text(NodeId));
        valid &= Kind switch
        {
            SyntheticSearchInputKind.Key => CharacterIndex == 0 && NativeKey is "Tab" or "Enter" or "Space",
            SyntheticSearchInputKind.Character => Text(NodeId) && CharacterIndex is >= 1 and <= 13
                && NativeKey == (SyntheticSearchPolicy.Query[CharacterIndex - 1] == ' ' ? "Space" : SyntheticSearchPolicy.Query[CharacterIndex - 1].ToString()),
            SyntheticSearchInputKind.MouseFocus or SyntheticSearchInputKind.MouseActivation => Text(NodeId) && CharacterIndex == 0 && NativeKey is null,
            _ => false
        };
        if (!valid) throw new BrowserInputDeniedException("SYNTHETIC_SEARCH_INPUT_INTENT_INVALID");
    }
}
