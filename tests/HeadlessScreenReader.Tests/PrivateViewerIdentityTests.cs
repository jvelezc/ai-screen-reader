using HeadlessScreenReader.Cli;

namespace HeadlessScreenReader.Tests;

public sealed class PrivateViewerIdentityTests
{
    [Theory]
    [InlineData("valid", true)]
    [InlineData("uppercase", true)]
    [InlineData("missing", false)]
    [InlineData("scheme", false)]
    [InlineData("short", false)]
    [InlineData("long", false)]
    [InlineData("nonhex", false)]
    [InlineData("unknown", false)]
    [InlineData("empty-subject", false)]
    public void ProcessIdentity_RequiresTheExactProvisionedCredentialAndReturnsNoCallerSuppliedSubject(string partition, bool expected)
    {
        var subject = partition == "empty-subject" ? Guid.Empty : Guid.NewGuid(); var identity = new ProcessViewerIdentity(subject);
        var header = partition switch
        {
            "missing" => null,
            "scheme" => "Basic " + identity.Credential.Value,
            "short" => "Bearer " + identity.Credential.Value[..63],
            "long" => "Bearer " + identity.Credential.Value + "0",
            "nonhex" => "Bearer " + new string('z', 64),
            "unknown" => "Bearer " + new string('0', 64),
            "uppercase" => "Bearer " + identity.Credential.Value.ToUpperInvariant(),
            _ => "Bearer " + identity.Credential.Value
        };
        Assert.Equal(expected, identity.TryAuthenticate(header, out var actual));
        Assert.Equal(expected ? subject : Guid.Empty, actual);
    }
}
