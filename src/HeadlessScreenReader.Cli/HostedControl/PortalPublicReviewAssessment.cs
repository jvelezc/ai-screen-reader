using System.Collections.Immutable;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace HeadlessScreenReader.Cli.HostedControl;

internal sealed record PortalReviewMemberDiagnostic(Guid JobId, string State, string Availability, string? ExpectedReportSha256,
    string? ReportSha256, Guid? ExecutionId, PortalTaskOriginalFailure? OriginalFailure);
internal sealed record PortalReviewReadDiagnostic(string Schema, Guid SetId, Guid RequestId, string ParentOriginalSha256,
    string Outcome, int VerifiedTaskCount, ImmutableArray<PortalReviewMemberDiagnostic> Tasks);

internal sealed partial class PortalPublicTasks
{
    internal async Task<PublicReviewAssessment> ReviewAssessmentAsync(PortalIdentity actor, Guid organization, Guid parent,
        Guid request, CancellationToken token, Action<PortalReviewReadDiagnostic>? diagnostic = null)
    {
        var initial = await SetStatusAsync(actor, organization, parent, request, token).ConfigureAwait(false)
            ?? throw new PortalFault(404, "CONTROL_PUBLIC_TASK_SET_NOT_FOUND");
        var verified = new Dictionary<Guid, PortalTaskResult>();
        var unavailable = new HashSet<Guid>();
        var failures = new Dictionary<Guid, PortalTaskOriginalFailure>();
        foreach (var member in initial.Tasks)
        {
            token.ThrowIfCancellationRequested();
            if (member.State != "completed") continue;
            try
            {
                var result = await ResultAsync(actor, organization, parent, member.Job.JobId, token).ConfigureAwait(false);
                if (result.Status.Job != member.Job || result.Status.ReportSha256 != member.ReportSha256)
                    throw new PortalFault(409, "CONTROL_PUBLIC_REVIEW_CHANGED");
                verified.Add(member.Job.JobId, result);
            }
            catch (PortalFault error) when (error.Status == 503 && error.Code == "CONTROL_PUBLIC_TASK_ORIGINAL_UNAVAILABLE")
            {
                token.ThrowIfCancellationRequested();
                unavailable.Add(member.Job.JobId);
                if (error.Data[typeof(PortalTaskOriginalFailure)] is PortalTaskOriginalFailure detail && detail.JobId == member.Job.JobId)
                    failures.Add(member.Job.JobId, detail);
            }
        }
        foreach (var result in verified.Values)
        {
            token.ThrowIfCancellationRequested();
            await RecheckAsync(actor, result.Status, token).ConfigureAwait(false);
        }
        var current = await SetStatusAsync(actor, organization, parent, request, token).ConfigureAwait(false)
            ?? throw new PortalFault(409, "CONTROL_PUBLIC_REVIEW_CHANGED");
        if (initial.SetId != current.SetId || initial.CreatedAt != current.CreatedAt || initial.Source != current.Source
            || initial.ParentOriginalSha256 != current.ParentOriginalSha256 || initial.MaximumTasks != current.MaximumTasks
            || !initial.GoalKinds.SequenceEqual(current.GoalKinds, StringComparer.Ordinal)
            || !initial.Tasks.Select(task => task.Job).SequenceEqual(current.Tasks.Select(task => task.Job)))
            throw new PortalFault(409, "CONTROL_PUBLIC_REVIEW_CHANGED");
        var tasks = ImmutableArray.CreateBuilder<PublicReviewTaskEvidence>();
        foreach (var member in current.Tasks)
        {
            if (verified.TryGetValue(member.Job.JobId, out var result))
            {
                if (member.State != "completed" || member.ReportSha256 != result.Status.ReportSha256)
                    throw new PortalFault(409, "CONTROL_PUBLIC_REVIEW_CHANGED");
                tasks.Add(new(PublicReviewTaskEvidence.CurrentSchema, member.Job.JobId, "VerifiedOriginals", result.Status.ReportSha256,
                    result.Status.Publication!.ExecutionId, result.Goal, result.Assessment, [.. result.Files]));
            }
            else
            {
                var availability = member.State is "queued" or "leased" ? "Pending"
                    : member.State != "completed" ? "Stopped"
                    : unavailable.Contains(member.Job.JobId) ? "OriginalsUnavailable" : "OriginalsNotRead";
                tasks.Add(new(PublicReviewTaskEvidence.CurrentSchema, member.Job.JobId, availability, null, null, null, null, []));
            }
        }
        var assessment = new PublicReviewAssessment(PublicReviewAssessment.CurrentSchema, current, tasks.ToImmutable());
        try { assessment.Validate(organization, parent, actor.UserId, request); }
        catch (BrowserOperationException) { throw new PortalFault(503, "CONTROL_PUBLIC_REVIEW_RESPONSE_INVALID"); }
        if (JsonSerializer.SerializeToUtf8Bytes(assessment, PortalJson.Options).Length > PortalPublicWebsiteContract.MaximumReplyBytes)
            throw new PortalFault(409, "CONTROL_PUBLIC_REVIEW_PAGE_TOO_LARGE");
        // Scoped safe failure details belong to request diagnostics, not the public evidence decision.
        var diagnostics = new PortalReviewReadDiagnostic("hsr-review-evidence-read-v1", current.SetId, current.RequestId,
            current.ParentOriginalSha256, assessment.Outcome, assessment.VerifiedTaskCount,
            [.. assessment.Tasks.Select((task, index) => new PortalReviewMemberDiagnostic(task.JobId, current.Tasks[index].State,
                task.Availability, current.Tasks[index].ReportSha256, task.ReportSha256, task.ExecutionId,
                failures.GetValueOrDefault(task.JobId)))]);
        try { diagnostic?.Invoke(diagnostics); }
        catch (Exception) { /* Diagnostics cannot change the evidence decision or repeat a read. */ }
        return assessment;
    }
}

internal static class PortalPublicReviewAssessmentEndpoints
{
    internal static void Map(WebApplication app) => app.MapGet(
        "/api/workspaces/{organization:guid}/website-scans/{scan:guid}/tasks/sets/{request:guid}/assessment",
        async (HttpContext context, PortalPublicTasks service, Guid organization, Guid scan, Guid request, CancellationToken token) =>
        {
            var result = await service.ReviewAssessmentAsync(HostedControlServer.Actor(context), organization, scan, request, token,
                value => context.Items[typeof(PortalReviewReadDiagnostic)] = value).ConfigureAwait(false);
            return TypedResults.Json(result, PortalJson.Options);
        })
        .WithName("ReadPublicReviewAssessment").WithSummary("Assess this task set using each task's verified original records")
        .WithDescription("Read-only, current-caller evidence assessment of the exact saved page's declared service-link tasks. Every available task report and supporting file is verified independently. Conditions from different tasks are never combined into a barrier. Pending, stopped and unavailable evidence stays explicit. No whole-site, actual assistive-technology or legal determination; no task input, admission or retry.")
        .Produces<PublicReviewAssessment>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403)
        .ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
}
