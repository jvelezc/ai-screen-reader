using System.Collections.Immutable;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public sealed record SyntheticSearchTaskTrial(int Trial, string Submission, string RunId, string Execution,
    string Reachability, int AttemptedKeys, int CompletedKeys);
public sealed record SyntheticSearchTaskOriginal(string Path, long Bytes, string Sha256);
public sealed record SyntheticSearchTaskManifest(string Schema, DateTimeOffset CreatedAtUtc, SyntheticSearchTaskStart Start,
    ImmutableArray<SyntheticSearchTaskTrial> KeyboardTrials, string PointerRunId, string Observation,
    string ProducerSha256, string ProducerVersion, string Runtime, string Platform, string Viewport,
    ImmutableArray<SyntheticSearchTaskOriginal> Files)
{
    public const string CurrentSchema = "hsr-embedded-search-task-collection-v1";
    public const string ManifestFile = "search-task-manifest.json";
    public string Scope => "ControlledEmbeddedSearchTaskExperiments";
    public string VideoAlignment => "UnverifiedWithoutCalibrationMarkers";
    public string WebsiteEvidenceAssessment => "NotAssessed";
    public string ExternalAssistiveTechnologyComparison => "NotPerformed";
    public bool LegalCaseDetermined => false;
}
