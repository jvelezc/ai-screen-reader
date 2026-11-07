using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

public enum WebsiteTaskPurpose { DiscoverServices, ReadPricing, Enrollment, AccountAccess, ContactSupport }

/// <summary>A missing exposed name is a potential issue, not proof of task failure or a legal violation.</summary>
public sealed record WebsiteScreeningIssue(string Id, string Rule, string NodeId, string Role, string ExposedName, bool Disabled)
{
    public string Classification => "PotentialIssue";
    public string TaskImpact => "NotAssessed";
}

/// <summary>An observed control suggests a service journey; neither its purpose nor its successful use is verified.</summary>
public sealed record WebsiteTaskCandidate(string Id, WebsiteTaskPurpose Purpose, string NodeId, string Role,
    string ExposedName, bool Disabled)
{
    public string State => "NotTested";
    public bool ExecutionAuthorized => false;
}

/// <summary>Diagnostic screening of one complete captured tree, separate from reader semantics and recorded task assessment.</summary>
public sealed record WebsitePageScreening(string Schema, string Policy, string DocumentId, long CaptureEpoch,
    int InspectedNodes, int InteractiveControls, ImmutableArray<WebsiteScreeningIssue> Issues,
    ImmutableArray<WebsiteTaskCandidate> SuggestedTasks, ImmutableArray<string> Limitations)
{
    public const string CurrentSchema = "hsr-website-page-screening-v1";
    public const string CurrentPolicy = "hsr-service-journey-screening-v1";
    public string Scope => "CapturedPageOnly";
    public string TaskExecution => "NotStarted";
    public string TaskBarrier => "NotAssessed";
    public string EvidenceReadiness => "InsufficientTaskEvidence";
    public bool LegalCaseDetermined => false;
}
