using System.Security.Cryptography;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Cli;

public interface IPrivateViewerIdentity
{
    bool TryAuthenticate(string? authorization, out Guid subject);
}
/// <summary>One process-provisioned proof identity. This is not Supabase or tenant authentication.</summary>
public sealed class ProcessViewerIdentity(Guid subject) : IPrivateViewerIdentity
{
    private readonly byte[] _credential = RandomNumberGenerator.GetBytes(32);
    public ViewerSecret Credential => new(Convert.ToHexStringLower(_credential));
    public bool TryAuthenticate(string? authorization, out Guid authenticatedSubject)
    {
        authenticatedSubject = Guid.Empty;
        if (subject == Guid.Empty || authorization is not { Length: 71 } || !authorization.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
        byte[] supplied;
        try { supplied = Convert.FromHexString(authorization[7..]); }
        catch (FormatException) { return false; }
        if (!CryptographicOperations.FixedTimeEquals(supplied, _credential)) return false;
        authenticatedSubject = subject; return true;
    }
}
