using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

public sealed class PublicTaskNavigationTests
{
    private static readonly PublicTaskGoal Goal = new("Pricing", "Reach pricing information", "View pricing", "https://example.com/pricing");
    private static PublicTaskFocus Focus(int number) => new($"node-{number}", "link", "Repeated label", "https://example.com/info");

    [Fact]
    public void PublishedPageSelectorsAcceptLaterOffsetsWithoutChangingPerPageBounds()
    {
        using var document = JsonDocument.Parse(PortalOpenApi.Document);
        var paths = document.RootElement.GetProperty("paths");
        foreach (var kind in new[] { "events", "reader", "graph" })
        {
            var endpoint = paths.GetProperty("/api/workspaces/{organization}/website-scans/{scan}/tasks/{job}/" + kind + "/{attempt}/{offset}/{digest}").GetProperty("get");
            var offset = endpoint.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "offset");
            Assert.Equal(int.MaxValue, offset.GetProperty("schema").GetProperty("maximum").GetInt32());
            Assert.True(endpoint.GetProperty("security")[0].TryGetProperty("bearerAuth", out _));
        }
    }

    [Theory]
    [InlineData(65)]
    [InlineData(256)]
    public void TraversalHasNoFixedTabCountAndStopsOnlyAtTheObservedGoal(int count)
    {
        var path = new PublicTaskKeyboardPath(Goal);
        for (var i = 0; i < count; i++) Assert.True(path.Continue("document", Focus(i), i));
        Assert.False(path.Continue("document", Focus(count) with { AnchorUrl = Goal.DestinationUrl }, count));
        Assert.Equal(new(PublicTaskNavigation.CurrentStrategy, "GoalFocused", count, count, null), path.Record());
    }

    [Fact]
    public void RepeatedFocusStopsTheCurrentRouteWithoutInventingActivationOrUniversalUnreachability()
    {
        var path = new PublicTaskKeyboardPath(Goal);
        Assert.True(path.Continue("document", Focus(1), 0));
        Assert.True(path.Continue("document", Focus(2), 1));
        Assert.False(path.Continue("document", Focus(1), 2));
        Assert.Equal(new(PublicTaskNavigation.CurrentStrategy, "FocusCycle", 2, 2, 0), path.Record());
        var original = PublicTaskTests.Report(); var attempt = original.Attempts[0] with
        {
            Events = [new(1, 1000, "Tab", Focus(1), Focus(2), original.Start.Job.Source.TargetUrl, true, true),
                new(2, 2000, "Tab", Focus(2), Focus(1), original.Start.Job.Source.TargetUrl, true, true)],
            Navigation = path.Record(),
            Outcome = "Unconfirmed"
        };
        original = original with { Attempts = original.Attempts.SetItem(0, attempt) };
        Assert.Equal("InsufficientEvidence", PublicTaskAssessment.Evaluate(original, true, true).Outcome);
        Assert.DoesNotContain(attempt.Events, e => e.Action == "Enter");
    }

    [Fact]
    public void UnknownFocusAndUnconfirmedInputRemainExplicitAndAreNotRetried()
    {
        var path = new PublicTaskKeyboardPath(Goal);
        Assert.False(path.Continue("document", null, 0));
        Assert.Equal("FocusUnobserved", path.Record().StopReason);
        path.Unconfirmed(1);
        Assert.Equal("InputUnconfirmed", path.Record().StopReason);
        Assert.Null(path.Record().CycleStartSequence);
        var fresh = new PublicTaskKeyboardPath(Goal);
        Assert.False(fresh.Continue(null, Focus(1), 0));
        Assert.Equal("FocusUnobserved", fresh.Record().StopReason);
    }

    [Fact]
    public void NodeIdentityFromAnotherDocumentIsNotMistakenForAFocusCycle()
    {
        var path = new PublicTaskKeyboardPath(Goal);
        Assert.True(path.Continue("first", Focus(1), 0));
        Assert.True(path.Continue("second", Focus(1), 1));
        Assert.False(path.Continue("second", Focus(1), 2));
        Assert.Equal(1, path.Record().CycleStartSequence);
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task ActualChromiumReaderReachesAndActivatesAServiceAfterSeventyControls()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync();
        await browser.DiagnosticPage.SetContentAsync("<h1>Task</h1>" + string.Concat(Enumerable.Range(0, 70)
            .Select(i => $"<button>Control {i}</button>")) + "<a id='goal' href='https://example.com/pricing'>View pricing</a>");
        await browser.DiagnosticPage.EvaluateAsync("() => document.querySelector('#goal').addEventListener('click', e => {e.preventDefault();document.querySelector('h1').textContent='Pricing opened';})");
        await using var navigation = new PublicNavigationBrowser(browser, PublicTaskTests.Start().Job, Path.GetTempPath());
        var time = TimeProvider.System;
        using var reader = new PublicTaskReaderController(browser, navigation.FocusFromSnapshotAsync, time, time.GetTimestamp());
        await reader.ExecuteAsync("Observe", default);
        var path = new PublicTaskKeyboardPath(Goal); var tabs = 0;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        while (path.Continue(reader.Snapshot?.DocumentId, reader.Focus, tabs))
        {
            var record = await reader.ExecuteAsync("Tab", stop.Token);
            Assert.Equal("Observed", record.Outcome); Assert.True(reader.InputCompleted); tabs++;
        }
        Assert.Equal("GoalFocused", path.Record().StopReason);
        Assert.Equal(71, tabs);
        Assert.Equal(Goal.DestinationUrl, reader.Focus?.AnchorUrl);
        Assert.Contains("View pricing", reader.LastRecord!.Announcement, StringComparison.Ordinal);
        var activation = await reader.ExecuteAsync("Enter", stop.Token);
        Assert.Equal("Observed", activation.Outcome); Assert.Equal("Activate", activation.Command);
        Assert.Equal(73, activation.Sequence); activation.Validate(ScreenReaderSession.Profile);
        Assert.Contains(reader.Snapshot!.Nodes, n => n.Role == "heading" && n.Name == "Pricing opened");
    }

    internal static PublicTaskReport LongReport(PublicTaskReport original)
    {
        var report = original with
        {
            Attempts = original.Attempts.Select(a => a.Method != "Keyboard" ? a : a with
            {
                Events = Enumerable.Range(1, 140).Select(n => a.Events[0] with
                {
                    Sequence = n,
                    Action = n == 140 ? "Enter" : "Tab",
                    ElapsedMilliseconds = n * 4000
                }).ToImmutableArray()
            }).ToImmutableArray()
        };
        return PublicTaskReaderTests.WithReader(report);
    }

    [Fact]
    public async Task OriginalBoundHttpPagesPreserveReaderEventsAndGraphsBeyondTheOldCutoff()
    {
        using var rig = await PublicTaskHttpTests.Rig.Create(LongReport);
        await using var server = await rig.Server(); using var http = rig.Http(server);
        var digest = rig.Status.ReportSha256;
        using var response = await http.GetAsync(rig.Path + $"/reader/1/120/{digest}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reader = (await response.Content.ReadFromJsonAsync<PortalTaskReaderPage>(PortalJson.Options))!;
        Assert.Equal(140, reader.Total); Assert.Equal(132, reader.NextOffset); Assert.Null(reader.Initial);
        Assert.Equal(121, reader.Events[0].Sequence); Assert.Equal(122, reader.Events[0].Reader!.Sequence);
        Assert.True(reader.Events[0].Reader!.FinishedMilliseconds > 180000);
        using var eventsResponse = await http.GetAsync(rig.Path + $"/events/1/120/{digest}");
        Assert.Equal(HttpStatusCode.OK, eventsResponse.StatusCode);
        var events = (await eventsResponse.Content.ReadFromJsonAsync<PortalTaskEventPage>(PortalJson.Options))!;
        Assert.Equal(140, events.Total); Assert.Equal(20, events.Events.Length); Assert.Null(events.NextOffset);
        using var graphResponse = await http.GetAsync(rig.Path + $"/graph/1/120/{digest}");
        Assert.Equal(HttpStatusCode.OK, graphResponse.StatusCode);
        var graph = (await graphResponse.Content.ReadFromJsonAsync<PortalTaskGraphPage>(PortalJson.Options))!;
        Assert.Equal(140, graph.TotalEdges); Assert.Equal(121, graph.Edges[0].Sequence);
        Assert.Equal(digest, graph.ReportSha256); Assert.Equal(0, rig.Creates);
    }
}
