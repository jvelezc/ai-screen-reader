using System.Text.Json.Nodes;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Cli.HostedControl;

internal static partial class PortalOpenApi
{
    private static void ApplyPublicCaseSchema(Type type, JsonObject value, JsonObject properties)
    {
        JsonArray Findings() => Strings(PublicCaseAssessmentRequest.Unassessed, PublicCaseAssessmentRequest.Supported,
            PublicCaseAssessmentRequest.Unsupported, PublicCaseAssessmentRequest.Conflicting);

        if (type == typeof(PublicCaseAssessmentRequest) || type == typeof(PublicCaseAssessment))
        {
            properties["profile"]!["const"] = PublicCaseAssessmentRequest.SupportedProfile;
            properties["reportSha256"]!["pattern"] = "^[a-f0-9]{64}$";
        }
        if (type == typeof(PublicCaseAssessmentRequest))
        {
            foreach (var field in new[] { "coverage", "websiteConnection", "claimantAccess", "standing" })
                properties[field]!["enum"] = Findings();
            value["additionalProperties"] = false;
            value["description"] = "One counsel-selected profile and four categorical findings. Report digest is required, with null only when there is no current verified original.";
        }
        if (type == typeof(PublicCaseAssessment))
        {
            properties["schema"]!["const"] = PublicCaseAssessment.CurrentSchema;
            properties["outcome"]!["enum"] = Strings("EvidenceGaps", "ConflictingEvidence", "ContraryEvidence", "CounselReview");
            properties["criteria"]!["minItems"] = 5;
            properties["criteria"]!["maxItems"] = 5;
            value["description"] = "Read-only, source-bound screening based on one verified task and counsel's conditional selections. No filing, claimant verification, persistence, legal case determination or new input.";
        }
        if (type == typeof(PublicCaseCriterion))
        {
            properties["id"]!["enum"] = Strings("Coverage", "WebsiteConnection", "Barrier", "ClaimantAccess", "Standing");
            properties["status"]!["enum"] = Findings();
            properties["source"]!["enum"] = Strings("Counsel", "RecordedTask");
        }
    }

    private static void AddPublicCaseSchemas(JsonObject schemas)
    {
        schemas["PublicCaseAssessmentRequest"] = Schema<PublicCaseAssessmentRequest>("PublicCaseAssessmentRequest");
        schemas["PublicCaseAssessment"] = Schema<PublicCaseAssessment>("PublicCaseAssessment");
    }

    private static void AddPublicCasePaths(JsonObject paths)
    {
        var operation = Operation("AssessPublicReviewCase",
            "Combine this caller's current verified evidence for the exact selected task with counsel's selections under one supported profile. This POST computes a read-only result, never stores the selections, and never starts website input or determines whether to file.",
            "PublicCaseAssessment", true, "PublicCaseAssessmentRequest", organization: true, scan: true);
        ((JsonArray)operation["parameters"]!).Add(new JsonObject
        {
            ["name"] = "request",
            ["in"] = "path",
            ["required"] = true,
            ["schema"] = new JsonObject { ["type"] = "string", ["format"] = "uuid" }
        });
        paths["/api/workspaces/{organization}/website-scans/{scan}/tasks/sets/{request}/case-assessment"] = Path("post", operation);
    }
}
