using System.Text.Json.Nodes;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Cli.HostedControl;

internal static partial class PortalOpenApi
{
    private static void ApplyPublicReviewSchema(Type type, JsonObject value, JsonObject properties)
    {
        if (type == typeof(PublicReviewAssessment))
        {
            properties["schema"]!["const"] = PublicReviewAssessment.CurrentSchema;
            properties["scope"]!["const"] = "CapturedPageServiceLinks";
            properties["isAssessment"]!["const"] = true;
            foreach (var name in new[] { "websiteInputStartedByAssessment", "fullSiteCoverageEstablished", "legalCaseDetermined" })
                properties[name]!["const"] = false;
            properties["assistiveTechnologyComparison"]!["const"] = "NotEstablishedByThisAssessment";
            properties["outcome"]!["enum"] = Strings("WebsiteEvidenceReady", "NoBlockingBarrierInTestedTasks", "InsufficientEvidence");
            properties["verifiedTaskCount"]!["minimum"] = 0;
            properties["verifiedTaskCount"]!["maximum"] = PublicTaskSet.MaximumMembers;
            properties["tasks"]!["maxItems"] = PublicTaskSet.MaximumMembers;
            properties["barrierJobIds"]!["maxItems"] = PublicTaskSet.MaximumMembers;
            properties["barrierJobIds"]!["uniqueItems"] = true;
            value["description"] = "Current read-only assessment of the exact source task set. Only independently verified originals support a task finding. One complete barrier may qualify its own website evidence package while other members remain explicitly unavailable; true conditions are never combined across tasks. Zero, pending, stopped or missing evidence cannot produce a clean set finding. No whole-site conformance, external assistive-technology or legal determination.";
        }
        if (type == typeof(PublicReviewTaskEvidence))
        {
            properties["schema"]!["const"] = PublicReviewTaskEvidence.CurrentSchema;
            properties["availability"]!["enum"] = Strings("VerifiedOriginals", "Pending", "Stopped", "OriginalsUnavailable", "OriginalsNotRead");
            properties["originalFiles"]!["maxItems"] = PublicReviewTaskEvidence.MaximumFiles;
            properties["reportSha256"]!["pattern"] = "^[a-f0-9]{64}$";
            properties["executionId"]!["format"] = "uuid";
            value["description"] = "Same-order child of taskSet. VerifiedOriginals binds its current report fingerprint/execution and all supporting files to that exact job. Other availability states contain no goal, assessment, execution, report fingerprint or original-file claims. A completed task not read during this response remains OriginalsNotRead.";
        }
    }

    private static void AddPublicReviewSchemas(JsonObject schemas) =>
        schemas["PublicReviewAssessment"] = Schema<PublicReviewAssessment>("PublicReviewAssessment");

    private static void AddPublicReviewPaths(JsonObject paths)
    {
        var read = Operation("ReadPublicReviewAssessment",
            "Read this caller's exact source-bound task set and independently verify every available completed task report and all supporting originals through current access checks. Conditions from different tasks never form one barrier. Pending/stopped/unavailable/not-read members stay explicit. Zero members cannot establish no barriers. This GET admits no jobs, sends no website input, needs no live worker and makes no legal or whole-site determination.",
            "PublicReviewAssessment", true, organization: true, scan: true);
        ((JsonArray)read["parameters"]!).Add(new JsonObject
        {
            ["name"] = "request",
            ["in"] = "path",
            ["required"] = true,
            ["schema"] = new JsonObject { ["type"] = "string", ["format"] = "uuid" }
        });
        paths["/api/workspaces/{organization}/website-scans/{scan}/tasks/sets/{request}/assessment"] = Path("get", read);
    }
}
