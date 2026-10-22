using System.Text.Json;

namespace HeadlessScreenReader.Cli.HostedControl;

internal static class PortalAssets
{
    public static string JavaScript => Resource("portal.js");
    public static string Css => Resource("portal.css");
    private static string Resource(string name)
    {
        using var stream = typeof(PortalAssets).Assembly.GetManifestResourceStream("HeadlessScreenReader.Cli.HostedControl." + name)
            ?? throw new InvalidOperationException("CONTROL_ASSET_UNAVAILABLE");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public const string Html = """
        <!doctype html>
        <html lang="en">
        <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>HeadlessScreenReader workspace</title><link rel="stylesheet" href="/portal.css"><script src="/portal.js" defer></script></head>
        <body><a class="skip" href="#workspace">Skip to workspace</a><main>
        <header><h1>HeadlessScreenReader</h1><p>Run the bundled shop and inspect the reader’s observations.</p></header>
        <section id="account" aria-labelledby="account-title">
          <h2 id="account-title">Sign in to your workspace</h2>
          <p>New accounts need email confirmation. Your reader runs stay private to your workspace.</p>
          <form id="sign-in" method="post" action="/">
            <label for="email">Email</label><input id="email" name="email" type="email" autocomplete="username" required maxlength="320">
            <label for="password">Password</label><input id="password" name="password" type="password" autocomplete="current-password" required maxlength="256">
            <div class="actions"><button id="login" class="primary" type="submit" disabled>Sign in</button>
            <button id="signup" type="button" disabled>Create account</button></div>
          </form>
        </section>
        <div class="state" aria-live="polite"><h2 id="state-title">Connecting</h2><p id="state-message">Preparing your private workspace.</p></div>
        <p id="error" role="alert" hidden></p>
        <button id="reconnect" type="button" hidden>Retry connection</button>
        <section id="workspace" aria-labelledby="workspace-title" hidden>
          <div class="workspace-head"><div><h2 id="workspace-title">Your workspace</h2><p id="signed-in"></p></div>
          <button id="logout" type="button">Sign out</button></div>
          <div class="actions"><button id="run" class="primary" type="button">Run shop fixture</button>
            <button id="refresh" type="button">Refresh runs</button></div>
          <p>The bundled shop is a connection and observation test. It sends no browser input and does not assess goal completion.</p>
          <div class="workspace-grid">
            <section aria-labelledby="runs-title"><h2 id="runs-title">Recent runs</h2>
              <p id="runs-empty">No runs yet. Run the shop fixture to capture its exposed controls.</p>
              <ul id="runs"></ul><p id="more" hidden>Showing the fifty most recent runs. Earlier results remain stored.</p>
            </section>
            <section aria-labelledby="result-title"><h2 id="result-title">Reader result</h2>
              <p id="result-empty">Select a run to see its observation and evidence status.</p>
              <div id="result" hidden><dl id="facts"></dl><p id="result-message"></p>
                <button id="download" type="button" disabled>Download report</button>
                <details id="integrity"><summary>Evidence details</summary><p id="digest"></p></details>
                <h3 id="nodes-title" hidden>Exposed semantics</h3><ol id="nodes"></ol>
                <ul id="limitations"></ul>
              </div>
            </section>
          </div>
        </section>
        <footer>Observations describe this fixture run. They do not establish accessibility conformance or an independently completed objective.</footer>
        </main></body></html>
        """;

    public static string OpenApi => JsonSerializer.Serialize(new
    {
        openapi = "3.1.0",
        info = new { title = "Private reader workspace", version = "1", description = "Fixed fixture admission and tenant-scoped verified reports. Public MCP is deferred." },
        paths = new Dictionary<string, object>
        {
            ["/"] = new { get = Operation("PortalHome", "Read the private workspace page", false) },
            ["/portal.js"] = new { get = Operation("PortalScript", "Read the workspace script", false) },
            ["/portal.css"] = new { get = Operation("PortalStyles", "Read the workspace styles", false) },
            ["/openapi/v1.json"] = new { get = Operation("PortalOpenApi", "Read this protocol description", false) },
            ["/api/config"] = new { get = Operation("PortalConfiguration", "Read public Auth configuration", false) },
            ["/internal/ready"] = new { get = Operation("PortalReadiness", "Actual loopback listener readiness, not a tenant JWT assertion", false) },
            ["/api/session"] = new { get = Operation("PortalSession", "Read Auth-server-validated current identity", true) },
            ["/api/workspace"] = new { post = Operation("EnsurePersonalWorkspace", "Idempotently create or resume this subject's personal workspace", true, "EmptyRequest", 201) },
            ["/api/workspaces/{organization}"] = new { get = Operation("GetPortalWorkspace", "Read a currently authorized workspace", true, organization: true) },
            ["/api/workspaces/{organization}/scans"] = new
            {
                get = Operation("ListPortalScans", "Read recent runs with an explicit hasMore flag", true, organization: true),
                post = Operation("CreatePortalScan", "Admit the fixed shop fixture; Location identifies the report", true, "CreateScan", 201, true)
            },
            ["/api/workspaces/{organization}/scans/{scan}"] = new
            { get = Operation("GetPortalReport", "Read exact hash- and identity-verified evidence; missing evidence remains explicit", true, organization: true, scan: true) }
        },
        components = new
        {
            securitySchemes = new { bearerAuth = new { type = "http", scheme = "bearer", bearerFormat = "JWT" } },
            schemas = new Dictionary<string, object>
            {
                ["EmptyRequest"] = new { type = "object", additionalProperties = false },
                ["CreateScan"] = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "requestId" },
                    properties = new { requestId = new { type = "string", format = "uuid", description = "Reuse this ID after an unconfirmed admission" } }
                }
            }
        }
    });

    private static object Operation(string id, string summary, bool authenticated, string? body = null,
        int success = 200, bool organization = false, bool scan = false)
    {
        var operation = new Dictionary<string, object>
        {
            ["operationId"] = id,
            ["summary"] = summary,
            ["responses"] = new Dictionary<string, object>
            {
                [success.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new { description = success == 201 ? "Created or idempotently replayed resource; Location header identifies it" : "Successful scoped response" },
                ["400"] = new { description = "Invalid request" },
                ["401"] = new { description = "Authentication required" },
                ["403"] = new { description = "Current authorization denied" },
                ["404"] = new { description = "Resource not visible" },
                ["409"] = new { description = "Request identity conflict" },
                ["503"] = new { description = "Operation or evidence unconfirmed" }
            }
        };
        if (id == "EnsurePersonalWorkspace")
            ((Dictionary<string, object>)operation["responses"])["200"] = new { description = "Existing personal workspace resumed" };
        if (authenticated) operation["security"] = new[] { new Dictionary<string, string[]> { ["bearerAuth"] = [] } };
        if (body is not null) operation["requestBody"] = new
        {
            required = true,
            content = new Dictionary<string, object> { ["application/json"] = new { schema = new Dictionary<string, string> { ["$ref"] = "#/components/schemas/" + body } } }
        };
        var parameters = new List<object>();
        if (organization) parameters.Add(new { name = "organization", @in = "path", required = true, schema = new { type = "string", format = "uuid" } });
        if (scan) parameters.Add(new { name = "scan", @in = "path", required = true, schema = new { type = "string", format = "uuid" } });
        if (parameters.Count > 0) operation["parameters"] = parameters;
        return operation;
    }
}
