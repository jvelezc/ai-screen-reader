namespace HeadlessScreenReader.Domain;

/// <summary>Task navigation authority is separate from ordinary website dependency requests.</summary>
public static class PublicTaskRequestPolicy
{
    public static TargetDecision Assess(PublicTargetPolicy target, string url, string method, bool topDocument)
    {
        if (method is not ("GET" or "HEAD") && (topDocument || !target.PublicDependenciesEnabled))
            return new(null, "DOCUMENT_METHOD_DENIED");
        var decision = topDocument ? target.AssessUrl(url) : target.AssessDependencyUrl(url);
        return decision.Allowed ? decision : new(null, topDocument ? "DOCUMENT_URL_DENIED" : "DEPENDENCY_URL_DENIED");
    }

    public static TargetDecision AssessWebSocket(PublicTargetPolicy target, string url) => target.PublicDependenciesEnabled
        && url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)
        ? target.AssessDependencyUrl("https://" + url[6..]) : new(null, "WEBSOCKET_URL_DENIED");
}
