using System.Text.Json.Nodes;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Cli.HostedControl;

internal static partial class PortalOpenApi
{
    private static void AddSearchTaskSchemas(JsonObject schemas)
    {
        schemas["CreateSearchTask"] = Schema<PortalCreateSearchTaskRequest>();
        schemas["SearchTaskStatus"] = Schema<SyntheticSearchTaskStatus>("SearchTaskStatus");
        schemas["SearchTaskStatusOrNull"] = new JsonObject { ["oneOf"] = new JsonArray(Reference("SearchTaskStatus"), new JsonObject { ["type"] = "null" }) };
        schemas["SearchTaskResult"] = Schema<PortalSearchResult>("SearchTaskResult");
        schemas["SearchTaskFiles"] = Schema<PortalSearchFilePage>("SearchTaskFiles");
        schemas["SearchTaskOriginal"] = Schema<PortalSearchOriginalPage>("SearchTaskOriginal");
        schemas["SearchTaskDiagnostics"] = Schema<PortalSearchDiagnostics>("SearchTaskDiagnostics");
    }

    private static void AddSearchTaskPaths(JsonObject paths)
    {
        const string root = "/api/workspaces/{organization}/search-demonstrations";
        JsonObject SearchOperation(string name, string detail, string output, string? body = null, int status = 200, bool job = true)
        {
            var operation = Operation(name, detail, output, true, body, status, organization: true);
            if (job) ((JsonArray)operation["parameters"]!).Add(new JsonObject
            { ["name"] = "job", ["in"] = "path", ["required"] = true, ["schema"] = new JsonObject { ["type"] = "string", ["format"] = "uuid" } });
            return operation;
        }
        var admission = SearchOperation("AdmitSearchTask", "One explicit, idempotent controlled search demonstration. sourceId is search-working or search-keyboard-blocked. Four keyboard contexts and a separate pointer comparison, with fixed synthetic query; never a test of a customer URL. Current original-caller session and workspace membership required. Reconcile a lost response with the SAME requestId and sourceId. An expired input lease is terminal; storage-only recovery never repeats website input.", "SearchTaskStatus", "CreateSearchTask", 202, false);
        admission["responses"]!["202"]!["headers"] = new JsonObject
        { ["Location"] = new JsonObject { ["description"] = "Same independent job status; no execution claim", ["schema"] = new JsonObject { ["type"] = "string", ["format"] = "uri-reference" } } };
        admission["responses"]!["429"] = Response("Workspace search queue is full; reconcile the same existing request before new work", "Problem", "application/problem+json");
        paths[root] = new JsonObject
        {
            ["post"] = admission,
            ["get"] = SearchOperation("ReadLatestSearchTask", "Latest independent search demonstration in this workspace, or null. It is not linked to the current customer website review.", "SearchTaskStatusOrNull", job: false)
        };
        paths[root + "/{job}"] = Path("get", SearchOperation("ReadSearchTaskStatus", "Current scoped state. Admission, completed input, observed goal and verified originals are separate facts. Expired leases do not imply resource removal.", "SearchTaskStatusOrNull"));
        paths[root + "/{job}/cancel"] = Path("post", SearchOperation("CancelSearchTask", "Revoke future input and publication authority. Does not reverse already dispatched input, replay an interrupted test or alter completed originals.", "SearchTaskStatus"));
        paths[root + "/{job}/result"] = Path("get", SearchOperation("ReadSearchTaskResult", "Verify the original archive and all source, execution, reader, graph and media correspondence. Returns four keyboard summaries, a separate pointer video and exact original-file IDs. Scope is controlled synthetic search; website evidence and legal case remain unassessed. Recordings are browser video, not actual external assistive-technology speech. Timing alignment is unverified.", "SearchTaskResult"));
        var files = SearchOperation("ReadSearchTaskFiles", "At most24 original descriptors, sorted by ordinal path, pinned by immutable index SHA-256. Stable numeric IDs avoid accepting arbitrary file paths. Follow nextOffset without skipping; originals remain unchanged.", "SearchTaskFiles");
        ((JsonArray)files["parameters"]!).Add(PublicParameter("offset", 0, 1400));
        ((JsonArray)files["parameters"]!).Add(PublicDigestParameter());
        paths[root + "/{job}/files/{offset}/{digest}"] = Path("get", files);
        var original = SearchOperation("ReadSearchTaskOriginal", "At most64KiB of one original, pinned by index digest and sorted file ID. Verify page and whole-file SHA-256, then make a fresh exact-EOF authorization read before playback/export. Ordinary offsets are64KiB boundaries; exact EOF is allowed even when unaligned and returns empty bytes. Every read rechecks current caller/workspace and immutable job before and after original retrieval. No signed URL, credentials, generated HTML execution or input replay.", "SearchTaskOriginal");
        var parameters = (JsonArray)original["parameters"]!;
        parameters.Add(PublicParameter("fileId", 0, 1400)); parameters.Add(PublicParameter("offset", 0, 26 * 1024 * 1024)); parameters.Add(PublicDigestParameter());
        paths[root + "/{job}/originals/{fileId}/{offset}/{digest}"] = Path("get", original);
        paths[root + "/{job}/diagnostics"] = Path("get", SearchOperation("ReadSearchTaskDiagnostics", "At most three ordered storage-publication attempts, with original producer, publisher, execution, immutable index and closed failure reasons. WebsiteInputRetried is always false. No raw provider error, lease/session capability, page body, credentials or automatic retry.", "SearchTaskDiagnostics"));
    }

    private static void ApplySearchTaskSchema(Type type, JsonObject value, JsonObject properties)
    {
        if (type == typeof(PortalCreateSearchTaskRequest)) properties["sourceId"]!["enum"] = Strings("search-working", "search-keyboard-blocked");
        if (type == typeof(SyntheticSearchTaskSource))
        {
            properties["id"]!["enum"] = Strings("search-working", "search-keyboard-blocked");
            properties["sha256"]!["pattern"] = "^[a-f0-9]{64}$";
        }
        if (type == typeof(SyntheticSearchTaskJob))
        { properties["generation"]!["const"] = 1; properties["profile"]!["const"] = SyntheticSearchTaskJob.CurrentProfile; }
        if (type == typeof(SyntheticSearchTaskStatus))
        {
            properties["schema"]!["const"] = SyntheticSearchTaskStatus.CurrentSchema;
            properties["state"]!["enum"] = Strings("queued", "leased", "publishing", "completed", "blocked", "cancelled", "interrupted");
            properties["phase"]!["enum"] = Strings("Queued", "Starting", "Collecting", "Publishing", "Finished");
            value["allOf"] = JsonNode.Parse("""
                [{"if":{"properties":{"state":{"const":"completed"}}},
                  "then":{"properties":{"publication":{"type":"object"},"producerId":{"type":"string","format":"uuid"},"phase":{"const":"Finished"},"errorCode":{"const":null}}},
                  "else":{"properties":{"publication":{"const":null}}}}]
                """);
        }
        if (type == typeof(SyntheticSearchTaskPublication))
        {
            properties["schema"]!["const"] = SyntheticSearchTaskPublication.CurrentSchema;
            foreach (var name in new[] { "indexSha256", "manifestSha256", "archiveSha256" }) properties[name]!["pattern"] = "^[a-f0-9]{64}$";
            properties["manifestBytes"]!["minimum"] = 1; properties["manifestBytes"]!["maximum"] = 512 * 1024;
            properties["archiveBytes"]!["minimum"] = 1; properties["archiveBytes"]!["maximum"] = 26 * 1024 * 1024;
            properties["bytes"]!["minimum"] = 1; properties["bytes"]!["maximum"] = 25 * 1024 * 1024;
            properties["files"]!["minimum"] = 28; properties["files"]!["maximum"] = 1400;
            properties["nativeRemoved"]!["const"] = true; properties["originalsVerified"]!["const"] = true;
        }
        if (type == typeof(SyntheticSearchTaskPublication) || type == typeof(PortalSearchResult))
        {
            properties["scope"]!["const"] = "ControlledEmbeddedSearchTaskExperiments";
            properties["websiteEvidenceAssessment"]!["const"] = "NotAssessed";
            properties["videoAlignment"]!["const"] = "UnverifiedWithoutCalibrationMarkers";
            properties["legalCaseDetermined"]!["const"] = false;
            properties["observation"]!["enum"] = Strings("AllTestedKeyboardGoalsObserved", "AKeyboardAlternativeReachedTheGoal", "KeyboardGoalNotObservedAcrossFieldAndButtonAlternativesWithPointerGoalObserved");
        }
        if (type == typeof(PortalSearchResult))
        {
            properties["keyboardTrials"]!["minItems"] = 4; properties["keyboardTrials"]!["maxItems"] = 4;
            properties["externalAssistiveTechnologyComparison"]!["const"] = "NotPerformed";
            properties["viewport"]!["const"] = "320x240";
        }
        if (type == typeof(PortalSearchFile))
        {
            properties["id"]!["minimum"] = 0; properties["id"]!["maximum"] = 1400;
            properties["bytes"]!["minimum"] = 1; properties["bytes"]!["maximum"] = 26 * 1024 * 1024;
            properties["sha256"]!["pattern"] = "^[a-f0-9]{64}$";
            properties["mediaType"]!["enum"] = Strings("video/webm", "application/json", "image/jpeg", "text/html", "text/plain");
        }
        if (type == typeof(PortalSearchFilePage))
        { properties["files"]!["minItems"] = 1; properties["files"]!["maxItems"] = PortalSearchTasks.FilePageSize; }
        if (type == typeof(PortalSearchOriginalPage))
        {
            properties["data"]!["contentEncoding"] = "base64"; properties["data"]!["maxLength"] = 87384;
            properties["pageSha256"]!["pattern"] = "^[a-f0-9]{64}$";
        }
        if (type == typeof(PortalSearchDiagnostics))
        { properties["schema"]!["const"] = "hsr-search-task-diagnostics-v1"; properties["publicationAttempts"]!["maxItems"] = 3; }
        if (type == typeof(PortalSearchPublicationAttempt))
        { properties["number"]!["minimum"] = 1; properties["number"]!["maximum"] = 3; properties["websiteInputRetried"]!["const"] = false; }
    }
}
