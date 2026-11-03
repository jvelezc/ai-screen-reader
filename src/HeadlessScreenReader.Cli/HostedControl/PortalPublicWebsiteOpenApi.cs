using System.Text.Json.Nodes;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Cli.HostedControl;

internal static partial class PortalOpenApi
{
    private static void AddPublicWebsiteSchemas(JsonObject schemas)
    {
        schemas["CreatePublicWebsite"] = Schema<PortalCreatePublicWebsiteRequest>();
        schemas["PublicWebsiteCreated"] = Schema<PortalPublicWebsiteCreatedResponse>();
        schemas["PublicWebsiteSummary"] = Schema<PortalPublicWebsiteSummaryResponse>();
        schemas["PublicWebsiteList"] = Schema<PortalPublicWebsiteListResponse>("PublicWebsiteList");
        schemas["PublicWebsiteStatus"] = Schema<PortalPublicWebsiteStatusResponse>("PublicWebsiteStatus");
        schemas["PublicWebsiteNodes"] = Schema<PortalPublicWebsiteNodePageResponse>("PublicWebsiteNodes");
        schemas["PublicWebsiteOriginal"] = Schema<PortalPublicWebsiteOriginalPageResponse>("PublicWebsiteOriginal");
    }

    private static void AddPublicWebsitePaths(JsonObject paths)
    {
        const string root = "/api/workspaces/{organization}/website-scans";
        var admission = Operation("AdmitPublicWebsite", "Queue one approved public HTTPS source. Reuse the same request UUID/source for explicit reconciliation; admission never acknowledges execution or evidence sufficiency.",
            "PublicWebsiteCreated", true, "CreatePublicWebsite", 202, organization: true);
        admission["responses"]!["202"]!["headers"] = new JsonObject
        {
            ["Location"] = new JsonObject
            {
                ["description"] = "Scoped status resource; no execution claim",
                ["schema"] = new JsonObject { ["type"] = "string", ["format"] = "uri-reference" }
            }
        };
        admission["responses"]!["429"] = Response("Workspace queue limit reached; reconcile an existing request before submitting new work", "Problem", "application/problem+json");
        paths[root] = new JsonObject
        {
            ["get"] = Operation("ListPublicWebsites", "At most twenty recent source metadata rows and128KiB; hasMore preserves omitted history. Never loads originals.", "PublicWebsiteList", true, organization: true),
            ["post"] = admission
        };
        paths[root + "/{scan}"] = Path("get", Operation("ReadPublicWebsiteStatus", "Fresh atomic job/clock-bound permission metadata. Active is a historical snapshot, never execution, renewal or publication authority.",
            "PublicWebsiteStatus", true, organization: true, scan: true));
        paths[root + "/{scan}/observations/{capture}/{offset}/{limit}"] = Path("get", PublicNodes(false));
        paths[root + "/{scan}/observations/{capture}/{offset}/{limit}/{digest}"] = Path("get", PublicNodes(true));
        var original = Operation("ReadPublicWebsiteOriginal", "Read at most64KiB of verified original observation JSON. Exact64KiB boundary or EOF; pin original SHA-256, verify page/whole hashes and reauthorize EOF. No signed URL, native path or private lease is returned.",
            "PublicWebsiteOriginal", true, organization: true, scan: true);
        var parameters = (JsonArray)original["parameters"]!;
        parameters.Add(PublicParameter("offset", 0, PublicWebsiteObservation.MaximumBytes));
        parameters.Add(PublicDigestParameter());
        paths[root + "/{scan}/original/{offset}/{digest}"] = Path("get", original);

        static JsonObject PublicNodes(bool pinned)
        {
            var result = Operation(pinned ? "ReadPublicWebsitePinnedNodes" : "ReadPublicWebsiteFirstNodes",
                "Verify the exact scoped producer/original before and after retrieval, then return at most100 nodes/128KiB. Nonzero offsets require the original digest. Passive snapshots leave task outcome and evidence sufficiency NotAssessed.",
                "PublicWebsiteNodes", true, organization: true, scan: true);
            var values = (JsonArray)result["parameters"]!;
            values.Add(PublicParameter("capture", 0, 1)); values.Add(PublicParameter("offset", 0, 2048));
            values.Add(PublicParameter("limit", 1, 100)); if (pinned) values.Add(PublicDigestParameter());
            return result;
        }
    }

    private static JsonObject PublicParameter(string name, int minimum, int maximum) => new()
    {
        ["name"] = name,
        ["in"] = "path",
        ["required"] = true,
        ["schema"] = new JsonObject { ["type"] = "integer", ["minimum"] = minimum, ["maximum"] = maximum }
    };
    private static JsonObject PublicDigestParameter() => new()
    {
        ["name"] = "digest",
        ["in"] = "path",
        ["required"] = true,
        ["schema"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[a-f0-9]{64}$" }
    };

    private static void ApplyPublicWebsiteSchema(Type type, JsonObject value, JsonObject properties)
    {
        if (type == typeof(PortalCreatePublicWebsiteRequest))
        {
            properties["url"]!["minLength"] = 1; properties["url"]!["maxLength"] = 2048;
            properties["url"]!["format"] = "uri";
            value["description"] = "A nonempty request UUID and credential-free public HTTPS DNS source. Unsupported schemes, ports, IP literals, fragments and malformed spelling are rejected before normalization.";
        }
        if (type == typeof(PortalPublicWebsiteCreatedResponse) || type == typeof(PortalPublicWebsiteSummaryResponse) || type == typeof(PortalPublicWebsiteStatusResponse))
        {
            properties["readerProfile"]!["const"] = PublicReaderSource.CurrentProfile;
            properties["targetUrl"]!["maxLength"] = 2048; properties["targetUrl"]!["format"] = "uri";
            properties["allowedHost"]!["maxLength"] = 253;
        }
        if (type == typeof(PortalPublicWebsiteSummaryResponse)) properties["state"]!["enum"] = Strings("queued", "leased", "observed", "blocked", "cancelled");
        if (type == typeof(PortalPublicWebsiteListResponse))
        {
            properties["items"]!["maxItems"] = 20; properties["items"]!["items"] = Reference("PublicWebsiteSummary");
            value["description"] = "Recent metadata only. The actual UTF-8 response fits128KiB; hasMore is true when any row is omitted by count or byte limits.";
        }
        if (type == typeof(PortalPublicWebsiteStatusResponse))
        {
            properties["state"]!["enum"] = Strings("queued", "leased", "completed", "blocked", "cancelled");
            properties["generation"]!["minimum"] = 0; properties["generation"]!["maximum"] = 3;
            properties["originalSha256"]!["pattern"] = "^[a-f0-9]{64}$";
            properties["errorCode"]!["pattern"] = "^PUBLIC_WEBSITE_[A-Z_]+$";
            value["description"] = "Original bytes and producer exist only after completed work. A leased job includes an exact job/generation clock snapshot; private capabilities and payload are never metadata fields.";
            value["allOf"] = JsonNode.Parse("""
                [{"if":{"properties":{"state":{"const":"queued"}}},"then":{"properties":{"generation":{"const":0}}}},
                 {"if":{"properties":{"state":{"enum":["leased","completed"]}}},"then":{"properties":{"generation":{"minimum":1}}}},
                 {"if":{"properties":{"state":{"const":"completed"}}},"then":{"properties":{"producerId":{"type":"string","format":"uuid"},"originalSha256":{"type":"string","pattern":"^[a-f0-9]{64}$"},"errorCode":{"const":null}}},"else":{"properties":{"producerId":{"const":null},"originalSha256":{"const":null}}}},
                 {"if":{"properties":{"state":{"const":"leased"}}},"then":{"properties":{"workerPermission":{"type":"object"}}},"else":{"properties":{"workerPermission":{"const":null}}}}]
                """);
        }
        if (type == typeof(PortalPublicWebsiteNodePageResponse))
        {
            properties["scope"]!["const"] = "PageObservationsOnly"; properties["goalVerification"]!["const"] = "NotAssessed";
            properties["dispatchedInputs"]!["const"] = 0; properties["captures"]!["const"] = 2;
            properties["captureIndex"]!["minimum"] = 0; properties["captureIndex"]!["maximum"] = 1;
            properties["captureEpoch"]!["minimum"] = 1; properties["totalNodes"]!["minimum"] = 1; properties["totalNodes"]!["maximum"] = 2048;
            properties["offset"]!["minimum"] = 0; properties["offset"]!["maximum"] = 2048; properties["nodes"]!["maxItems"] = 100;
            properties["originalSha256"]!["pattern"] = "^[a-f0-9]{64}$";
        }
        if (type == typeof(PortalPublicWebsiteOriginalPageResponse))
        {
            properties["originalSha256"]!["pattern"] = "^[a-f0-9]{64}$"; properties["pageSha256"]!["pattern"] = "^[a-f0-9]{64}$";
            properties["totalBytes"]!["minimum"] = 1; properties["totalBytes"]!["maximum"] = PublicWebsiteObservation.MaximumBytes;
            properties["offset"]!["minimum"] = 0; properties["offset"]!["maximum"] = PublicWebsiteObservation.MaximumBytes;
            properties["data"]!["maxLength"] = 87384; properties["data"]!["contentEncoding"] = "base64";
            value["description"] = "At most65536 original bytes per page, with exact source and page SHA-256. Follow nextOffset, compare the whole digest and reauthorize EOF before claiming a complete saved copy.";
        }
    }
}
