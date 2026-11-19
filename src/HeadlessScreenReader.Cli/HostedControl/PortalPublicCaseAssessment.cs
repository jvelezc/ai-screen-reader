using HeadlessScreenReader.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace HeadlessScreenReader.Cli.HostedControl;

internal sealed partial class PortalPublicTasks
{
    internal async Task<PublicCaseAssessment> AssessCaseAsync(PortalIdentity actor, Guid organization, Guid scan,
        Guid request, PublicCaseAssessmentRequest input, CancellationToken token)
    {
        if (!input.HasSupportedInput()) throw new PortalFault(400, "CONTROL_REQUEST_INVALID");
        var review = await ReviewAssessmentAsync(actor, organization, scan, request, token).ConfigureAwait(false);
        var task = review.Tasks.SingleOrDefault(item => item.JobId == input.JobId)
            ?? throw new PortalFault(404, "CONTROL_PUBLIC_TASK_NOT_FOUND");
        if (task.ReportSha256 != input.ReportSha256) throw new PortalFault(409, "CONTROL_PUBLIC_REVIEW_CHANGED");
        return PublicCaseAssessment.Assess(review, task, input);
    }
}

internal static class PortalCaseAssessmentEndpoints
{
    internal static void Map(WebApplication app) => app.MapPost(
        "/api/workspaces/{organization:guid}/website-scans/{scan:guid}/tasks/sets/{request:guid}/case-assessment",
        async (HttpContext context, PortalPublicTasks service, Guid organization, Guid scan, Guid request, CancellationToken token) =>
        {
            var input = await HostedControlServer.BodyAsync<PublicCaseAssessmentRequest>(context, token).ConfigureAwait(false);
            var result = await service.AssessCaseAsync(HostedControlServer.Actor(context), organization, scan, request, input, token).ConfigureAwait(false);
            return TypedResults.Json(result, PortalJson.Options);
        })
        .WithName("AssessPublicReviewCase").WithSummary("Screen one source-bound task using the selected profile")
        .WithDescription("Combines the current caller's verified evidence for one task with counsel-supplied findings. Read-only computation without persistence, website input or a filing decision.")
        .Produces<PublicCaseAssessment>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403)
        .ProducesProblem(404).ProducesProblem(409).ProducesProblem(415).ProducesProblem(503);
}
