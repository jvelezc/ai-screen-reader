using System.Text.Json.Nodes;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Cli.HostedControl;

internal static partial class PortalOpenApi
{
    private static void ApplyPublicTaskCoverageSchema(Type type, JsonObject value, JsonObject properties)
    {
        if (type == typeof(PortalTaskAttemptSummary))
            value["required"] = Strings(properties.Select(p => p.Key).Where(k => k != "navigation").ToArray());
        if (type == typeof(PublicTaskLiveFrame))
        {
            properties["schema"]!["const"] = PublicTaskLiveFrame.CurrentSchema;
            properties["attempt"]!["minimum"] = 1; properties["attempt"]!["maximum"] = 3;
            properties["sequence"]!["minimum"] = 1;
            properties["width"]!["minimum"] = 1; properties["width"]!["maximum"] = 1280;
            properties["height"]!["minimum"] = 1; properties["height"]!["maximum"] = 720;
            properties["data"]!["maxLength"] = (PublicTaskLiveFrame.MaximumImageBytes + 2) / 3 * 4;
            properties["data"]!["contentEncoding"] = "base64"; properties["data"]!["contentMediaType"] = "image/jpeg";
            properties["sha256"]!["pattern"] = "^[a-f0-9]{64}$";
        }
        if (type == typeof(PublicTaskNavigation))
        {
            properties["strategy"]!["const"] = PublicTaskNavigation.CurrentStrategy;
            properties["stopReason"]!["enum"] = Strings("GoalFocused", "FocusCycle", "FocusUnobserved", "InputUnconfirmed", "TargetChanged", "NotStarted");
            properties["stopReason"]!["description"] = "Why this observed sequential route stopped. A focus cycle does not prove that all possible reader routes were exhausted.";
        }
        if (type == typeof(PortalCreatePublicTaskRequest) || type == typeof(PublicTaskJob))
        {
            value["required"] = Strings(properties.Select(p => p.Key).Where(k => k != "goalKind").ToArray());
            var kinds = Strings("Pricing", "Catalog", "Contact", "Help"); kinds.Add((JsonNode?)null);
            properties["goalKind"]!["enum"] = kinds;
            properties["goalKind"]!["description"] = "Optional exact service-link kind. Omitted/null preserves automatic selection. Changing this field under the same request conflicts.";
        }
        if (type == typeof(PortalWebsiteTaskCoverageResponse))
        {
            properties["latestTasks"]!["maxItems"] = 5;
            properties["taskResultsRequireOriginalRead"]!["const"] = true;
            properties["captureIndex"]!["const"] = 1; properties["captures"]!["const"] = 2;
        }
        if (type == typeof(PublicTaskCoverage))
        {
            properties["schema"]!["const"] = PublicTaskCoverage.CurrentSchema;
            properties["policy"]!["const"] = PublicTaskCoverage.CurrentPolicy;
            properties["scope"]!["const"] = "CapturedPageOnly";
            properties["taskExecution"]!["const"] = "NotStartedByCoverage";
            properties["fullSiteCoverageEstablished"]!["const"] = false; properties["legalCaseDetermined"]!["const"] = false;
            properties["groups"]!["minItems"] = 8; properties["groups"]!["maxItems"] = 8;
            properties["limitations"]!["maxItems"] = 12;
            foreach (var name in new[] { "inspectedNodes", "interactiveControls", "unnamedInteractiveControls", "unclassifiedInteractiveControls" })
            { properties[name]!["minimum"] = 0; properties[name]!["maximum"] = 2048; }
            properties["captureEpoch"]!["minimum"] = 1;
        }
        if (type == typeof(PublicTaskCoverageGroup))
        {
            properties["kind"]!["enum"] = Strings("Pricing", "Catalog", "Contact", "Help", "Search", "FormCompletion", "AccountAccess", "EnrollmentOrCheckout");
            properties["examples"]!["maxItems"] = 2; properties["executionAuthorized"]!["const"] = false;
        }
    }
    private static void AddPublicTaskSchemas(JsonObject schemas)
    {
        schemas["CreatePublicTask"] = Schema<PortalCreatePublicTaskRequest>();
        schemas["PublicTaskCoverage"] = Schema<PortalWebsiteTaskCoverageResponse>("PublicTaskCoverage");
        schemas["PublicTaskStatus"] = Schema<PublicTaskStatus>("PublicTaskStatus");
        schemas["PublicTaskStatusOrNull"] = new JsonObject { ["oneOf"] = new JsonArray(Reference("PublicTaskStatus"), new JsonObject { ["type"] = "null" }) };
        schemas["PublicTaskLiveFrame"] = Schema<PublicTaskLiveFrame>();
        schemas["PublicTaskLiveFrameOrNull"] = new JsonObject { ["oneOf"] = new JsonArray(Reference("PublicTaskLiveFrame"), new JsonObject { ["type"] = "null" }) };
        schemas["PublicTaskResult"] = Schema<PortalTaskResult>("PublicTaskResult");
        schemas["PublicTaskEvents"] = Schema<PortalTaskEventPage>("PublicTaskEvents");
        schemas["PublicTaskReader"] = Schema<PortalTaskReaderPage>("PublicTaskReader");
        schemas["PublicTaskGraph"] = Schema<PortalTaskGraphPage>("PublicTaskGraph");
        schemas["PublicTaskOriginal"] = Schema<PortalTaskOriginalPage>("PublicTaskOriginal");
    }
    private static void AddPublicTaskPaths(JsonObject paths)
    {
        const string root = "/api/workspaces/{organization}/website-scans/{scan}/tasks";
        var coverage = Operation("ReadPublicTaskCoverage", "Diagnostic task hints from the exact retained final page tree; no new input. At most five latest task statuses remain bound to the source digest and current caller. Original task results require their own verified read. Unsupported and unobserved journeys remain unassessed, never a clean-site or legal claim.", "PublicTaskCoverage", true, organization: true, scan: true);
        ((JsonArray)coverage["parameters"]!).Add(PublicDigestParameter());
        paths["/api/workspaces/{organization}/website-scans/{scan}/coverage/{digest}"] = Path("get", coverage);
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
            ["post"] = TaskOperation("AdmitPublicTask", "One idempotent source-bound service-link experiment tied to this completed review digest. Public HTTPS/WSS dependency requests use an independently DNS-validated proxy; top-level navigation stays source-bound GET/HEAD. Fresh current session/membership required. Two keyboard attempts and a separate pointer comparison; no credentials, forms, purchases or automatic input replay.", "PublicTaskStatus", "CreatePublicTask", 202, false),
            ["get"] = TaskOperation("ReadLatestPublicTask", "Latest task for this exact source review; null means none. No example or another website is substituted.", "PublicTaskStatusOrNull", job: false)
        };
        paths[root + "/{job}"] = Path("get", TaskOperation("ReadPublicTaskStatus", "Scoped current task metadata; expired authority is not native cleanup or a task verdict.", "PublicTaskStatusOrNull"));
        paths[root + "/{job}/live-frame"] = Path("get", TaskOperation("ReadPublicTaskLiveFrame", "View-only latest JPEG of this caller's active public task worker, at most8 seconds old. Null means waiting, closed, stale or no live frame. No input, lease renewal, replay or evidence write.", "PublicTaskLiveFrameOrNull"));
        paths[root + "/{job}/cancel"] = Path("post", TaskOperation("CancelPublicTask", "Revoke future input authority; inputs already dispatched are not reversed. Completed originals are unchanged.", "PublicTaskStatus"));
        paths[root + "/{job}/result"] = Path("get", TaskOperation("ReadPublicTaskResult", "Legacy result shape without diagnostics. Verify original report and every artifact before deriving evidence conditions. The explicit /result/diagnostics representation contains at most32 redacted issue events from this same original, timestamp/request/evidence references and troubleshooting actions. Missing/truncated traces and expected isolation probes are explicit; technical failures are not accessibility barriers. Plaintiff-specific legal elements, WCAG conformance and actual NVDA speech are outside scope.", "PublicTaskResult"));
        paths[root + "/{job}/result/diagnostics"] = Path("get", TaskOperation("ReadPublicTaskDiagnosticsResult", "Explicit diagnostic representation of the same verified result, with at most32 original-bound technical issue rows. Same authentication and original verification as /result; no new task authority or input replay.", "PublicTaskResult"));
        var events = TaskOperation("ReadPublicTaskEvents", "At most24 recorded events from one actual attempt, with pinned report digest. Graph projections must retain attempted/completed/observed distinctions. These times do not establish video seeking alignment.", "PublicTaskEvents");
        var eventParameters = (JsonArray)events["parameters"]!;
        eventParameters.Add(PublicParameter("attempt", 1, 3)); eventParameters.Add(PublicParameter("offset", 0, int.MaxValue)); eventParameters.Add(PublicDigestParameter());
        paths[root + "/{job}/events/{attempt}/{offset}/{digest}"] = Path("get", events);
        var reader = TaskOperation("ReadPublicTaskReader", "Actual AI reader commands, announcements, input counts and last-observed snapshot fingerprints. At most12 events and112KiB per page; pagination never skips a record. Initial output appears only at offset0. NotRetained identifies historical originals without a transcript, Partial exposes missing records. These are reader text outputs, not recorded external AT speech; intermediate full snapshots are not retained by this representation. No task input or authority renewal.", "PublicTaskReader");
        var readerParameters = (JsonArray)reader["parameters"]!;
        readerParameters.Add(PublicParameter("attempt", 1, 3)); readerParameters.Add(PublicParameter("offset", 0, int.MaxValue)); readerParameters.Add(PublicDigestParameter());
        paths[root + "/{job}/reader/{attempt}/{offset}/{digest}"] = Path("get", reader);
        var graph = TaskOperation("ReadPublicTaskGraph", "At most24 observed focus states and24 actual input edges per page; missing focus is explicitly unobserved. No global reachability or unreachability inference.", "PublicTaskGraph");
        var graphParameters = (JsonArray)graph["parameters"]!;
        graphParameters.Add(PublicParameter("attempt", 1, 3)); graphParameters.Add(PublicParameter("offset", 0, int.MaxValue)); graphParameters.Add(PublicDigestParameter());
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
