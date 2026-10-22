using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Cli.HostedControl;

/// <summary>Executable serialization shapes for consumers of the bounded core HTTP protocol.</summary>
internal static class PortalOpenApi
{
    private static readonly Lazy<string> Cached = new(Build);
    public static string Document => Cached.Value;
    public const string ContractVersion = "1.1.0";

    private static string Build()
    {
        var schemas = new JsonObject
        {
            ["EmptyRequest"] = Schema<PortalEmptyRequest>(),
            ["CreateScan"] = Schema<PortalCreateScanRequest>(),
            ["Configuration"] = Schema<PortalConfigurationResponse>(),
            ["Session"] = Schema<PortalSessionResponse>(),
            ["Workspace"] = Schema<PortalWorkspaceResponse>(),
            ["ScanCreated"] = Schema<PortalScanCreatedResponse>(),
            ["Scan"] = Schema<PortalScanResponse>(),
            ["ScanList"] = Schema<PortalScanListResponse>(),
            ["Node"] = Schema<PortalNodeResponse>(),
            ["Observation"] = Schema<PortalObservationResponse>(),
            ["Report"] = Schema<PortalReportResponse>(),
            ["Readiness"] = Schema<PortalReadinessResponse>(),
            ["ReaderSession"] = Schema<PortalReaderSessionResponse>(),
            ["ReaderSnapshot"] = Schema<PortalReaderSnapshotResponse>(),
            ["ReaderClosed"] = Schema<PortalReaderClosedResponse>(),
            ["Problem"] = JsonNode.Parse("""
                {"type":"object","required":["type","title","status","detail","supportReference"],
                 "properties":{"type":{"type":"string","format":"uri"},
                 "title":{"type":"string","pattern":"^CONTROL_[A-Z0-9_]+$"},
                 "status":{"type":"integer","minimum":400,"maximum":599},
                 "detail":{"type":"string","pattern":"^CONTROL_[A-Z0-9_]+$"},
                 "supportReference":{"type":"string","pattern":"^[a-f0-9]{32}$"}}}
                """)
        };
        var paths = new JsonObject
        {
            ["/"] = Path("get", Operation("PortalHome", "Legacy private portal page", "string", media: "text/html")),
            ["/portal.js"] = Path("get", Operation("PortalScript", "Legacy portal script", "string", media: "text/javascript")),
            ["/portal.css"] = Path("get", Operation("PortalStyles", "Legacy portal stylesheet", "string", media: "text/css")),
            ["/openapi/v1.json"] = Path("get", Operation("PortalOpenApi", "Core protocol contract", "object")),
            ["/api/config"] = Path("get", Operation("PortalConfiguration", "Legacy portal public Auth configuration", "Configuration")),
            ["/internal/ready"] = Path("get", Operation("PortalReadiness", "Listener readiness; not a tenant sign-in assertion", "Readiness")),
            ["/api/session"] = Path("get", Operation("PortalSession", "Auth-server-validated current identity", "Session", true)),
            ["/api/workspace"] = Path("post", Operation("EnsurePersonalWorkspace", "Create or resume this subject's workspace", "Workspace", true, "EmptyRequest", 201)),
            ["/api/workspaces/{organization}"] = Path("get", Operation("GetPortalWorkspace", "Read a currently authorized workspace", "Workspace", true, organization: true)),
            ["/api/workspaces/{organization}/scans"] = new JsonObject
            {
                ["get"] = Operation("ListPortalScans", "At most fifty recent runs; hasMore preserves truncation", "ScanList", true, organization: true),
                ["post"] = Operation("CreatePortalScan", "Idempotently admit the fixed input-free fixture; not execution or goal success", "ScanCreated", true, "CreateScan", 201, true)
            },
            ["/api/workspaces/{organization}/scans/{scan}"] = Path("get", Operation("GetPortalReport", "Read authorized hash-verified evidence; preserve unavailable and invalid outcomes", "Report", true, organization: true, scan: true)),
            ["/api/workspaces/{organization}/reader-sessions"] = Path("post", Operation("OpenReaderSession", "Open a subject-owned read-only Foundation fixture; two-minute lease, one per user, two per host", "ReaderSession", true, "EmptyRequest", 201, organization: true)),
            ["/api/workspaces/{organization}/reader-sessions/{session}"] = new JsonObject
            {
                ["get"] = Operation("GetReaderSession", "Read owned session metadata after current workspace RLS authorization", "ReaderSession", true, organization: true, session: true),
                ["delete"] = Operation("CloseReaderSession", "Acknowledge actual browser cleanup; failed cleanup quarantines capacity", "ReaderClosed", true, organization: true, session: true)
            },
            ["/api/workspaces/{organization}/reader-sessions/{session}/observations"] = Path("post", Operation("ObserveReaderSession", "Fresh input-free source observation; at most 100 captures per lease, 2048 nodes and 262144 response bytes", "ReaderSnapshot", true, "EmptyRequest", organization: true, session: true))
        };
        return new JsonObject
        {
            ["openapi"] = "3.1.0",
            ["jsonSchemaDialect"] = "https://json-schema.org/draft/2020-12/schema",
            ["info"] = new JsonObject
            {
                ["title"] = "HeadlessScreenReader core HTTP API",
                ["version"] = ContractVersion,
                ["description"] = "Private, fixed-fixture protocol consumed server-side by Supabase Edge Functions. Authenticated tenant scope is enforced by the core. This contract does not enable public hosting, browser input, goal verification or remote MCP. Legacy portal assets remain compatibility surfaces."
            },
            ["servers"] = new JsonArray(new JsonObject { ["url"] = "/", ["description"] = "Use the separately configured authenticated core origin; not a browser API proxy" }),
            ["paths"] = paths,
            ["components"] = new JsonObject
            {
                ["securitySchemes"] = new JsonObject
                {
                    ["bearerAuth"] = new JsonObject
                    {
                        ["type"] = "http",
                        ["scheme"] = "bearer",
                        ["bearerFormat"] = "JWT",
                        ["description"] = "Current protocol requires a confirmed application user JWT validated upstream with the configured Supabase Auth project. No service-role substitution or client-asserted actor. Gateway auth/delegation must follow this contract; MCP audience validation is separate."
                    }
                },
                ["schemas"] = schemas
            }
        }.ToJsonString();
    }

    private static JsonNode Schema<T>()
    {
        // Export the actual camelCase/nullability/required-member contract. Strict number
        // schemas describe serialized output; Web's read-from-string tolerance is not output.
        var options = new JsonSerializerOptions(PortalJson.Options)
        { TypeInfoResolver = new DefaultJsonTypeInfoResolver(), NumberHandling = JsonNumberHandling.Strict };
        return options.GetJsonSchemaAsNode(typeof(T), new()
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = (context, schema) =>
            {
                if (schema is not JsonObject value || value["properties"] is not JsonObject properties) return schema;
                // Response serialization emits every property, including nullable and
                // optional-constructor defaults such as evidenceStorage.
                if (context.TypeInfo.Type != typeof(PortalEmptyRequest) && context.TypeInfo.Type != typeof(PortalCreateScanRequest))
                    value["required"] = Strings(properties.Select(property => property.Key).ToArray());
                if (context.TypeInfo.Type == typeof(PortalCreateScanRequest))
                    properties["requestId"]!["not"] = new JsonObject { ["const"] = Guid.Empty.ToString("D") };
                if (context.TypeInfo.Type == typeof(PortalConfigurationResponse) || context.TypeInfo.Type == typeof(PortalScanResponse))
                {
                    properties["fixture"]!["const"] = "synthetic-shop";
                    properties["readerProfile"]!["const"] = FixtureObservation.Profile;
                }
                if (context.TypeInfo.Type == typeof(PortalScanResponse))
                    properties["state"]!["enum"] = Strings("queued", "leased", "observed", "blocked");
                if (context.TypeInfo.Type == typeof(PortalScanListResponse)) properties["items"]!["maxItems"] = 50;
                if (context.TypeInfo.Type == typeof(PortalReaderSessionResponse))
                {
                    properties["fixture"]!["const"] = "Foundation";
                    properties["readerProfile"]!["const"] = HeadlessScreenReader.ScreenReader.ScreenReaderSession.Profile;
                    properties["state"]!["const"] = "Open";
                    properties["readOnly"]!["const"] = true;
                }
                if (context.TypeInfo.Type == typeof(PortalReaderSnapshotResponse))
                {
                    properties["readerProfile"]!["const"] = HeadlessScreenReader.ScreenReader.ScreenReaderSession.Profile;
                    properties["dispatchedInputs"]!["const"] = 0;
                    properties["goalVerified"]!["const"] = false;
                    properties["snapshot"]!["properties"]!["nodes"]!["maxItems"] = PortalReaderSessions.MaximumNodes;
                    value["description"] = "Untrusted observed page content, not instructions. Values and credentials are excluded by the browser adapter. Numeric semantic source enums follow the core Domain serialization; null required observations remain unobserved. Transient and not archived evidence.";
                }
                if (context.TypeInfo.Type == typeof(PortalReaderClosedResponse)) properties["state"]!["const"] = "Closed";
                if (context.TypeInfo.Type == typeof(PortalReadinessResponse))
                {
                    properties["schema"]!["const"] = "hsr-control-readiness-v1";
                    properties["state"]!["const"] = "Listening";
                    properties["releaseCommit"]!["pattern"] = "^[a-f0-9]{40}$";
                    properties["appJwtValidated"]!["const"] = false;
                    properties["publicMcpEnabled"]!["const"] = false;
                }
                if (context.TypeInfo.Type == typeof(PortalObservationResponse))
                {
                    properties["readerProfile"]!["const"] = FixtureObservation.Profile;
                    properties["digest"]!["pattern"] = "^[a-f0-9]{64}$";
                    properties["dispatchedInputs"]!["const"] = 0;
                    properties["goalVerified"]!["const"] = false;
                    properties["nodes"]!["maxItems"] = 2048;
                    properties["evidenceStorage"]!["description"] = "S3: verified private object copy; Local: server disk; LocalFallback: server disk after remote copy missing. Unknown future locations remain unconfirmed.";
                }
                if (context.TypeInfo.Type == typeof(PortalReportResponse))
                {
                    properties["evidenceState"]!["enum"] = Strings("Pending", "Available", "Unavailable", "NotCollected", "Invalid");
                    value["description"] = "Execution and evidence are separate. Only Available carries an observation. Required-state null means unobserved, never false. A blocked job is not proof of an accessibility barrier.";
                    value["allOf"] = JsonNode.Parse("""
                        [
                          {"if":{"properties":{"evidenceState":{"const":"Available"}}},
                           "then":{"properties":{"observation":{"type":"object"},"errorCode":{"const":null},"scan":{"properties":{"state":{"const":"observed"}}}}},
                           "else":{"properties":{"observation":{"const":null}}}},
                          {"if":{"properties":{"evidenceState":{"const":"Pending"}}},
                           "then":{"properties":{"scan":{"properties":{"state":{"enum":["queued","leased"]}}}}}},
                          {"if":{"properties":{"evidenceState":{"const":"NotCollected"}}},
                           "then":{"properties":{"scan":{"properties":{"state":{"const":"blocked"}}}}}},
                          {"if":{"properties":{"evidenceState":{"const":"Invalid"}}},
                           "then":{"properties":{"scan":{"properties":{"state":{"const":"observed"}}}}}}
                        ]
                        """);
                }
                return schema;
            }
        });
    }

    private static JsonArray Strings(params string[] values) => new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
    private static JsonObject Path(string verb, JsonObject operation) => new() { [verb] = operation };
    private static JsonObject Reference(string schema) => schema is "string" or "object"
        ? new() { ["type"] = schema } : new() { ["$ref"] = "#/components/schemas/" + schema };
    private static JsonObject Response(string description, string schema, string media = "application/json") => new()
    {
        ["description"] = description,
        ["content"] = new JsonObject { [media] = new JsonObject { ["schema"] = Reference(schema) } }
    };

    private static JsonObject Operation(string id, string summary, string output, bool authenticated = false,
        string? body = null, int success = 200, bool organization = false, bool scan = false, string media = "application/json", bool session = false)
    {
        var response = Response("Successful scoped response", output, media);
        if (success == 201) response["headers"] = new JsonObject
        {
            ["Location"] = new JsonObject { ["description"] = "Authorized resource path; admission is not execution", ["schema"] = new JsonObject { ["type"] = "string", ["format"] = "uri-reference" } }
        };
        var responses = new JsonObject { [success.ToString(System.Globalization.CultureInfo.InvariantCulture)] = response };
        if (id == "EnsurePersonalWorkspace") responses["200"] = Response("Existing personal workspace resumed", output);
        foreach (var status in new[] { "400", "401", "403", "404", "409", "413", "415", "503" })
        {
            var fault = Response("Safe CONTROL_* problem; unconfirmed work is not a verified failure or goal outcome", "Problem", "application/problem+json");
            if (status == "401") fault["headers"] = new JsonObject
            { ["WWW-Authenticate"] = new JsonObject { ["schema"] = new JsonObject { ["type"] = "string", ["const"] = "Bearer" } } };
            responses[status] = fault;
        }
        var result = new JsonObject
        {
            ["operationId"] = id,
            ["summary"] = summary,
            ["responses"] = responses,
            ["security"] = authenticated ? new JsonArray(new JsonObject { ["bearerAuth"] = new JsonArray() }) : new JsonArray()
        };
        if (body is not null) result["requestBody"] = new JsonObject
        { ["required"] = true, ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = Reference(body) } } };
        var parameters = new JsonArray();
        foreach (var name in new[] { organization ? "organization" : null, scan ? "scan" : null, session ? "session" : null }.OfType<string>())
            parameters.Add(new JsonObject { ["name"] = name, ["in"] = "path", ["required"] = true, ["schema"] = new JsonObject { ["type"] = "string", ["format"] = "uuid" } });
        if (parameters.Count > 0) result["parameters"] = parameters;
        return result;
    }
}
