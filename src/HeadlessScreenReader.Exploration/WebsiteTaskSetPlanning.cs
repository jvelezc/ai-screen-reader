using System.Collections.Immutable;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Exploration;

/// <summary>Chooses captured-page service-link hints within a budget; a separate current admission authorizes execution.</summary>
public static class WebsiteTaskSetPlanning
{
    public static ImmutableArray<string> Select(PublicTaskCoverage coverage, int maximum)
    {
        string[] all = [.. PublicTaskSet.SupportedKinds, "Search", "FormCompletion", "AccountAccess", "EnrollmentOrCheckout"];
        if (maximum is < 1 or > PublicTaskSet.MaximumMembers || coverage is null
            || coverage.Schema != PublicTaskCoverage.CurrentSchema || coverage.Policy != PublicTaskCoverage.CurrentPolicy
            || !PublicTaskRules.Text(coverage.DocumentId, 8192) || coverage.CaptureEpoch < 1
            || coverage.InspectedNodes is < 0 or > 2048 || coverage.Groups.IsDefault || coverage.Groups.Length != all.Length
            || coverage.Groups.Any(group => group is null))
            throw new ArgumentException("A source-bound coverage report and a one-to-four task budget are required.");
        for (var i = 0; i < all.Length; i++)
        {
            var group = coverage.Groups[i];
            if (group.Kind != all[i] || group.ServiceLinkProfileAvailable != PublicTaskGoal.SupportsKind(group.Kind)
                || group.ObservedControls < 0 || group.ObservedControls > coverage.InspectedNodes
                || group.EnabledControls < 0 || group.EnabledControls > group.ObservedControls
                || group.ServiceLinkCandidates < 0 || group.ServiceLinkCandidates > group.EnabledControls
                || !group.ServiceLinkProfileAvailable && group.ServiceLinkCandidates != 0)
                throw new ArgumentException("Coverage groups must preserve their original bounded kinds and counts.");
        }
        if (coverage.Groups.Sum(group => group.ObservedControls) > coverage.InspectedNodes)
            throw new ArgumentException("Coverage cannot count the same captured node as multiple tasks.");
        return coverage.Groups.Where(group => group.ServiceLinkProfileAvailable && group.ServiceLinkCandidates > 0)
            .Take(maximum).Select(group => group.Kind).ToImmutableArray();
    }
}
