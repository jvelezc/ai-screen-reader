using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeadlessScreenReader.Domain;

/// <summary>A separate, one-shot navigation experiment tied to one retained public review.</summary>
public sealed record PublicTaskJob(Guid OrganizationId, Guid ParentScanId, Guid JobId, Guid RequestId,
    long Generation, PublicReaderSource Source, string ParentOriginalSha256)
{
    public const string Profile = "hsr-public-service-navigation-v1";
    public const int MaximumTabs = 48;
    public void Validate()
    {
        if (OrganizationId == Guid.Empty || ParentScanId == Guid.Empty || JobId == Guid.Empty || RequestId == Guid.Empty
            || Generation != 1 || Source is null || !PublicTaskRules.Digest(ParentOriginalSha256))
            throw new BrowserOperationException("PUBLIC_TASK_JOB_INVALID");
        Source.Validate();
        if (Source != PublicReaderSource.FromUrl(Source.TargetUrl) || new Uri(Source.TargetUrl).Query.Length != 0)
            throw new BrowserOperationException("PUBLIC_TASK_SOURCE_INVALID");
    }
}

/// <summary>One observed public hyperlink, never a form, script, account or transaction command.</summary>
public sealed record PublicTaskGoal(string Kind, string Name, string LinkName, string DestinationUrl)
{
    public void Validate(PublicReaderSource source)
    {
        if (Kind is not ("Pricing" or "Catalog" or "Contact" or "Help") || !PublicTaskRules.Text(Name, 200)
            || !PublicTaskRules.Text(LinkName, 500) || !PublicTaskRules.SafeDestination(source, DestinationUrl))
            throw new BrowserOperationException("PUBLIC_TASK_GOAL_INVALID");
    }
}

public sealed record PublicTaskStart(string Schema, PublicTaskJob Job, Guid WorkerId, Guid ExecutionId)
{
    public const string CurrentSchema = "hsr-public-task-worker-v1";
    public void Validate()
    {
        if (Schema != CurrentSchema || Job is null || WorkerId == Guid.Empty || ExecutionId == Guid.Empty)
            throw new BrowserOperationException("PUBLIC_TASK_START_INVALID");
        Job.Validate();
    }
}

public sealed record PublicTaskIntent(PublicTaskStart Start, int Sequence, int Attempt, string Action, PublicTaskGoal? Goal);
public sealed record PublicTaskAck(Guid ExecutionId, int Sequence, string IntentSha256);
public sealed record PublicTaskDiagnostic(PublicTaskStart Start, int Attempt, string Stage, string Category, string? Code)
{
    public const int MaximumBytes = 512 * 1024;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PublicTaskFailure? Failure { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PublicTaskNetworkTrace? Network { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PublicTaskNetworkTrace? Egress { get; init; }
}

/// <summary>Observed focus semantics and native anchor destination; no editable value is collected.</summary>
public sealed record PublicTaskFocus(string NodeId, string Role, string Name, string? AnchorUrl);
public sealed record PublicTaskEvent(int Sequence, double ElapsedMilliseconds, string Action,
    PublicTaskFocus? Before, PublicTaskFocus? After, string DocumentUrl, bool InputAttempted, bool InputCompleted);
public sealed record PublicTaskFile(string Path, string Sha256, int Bytes, string MediaType);

/// <summary>One fresh context. NotReached describes only this bounded attempt, never universal unreachability.</summary>
public sealed record PublicTaskAttempt(int Number, Guid Id, string Method, DateTimeOffset StartedAt,
    string Outcome, string? ErrorCode, string? FinalUrl, bool TaskTargetMatched, int PolicyInterruptions,
    ImmutableArray<PublicTaskEvent> Events, string BeforeSnapshot, string AfterSnapshot, string Video,
    bool BrowserCloseConfirmed, bool CaptureStopConfirmed, int DecodedVideoFrames)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PublicTaskNetworkTrace? Network { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<PublicTaskFailure>? Failures { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int OmittedFailures { get; init; }
}

/// <summary>Original collector facts. Resource removal and archive readback are independently checked by the parent.</summary>
public sealed record PublicTaskReport(string Schema, PublicTaskStart Start, PublicTaskGoal? Goal,
    ImmutableArray<PublicTaskAttempt> Attempts, ImmutableArray<PublicTaskFile> Files,
    string BrowserVersion, string PlaywrightVersion, string Platform, string Viewport, string CollectorSha256)
{
    public const string CurrentSchema = "hsr-public-task-report-v1";
    public const string TransportAttributionPolicy = "TransportAttributionV2";
    public const int MaximumReportBytes = 768 * 1024;
    public const int MaximumArchiveBytes = 25 * 1024 * 1024;
    public string Scope => "PublicServiceNavigationOnly";
    public string VideoAlignment => "EventSeekingNotVerified";
    public bool LegalCaseDetermined => false;
    public string AssistiveTechnologyComparison => "NotPerformed";

    // Absent in historical originals: their published assessment must remain reproducible.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AssessmentPolicy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NetworkPolicy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PublicTaskNetworkTrace? Egress { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? EgressIsolationProbeThroughSequence { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PublicTaskNetworkTrace? DiscoveryNetwork { get; init; }

    public void Validate(PublicTaskStart expected)
    {
        expected.Validate();
        void Require(bool value) { if (!value) throw new BrowserOperationException("PUBLIC_TASK_REPORT_INVALID"); }
        Require(Schema == CurrentSchema && Start == expected && !Attempts.IsDefault && !Files.IsDefault
            && Attempts.Length == (Goal is null ? 0 : 3) && Files.Length <= 10
            && Files.Select(f => f.Path).Distinct(StringComparer.Ordinal).Count() == Files.Length
            && PublicTaskRules.Text(BrowserVersion, 200) && PublicTaskRules.Text(PlaywrightVersion, 200)
            && PublicTaskRules.Text(Platform, 500) && Viewport == "1280x720" && PublicTaskRules.Digest(CollectorSha256));
        Goal?.Validate(Start.Job.Source);
        Require(NetworkPolicy is null or PublicTaskNetworkTrace.PublicDependencyPolicy);
        Require(AssessmentPolicy is null or TransportAttributionPolicy);
        Require(AssessmentPolicy is null || NetworkPolicy == PublicTaskNetworkTrace.PublicDependencyPolicy);
        Egress?.Validate();
        DiscoveryNetwork?.Validate();
        Require(Egress is null || NetworkPolicy == Egress.Policy);
        Require(DiscoveryNetwork is null || Goal is null && NetworkPolicy == DiscoveryNetwork.Policy);
        Require(NetworkPolicy is null || Egress is not null && EgressIsolationProbeThroughSequence is >= 0
            && Attempts.All(a => a.Network is not null && a.Failures is not null) && (Goal is not null || DiscoveryNetwork is not null));
        Require(EgressIsolationProbeThroughSequence is null || Egress is not null
            && EgressIsolationProbeThroughSequence <= (Egress.Events.LastOrDefault()?.Sequence ?? 0));
        Require(Attempts.Select(a => a.Id).Distinct().Count() == Attempts.Length);
        foreach (var file in Files)
            Require(PublicTaskRules.FileName(file.Path) && PublicTaskRules.Digest(file.Sha256)
                && file.Bytes is > 0 and <= MaximumArchiveBytes && file.MediaType is "application/json" or "video/webm");
        for (var i = 0; i < Attempts.Length; i++)
        {
            var attempt = Attempts[i];
            attempt.Network?.Validate();
            Require(attempt.OmittedFailures is >= 0 and <= 1000 && (attempt.Failures is null || !attempt.Failures.Value.IsDefault && attempt.Failures.Value.Length <= 4));
            if (attempt.Failures is { } failures) foreach (var failure in failures) failure.Validate(attempt.Events.IsDefault ? 0 : attempt.Events.Length);
            Require(attempt.Network is null || NetworkPolicy == attempt.Network.Policy);
            Require(attempt.Number == i + 1 && attempt.Id != Guid.Empty && attempt.Method == (i < 2 ? "Keyboard" : "Pointer")
                && attempt.StartedAt != default && attempt.Outcome is "Reached" or "NotReached" or "Unconfirmed"
                && attempt.PolicyInterruptions >= 0 && attempt.PolicyInterruptions <= 1000
                && !attempt.Events.IsDefault && attempt.Events.Length <= PublicTaskJob.MaximumTabs + 1
                && (attempt.FinalUrl is null ? attempt.Outcome == "Unconfirmed" : Start.Job.Source.AllowsDocument(attempt.FinalUrl))
                && (attempt.ErrorCode is null || attempt.ErrorCode is "PUBLIC_TASK_ATTEMPT_UNCONFIRMED" or "PUBLIC_TASK_POLICY_INTERRUPTED")
                && (attempt.Outcome != "Reached" || attempt.FinalUrl == Goal!.DestinationUrl && attempt.TaskTargetMatched && attempt.ErrorCode is null));
            double previous = -1;
            for (var j = 0; j < attempt.Events.Length; j++)
            {
                var step = attempt.Events[j];
                Require(step.Sequence == j + 1 && double.IsFinite(step.ElapsedMilliseconds) && step.ElapsedMilliseconds >= previous
                    && step.ElapsedMilliseconds <= 180_000 && (!step.InputCompleted || step.InputAttempted)
                    && Start.Job.Source.AllowsDocument(step.DocumentUrl)
                    && (i < 2 ? step.Action is "Tab" or "Enter" : step.Action == "Pointer")
                    && (step.Action != "Enter" || j == attempt.Events.Length - 1 && step.Before?.AnchorUrl == Goal!.DestinationUrl));
                previous = step.ElapsedMilliseconds;
                foreach (var focus in new[] { step.Before, step.After })
                    if (focus is not null) Require(PublicTaskRules.Text(focus.NodeId, 200) && PublicTaskRules.Text(focus.Role, 100)
                        && focus.Name is { Length: <= 500 } && (focus.AnchorUrl is null || focus.AnchorUrl.Length <= 2048));
            }
            Require(attempt.BeforeSnapshot == $"attempt-{i + 1}-before.json" && attempt.AfterSnapshot == $"attempt-{i + 1}-after.json"
                && attempt.Video == $"attempt-{i + 1}.webm" && attempt.DecodedVideoFrames >= 0);
            if (attempt.Outcome != "Unconfirmed") Require(attempt.BrowserCloseConfirmed && attempt.CaptureStopConfirmed
                && Files.Any(f => f.Path == attempt.BeforeSnapshot && f.MediaType == "application/json")
                && Files.Any(f => f.Path == attempt.AfterSnapshot && f.MediaType == "application/json")
                && Files.Any(f => f.Path == attempt.Video && f.MediaType == "video/webm") && attempt.DecodedVideoFrames > 0);
        }
        Require(Files.Sum(f => (long)f.Bytes) <= MaximumArchiveBytes);
    }
}

public sealed record PublicTaskCondition(string Id, bool Satisfied);
public sealed record PublicTaskAssessment(string Outcome, ImmutableArray<PublicTaskCondition> Conditions)
{
    public static PublicTaskAssessment Evaluate(PublicTaskReport report, bool originalsVerified, bool nativeRemovalConfirmed)
    {
        report.Validate(report.Start);
        var attempts = report.Attempts;
        var currentPolicy = report.AssessmentPolicy == PublicTaskReport.TransportAttributionPolicy;
        var egressConfirmed = !currentPolicy || report.Egress!.DroppedEvents == 0
            && !report.Egress.Events.Any(e => e.Sequence > report.EgressIsolationProbeThroughSequence && e.Code is not null);
        bool Complete(PublicTaskAttempt a) => a.Outcome != "Unconfirmed" && a.ErrorCode is null && a.TaskTargetMatched
            && a.PolicyInterruptions == 0 && a.BrowserCloseConfirmed && a.CaptureStopConfirmed && a.DecodedVideoFrames > 0
            && (a.Failures is null || a.Failures.Value.IsEmpty) && a.OmittedFailures == 0
            // Reaching the observed goal proves that specific success despite incidental requests.
            // A failed task with unexplained network errors cannot be promoted to a keyboard barrier.
            // Resource type alone cannot establish that a cancelled request was unrelated to failure.
            // Apply stronger attribution only to originals explicitly collected under that policy.
            && (a.Outcome == "Reached" || egressConfirmed && (a.Network is null || a.Network.DroppedEvents == 0
                && !a.Network.Events.Any(e => e.Code is not null && (currentPolicy || e.Code != "ERR_ABORTED") || e.Phase == "WebSocketRequested")))
            && a.Events.All(e => e.InputCompleted)
            && a.Events.LastOrDefault() is { InputCompleted: true } trigger
            && trigger.Action == (a.Method == "Keyboard" ? "Enter" : "Pointer");
        var complete = attempts.Length == 3 && attempts.All(Complete) && nativeRemovalConfirmed;
        var repeated = attempts.Length == 3 && attempts.Take(2).All(Complete) && attempts[0].Outcome == "NotReached" && attempts[1].Outcome == "NotReached";
        var pointer = attempts.Length == 3 && Complete(attempts[2]) && attempts[2].Outcome == "Reached";
        var trigger = repeated && attempts.Take(2).All(a => a.Events.LastOrDefault() is { Action: "Enter", InputCompleted: true });
        var context = attempts.Length == 3 && attempts.All(a => report.Files.Any(f => f.Path == a.BeforeSnapshot && f.MediaType == "application/json")
            && report.Files.Any(f => f.Path == a.AfterSnapshot && f.MediaType == "application/json"));
        var media = attempts.Length == 3 && attempts.All(a => a.BrowserCloseConfirmed && a.CaptureStopConfirmed && a.DecodedVideoFrames > 0
            && report.Files.Any(f => f.Path == a.Video && f.MediaType == "video/webm"));
        var environment = attempts.Length == 3 && attempts.All(a => a.TaskTargetMatched && a.BrowserCloseConfirmed && a.CaptureStopConfirmed)
            && report.BrowserVersion != "unavailable" && report.PlaywrightVersion != "unavailable";
        PublicTaskCondition[] conditions = [new("MeaningfulTask", report.Goal is not null),
            new("RecordedBarrier", repeated && pointer && trigger), new("ExactTrigger", trigger),
            new("AccessibilityContext", context),
            new("RepeatedFailure", repeated), new("PointerComparison", pointer),
            new("ReproducibleEnvironment", environment), new("OriginalArtifacts", originalsVerified && nativeRemovalConfirmed && context && media)];
        var outcome = conditions.All(c => c.Satisfied) ? "WebsiteEvidenceReady"
            : complete && originalsVerified && attempts.Take(2).All(a => a.Outcome == "Reached") ? "NoBlockingBarrierInTestedTask"
            : "InsufficientEvidence";
        return new(outcome, [.. conditions]);
    }
}

public static class PublicTaskRules
{
    public static bool Digest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);
    public static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);
    public static bool FileName(string? value) => value is not null && (value == "report.json"
        || Enumerable.Range(1, 3).Any(i => value == $"attempt-{i}.webm" || value == $"attempt-{i}-before.json" || value == $"attempt-{i}-after.json"));
    public static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
    public static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));
    /// <summary>An unchanged URL alone cannot establish failure: Enter may expose equivalent inline content.</summary>
    public static bool SameExposedContent(AccessibilitySnapshot before, AccessibilitySnapshot after)
    {
        object Projection(AccessibilitySnapshot value) => new
        {
            value.DocumentId,
            value.DocumentUrl,
            value.CaptureEpoch,
            // Chromium can renumber synthetic InlineTextBox leaves between captures.
            // Normalize only their comparison identity; retain names, parents, order and all other semantics.
            Nodes = value.Nodes.Select((n, i) => n with
            {
                IsFocused = false,
                Id = n.Role == "InlineTextBox" ? $"{n.ParentId}:inline-text-{i}" : n.Id
            }).OrderBy(n => n.Id, StringComparer.Ordinal),
            value.Frames,
            Limitations = value.Limitations.Order(StringComparer.Ordinal)
        };
        return JsonElement.DeepEquals(JsonSerializer.SerializeToElement(Projection(before)), JsonSerializer.SerializeToElement(Projection(after)));
    }
    public static bool SafeDestination(PublicReaderSource source, string? target)
    {
        if (target is null || !source.AllowsDocument(target) || target == source.TargetUrl) return false;
        var uri = new Uri(target);
        if (uri.Query.Length != 0 || uri.Fragment.Length != 0) return false;
        var path = Uri.UnescapeDataString(uri.AbsolutePath).ToLowerInvariant();
        if (new[] { "logout", "signout", "sign-out", "delete", "remove", "unsubscribe", "cancel", "purchase", "checkout", "payment", "oauth", "authorize", "confirm", "cart", "admin" }.Any(path.Contains)) return false;
        return new[] { "pricing", "price", "plans", "course", "catalog", "contact", "help", "support", "faq" }.Any(path.Contains);
    }
}
