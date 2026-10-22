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

    public static string OpenApi => PortalOpenApi.Document;
}
