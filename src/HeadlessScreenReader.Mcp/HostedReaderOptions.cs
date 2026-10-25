namespace HeadlessScreenReader.Mcp;

/// <summary>One private core API and original caller; never a browser or service-role credential.</summary>
public sealed class HostedReaderOptions
{
    public Uri Origin { get; }
    public Guid Organization { get; }
    internal string Bearer { get; }

    public HostedReaderOptions(Uri origin, Guid organization, string bearer)
    {
        if (!origin.IsAbsoluteUri || origin.Scheme != "http" || origin.Host is not ("localhost" or "127.0.0.1" or "[::1]")
            || origin.AbsolutePath != "/" || origin.UserInfo.Length != 0 || origin.Query.Length != 0 || origin.Fragment.Length != 0
            || organization == Guid.Empty || bearer is null || bearer.Length is < 1 or > 8192
            || bearer.Any(character => character is <= ' ' or >= '\u007f'))
            throw new ArgumentException("HOSTED_CONFIGURATION_INVALID");
        Origin = origin; Organization = organization; Bearer = bearer;
    }

    public static HostedReaderOptions FromEnvironment() => new(
        new Uri(Environment.GetEnvironmentVariable("HSR_MCP_API_ORIGIN") ?? "http://localhost:6086/", UriKind.Absolute),
        Guid.Parse(Environment.GetEnvironmentVariable("HSR_MCP_ORGANIZATION") ?? ""),
        Environment.GetEnvironmentVariable("HSR_MCP_ACCESS_TOKEN") ?? "");

    public override string ToString() => "HostedReaderOptions(original caller credential redacted)";
}
