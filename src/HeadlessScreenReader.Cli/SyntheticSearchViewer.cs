using System.Collections.Immutable;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using HeadlessScreenReader.Browser.Playwright;

namespace HeadlessScreenReader.Cli;

internal static class SyntheticSearchViewer
{
    internal static string Render(SyntheticSearchSite source, ImmutableArray<MockTaskRun> runs, SearchPointerRecording? pointer,
        string observation, string? stopped)
    {
        static string H(string? value) => WebUtility.HtmlEncode(value ?? "Not retained");
        var headline = observation switch
        {
            "AllTestedKeyboardGoalsObserved" => "The keyboard searches reached the result.",
            "RepeatedKeyboardGoalNotObservedWithPointerGoalObserved" => "The mouse search worked. The keyboard searches did not reach the result.",
            _ => "The search comparison is incomplete."
        };
        const string css = """
            :root{color-scheme:light}*{box-sizing:border-box}body{margin:0;color:#142e3c;background:#faf9f5;font:1.1rem/1.65 Palatino,'Book Antiqua',serif}
            main{max-width:72rem;margin:auto;padding:clamp(1.2rem,5vw,4rem)}h1{font-size:clamp(2rem,4vw,3.3rem);line-height:1.15;max-width:28ch}
            h2{font-size:1.7rem}a{color:#004c99;text-underline-offset:.2em}a:focus-visible,video:focus-visible{outline:3px solid #825118;outline-offset:5px}
            .tag,nav,small,th,td{font-family:'Trebuchet MS',sans-serif}.tag{letter-spacing:.12em;font-size:.78rem;font-weight:bold;color:#725018}
            section{border-top:1px solid #aebcc5;padding:1.5rem 0;margin-top:1rem}video{display:block;max-width:100%;width:40rem;background:#142e3c}
            table{width:100%;border-collapse:collapse;font-size:.95rem}td,th{text-align:left;border-bottom:1px solid #ccd5d9;padding:.7rem;vertical-align:top}
            .table{overflow-x:auto}p{max-width:72ch}li{margin:.6rem 0}code{overflow-wrap:anywhere}nav{display:flex;gap:1rem;flex-wrap:wrap}
            """;
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(css)));
        var html = new StringBuilder("<!doctype html><html lang=en><meta charset=utf-8><meta name=viewport content=\"width=device-width,initial-scale=1\">")
            .Append("<meta http-equiv=Content-Security-Policy content=\"default-src 'none'; style-src 'sha256-").Append(hash)
            .Append("'; media-src 'self'; img-src 'self'; base-uri 'none'; form-action 'none'\"><title>Recorded search comparison</title><style>")
            .Append(css).Append("</style><main><p class=tag>PRIVENSIC · SYNTHETIC SEARCH EXPERIMENT</p><h1>").Append(H(headline))
            .Append("</h1><p>These authored test pages check the reader and recording system. They are not findings about another website or a legal compliance decision.</p><p>Scenario: ")
            .Append(H(source.Id)).Append(". Fixed search: <strong>sample course</strong>. Goal: focus the “Sample course result” heading.</p>")
            .Append("<p>Original clips are available below. Event-to-video timing has not been calibrated for these pages; the viewers do not seek to an invented event time.</p>");
        if (stopped is not null) html.Append("<p><strong>Collection stopped after an incomplete attempt.</strong> No further keyboard or mouse input was sent. Missing runs remain missing.</p>");
        html.Append("<section><h2>Keyboard attempts</h2><p>Three independent runs are planned: Enter, Enter, then Space. Completed native input and an observed search result are separate facts.</p><ol>");
        foreach (var run in runs)
        {
            html.Append("<li>Run ").Append(run.Route).Append(" · ").Append(H(run.Key)).Append(" · Goal: ").Append(H(run.Reachability))
                .Append(" · Completed keys: ").Append(run.CompletedKeys).Append('/').Append(run.AttemptedKeys)
                .Append(". <a href=route-").Append(run.Route).Append("-recording.json>Original run records</a>");
            if (run.RunId is not null) html.Append(" · <a href=route-").Append(run.Route).Append(".html>Watch recording and explore the path</a>");
            html.Append("</li>");
        }
        html.Append("</ol><p>Recorded attempts: ").Append(runs.Length).Append(" of 3.</p></section><section><h2>Independent mouse comparison</h2>");
        if (pointer is null) html.Append("<p>Not attempted. Collection stopped before the mouse comparison.</p>");
        else
        {
            html.Append("<p>Mouse focus, keyboard query entry and mouse activation each have their own input facts. Goal: ")
                .Append(H(pointer.Activation?.GoalObservation ?? "Unavailable")).Append(".</p><nav><a href=pointer.json>Original comparison records</a><a href=pointer-graph.json>Observed states and action graph</a></nav>");
            if (pointer.Media is not null) html.Append("<p><video controls preload=metadata src=pointer-media/original.webm aria-label=\"Original mouse search comparison\"></video></p>");
        }
        return html.Append("</section><section><h2>Keep and verify the originals</h2><nav><a href=fixture-source.txt>Exact test-page source</a><a href=search-manifest.json>File inventory and fingerprints</a></nav><p>Keep the manifest digest separately. Verification detects changes relative to that digest. It does not prove collector identity, actual assistive-technology use, legal standing or admissibility.</p></section></main></html>").ToString();
    }
}
