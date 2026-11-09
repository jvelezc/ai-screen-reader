using System.Text.Json.Nodes;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Cli.HostedControl;

internal static partial class PortalOpenApi
{
    private static void AddPublicTaskSchemas(JsonObject schemas)
    {
        schemas["CreatePublicTask"] = Schema<PortalCreatePublicTaskRequest>();
        schemas["PublicTaskStatus"] = Schema<PublicTaskStatus>("PublicTaskStatus");
        schemas["PublicTaskStatusOrNull"] = new JsonObject { ["oneOf"] = new JsonArray(Reference("PublicTaskStatus"), new JsonObject { ["type"] = "null" }) };
        schemas["PublicTaskResult"] = Schema<PortalTaskResult>("PublicTaskResult");
        schemas["PublicTaskEvents"] = Schema<PortalTaskEventPage>("PublicTaskEvents");
        schemas["PublicTaskGraph"] = Schema<PortalTaskGraphPage>("PublicTaskGraph");
        schemas["PublicTaskOriginal"] = Schema<PortalTaskOriginalPage>("PublicTaskOriginal");
    }
    private static void AddPublicTaskPaths(JsonObject paths)
    {
        const string root = "/api/workspaces/{organization}/website-scans/{scan}/tasks";
        JsonObject TaskOperation(string name, string detail, string output, string? body = null, int status = 200, bool job = true)
        {
            var operation = Operation(name, detail, output, true, body, status, organization: true, scan: true);
            if (job) ((JsonArray)operation["parameters"]!).Add(new JsonObject
            {
                ["name"] = "job",
                ["in"] = "path",
                ["required"] = true,
                ["schema"] = new JsonObject { ["type"] = "string", ["format"] = "uuid" }
            });
            return operation;
        }
        paths[root] = new JsonObject
        {
            ["post"] = TaskOperation("AdmitPublicTask", "One idempotent public GET-only service-navigation experiment tied to this completed review digest. Fresh current application session and membership required. Two fresh keyboard attempts and a separate pointer comparison; no credentials, forms, purchases or automatic input replay. Admission is not a barrier or legal conclusion.", "PublicTaskStatus", "CreatePublicTask", 202, false),
            ["get"] = TaskOperation("ReadLatestPublicTask", "Latest task for this exact source review; null means none. No example or another website is substituted.", "PublicTaskStatusOrNull", job: false)
        };
        paths[root + "/{job}"] = Path("get", TaskOperation("ReadPublicTaskStatus", "Scoped current task metadata; expired authority is not native cleanup or a task verdict.", "PublicTaskStatusOrNull"));
        paths[root + "/{job}/cancel"] = Path("post", TaskOperation("CancelPublicTask", "Revoke future input authority; inputs already dispatched are not reversed. Completed originals are unchanged.", "PublicTaskStatus"));
        paths[root + "/{job}/result"] = Path("get", TaskOperation("ReadPublicTaskResult", "Verify original report and every retained artifact before deriving eight website-evidence conditions. Ready concerns the tested website task only. Disability, injury, standing, legal liability, WCAG conformance and actual NVDA speech are not established.", "PublicTaskResult"));
        var events = TaskOperation("ReadPublicTaskEvents", "At most24 recorded events from one actual attempt, with pinned report digest. Graph projections must retain attempted/completed/observed distinctions. These times do not establish video seeking alignment.", "PublicTaskEvents");
        var eventParameters = (JsonArray)events["parameters"]!;
        eventParameters.Add(PublicParameter("attempt", 1, 3)); eventParameters.Add(PublicParameter("offset", 0, 49)); eventParameters.Add(PublicDigestParameter());
        paths[root + "/{job}/events/{attempt}/{offset}/{digest}"] = Path("get", events);
        var graph = TaskOperation("ReadPublicTaskGraph", "At most24 observed focus states and24 actual input edges per page; missing focus is explicitly unobserved. No global reachability or unreachability inference.", "PublicTaskGraph");
        var graphParameters = (JsonArray)graph["parameters"]!;
        graphParameters.Add(PublicParameter("attempt", 1, 3)); graphParameters.Add(PublicParameter("offset", 0, 98)); graphParameters.Add(PublicDigestParameter());
        paths[root + "/{job}/graph/{attempt}/{offset}/{digest}"] = Path("get", graph);
        var original = TaskOperation("ReadPublicTaskOriginal", "At most64KiB of an exact original. Verify page and whole-file hashes and reauthorize at exact EOF before playback/export. The video is the original recording; viewing it sends no website input. No signed URL or storage credential.", "PublicTaskOriginal");
        var parameters = (JsonArray)original["parameters"]!;
        parameters.Add(new JsonObject
        {
            ["name"] = "name",
            ["in"] = "path",
            ["required"] = true,
            ["schema"] = new JsonObject { ["type"] = "string", ["pattern"] = "^(report\\.json|attempt-[123](\\.webm|-(before|after)\\.json))$" }
        });
        parameters.Add(PublicParameter("offset", 0, PublicTaskReport.MaximumArchiveBytes)); parameters.Add(PublicDigestParameter());
        paths[root + "/{job}/originals/{name}/{offset}/{digest}"] = Path("get", original);
    }
}
