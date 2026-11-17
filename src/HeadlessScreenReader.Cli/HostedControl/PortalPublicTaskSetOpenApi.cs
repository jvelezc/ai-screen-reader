using System.Text.Json.Nodes;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Cli.HostedControl;

internal static partial class PortalOpenApi
{
    private static void ApplyPublicTaskSetSchema(Type type, JsonObject value, JsonObject properties)
    {
        if (type == typeof(PortalCreatePublicTaskSetRequest) || type == typeof(PublicTaskSet))
        {
            properties["maximumTasks"]!["minimum"] = 1;
            properties["maximumTasks"]!["maximum"] = PublicTaskSet.MaximumMembers;
            properties["parentOriginalSha256"]!["pattern"] = "^[a-f0-9]{64}$";
        }
        if (type == typeof(PublicTaskSet))
        {
            properties["schema"]!["const"] = PublicTaskSet.CurrentSchema;
            properties["scope"]!["const"] = "CapturedPageServiceLinks";
            properties["taskResultsRequireOriginalRead"]!["const"] = true;
            properties["isAssessment"]!["const"] = false;
            properties["tasks"]!["maxItems"] = PublicTaskSet.MaximumMembers;
            properties["goalKinds"]!["maxItems"] = PublicTaskSet.MaximumMembers;
            properties["goalKinds"]!["uniqueItems"] = true;
            properties["goalKinds"]!["items"]!["enum"] = Strings(PublicTaskSet.SupportedKinds.ToArray());
            value["description"] = "Atomic current-caller admission metadata. Canonical ordered kinds and equally ordered child tasks; zero tasks explicitly records no supported captured-page candidates. Each result requires its own verified original. This set never assesses a barrier, conformance or a legal case.";
        }
        if (type == typeof(PublicTaskSetMember))
        {
            properties["schema"]!["const"] = PublicTaskSetMember.CurrentSchema;
            properties["state"]!["enum"] = Strings("queued", "leased", "completed", "blocked", "cancelled", "interrupted");
            properties["phase"]!["enum"] = Strings("Queued", "Starting", "KeyboardAttempt1", "KeyboardAttempt2", "PointerComparison", "Finalizing", "Finished");
            value["description"] = "Compact independent job metadata, without publication, producer or assessment claims. Read the original-verified child result separately. Expired authority is not native cleanup or a barrier.";
        }
    }

    private static void AddPublicTaskSetSchemas(JsonObject schemas)
    {
        schemas["CreatePublicTaskSet"] = Schema<PortalCreatePublicTaskSetRequest>();
        schemas["PublicTaskSet"] = Schema<PublicTaskSet>("PublicTaskSet");
        schemas["PublicTaskSetOrNull"] = new JsonObject
        { ["oneOf"] = new JsonArray(Reference("PublicTaskSet"), new JsonObject { ["type"] = "null" }) };
    }

    private static void AddPublicTaskSetPaths(JsonObject paths)
    {
        const string root = "/api/workspaces/{organization}/website-scans/{scan}/tasks/sets";
        var admit = Operation("AdmitPublicTaskSet",
            "Atomically admit applicable Pricing, Catalog, Contact and Help service-link tasks within the explicit one-to-four budget, from this exact verified saved-page digest. Each child runs two keyboard attempts and one pointer comparison independently. Same request reconciles the same set without repeating uncertain input; changed digest or budget conflicts. Existing organization quota applies to the entire transaction. Unsupported, unobserved and budget-excluded tasks remain unassessed. No credentials, form submission, purchases or aggregate verdict.",
            "PublicTaskSet", true, "CreatePublicTaskSet", 202, organization: true, scan: true);
        admit["responses"]!["202"]!["headers"] = new JsonObject
        {
            ["Location"] = new JsonObject
            { ["description"] = "Same request's authorized set metadata; admission is not execution", ["schema"] = new JsonObject { ["type"] = "string", ["format"] = "uri-reference" } }
        };
        admit["responses"]!["429"] = Response("Existing workspace queue quota prevents the entire atomic admission", "Problem", "application/problem+json");
        paths[root] = Path("post", admit);
        var read = Operation("ReadPublicTaskSet",
            "Read this original caller's exact set by its request UUID; null means absent. Fresh session and workspace access required. No admission, input retry or evidence assessment. Read each child's original-verified result, graph, reader text and matching recording separately.",
            "PublicTaskSetOrNull", true, organization: true, scan: true);
        ((JsonArray)read["parameters"]!).Add(new JsonObject
        {
            ["name"] = "request",
            ["in"] = "path",
            ["required"] = true,
            ["schema"] = new JsonObject { ["type"] = "string", ["format"] = "uuid" }
        });
        paths[root + "/{request}"] = Path("get", read);
    }
}
