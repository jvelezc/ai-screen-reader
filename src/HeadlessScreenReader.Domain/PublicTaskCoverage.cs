using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

/// <summary>Unchanged exposed data supporting a diagnostic task hint, never execution authority.</summary>
public sealed record PublicTaskCoverageExample(string NodeId, string Role, string ExposedName, bool Disabled);

public sealed record PublicTaskCoverageGroup(string Kind, bool ServiceLinkProfileAvailable, int ObservedControls,
    int EnabledControls, int ServiceLinkCandidates, ImmutableArray<PublicTaskCoverageExample> Examples)
{
    public string State => ObservedControls == 0 ? "NotDetectedInCapturedTree"
        : !ServiceLinkProfileAvailable ? "ObservedWithoutSupportedPublicInputProfile"
        : ServiceLinkCandidates == 0 ? "ObservedWithoutEnabledServiceLink" : "UnverifiedServiceLinkHint";
    public bool ExamplesTruncated => Examples.Length < ObservedControls;
    public bool ExecutionAuthorized => false;
}

/// <summary>Coverage of one retained tree. Missing hints cannot establish absence or accessible task completion.</summary>
public sealed record PublicTaskCoverage(string Schema, string Policy, string DocumentId, long CaptureEpoch,
    int InspectedNodes, int InteractiveControls, int UnnamedInteractiveControls, int UnclassifiedInteractiveControls,
    ImmutableArray<PublicTaskCoverageGroup> Groups, ImmutableArray<string> Limitations,
    int TotalLimitations)
{
    public const string CurrentSchema = "hsr-public-task-coverage-v1";
    public const string CurrentPolicy = "hsr-captured-task-hints-v1";
    public string Scope => "CapturedPageOnly";
    public string TaskExecution => "NotStartedByCoverage";
    public bool LimitationsTruncated => Limitations.Length < TotalLimitations;
    public bool FullSiteCoverageEstablished => false;
    public bool LegalCaseDetermined => false;
}
