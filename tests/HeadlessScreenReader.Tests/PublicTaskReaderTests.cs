using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;
using HeadlessScreenReader.Worker;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class PublicTaskReaderTests
{
    [Fact]
    public async Task ReaderContinuesAfterSixtyFourConfirmedKeyboardCommands()
    {
        var time = new FakeTimeProvider(); var browser = new Browser();
        using var reader = new PublicTaskReaderController(browser, Focus, time, time.GetTimestamp());
        await reader.ExecuteAsync("Observe", default);
        PublicTaskReaderRecord? last = null;
        for (var i = 0; i < 65; i++) last = await reader.ExecuteAsync("Tab", default);
        Assert.NotNull(last); Assert.Equal("Observed", last.Outcome);
        Assert.Equal(66, last.Sequence); Assert.Equal(65, browser.Keys.Count);
        Assert.True(reader.InputCompleted); last.Validate(ScreenReaderSession.Profile);
    }

    private static Task<PublicTaskFocus?> Focus(AccessibilitySnapshot snapshot, CancellationToken _) => Task.FromResult(
        snapshot.FocusedNode is { } n ? new PublicTaskFocus(n.Id, n.Role, n.Name, n.Role == "link" ? "https://example.com/pricing" : null) : null);

    [Fact]
    public async Task ReaderTaskUsesRealReaderTabAndRoleBasedActivation()
    {
        var time = new FakeTimeProvider(); var browser = new Browser();
        using var reader = new PublicTaskReaderController(browser, Focus, time, time.GetTimestamp());
        var initial = await reader.ExecuteAsync("Observe", default); Assert.Equal("RootWebArea", browser.Snapshot.FocusedNode!.Role);
        Assert.Equal("ReadCurrent", initial.Command); Assert.Equal(0, initial.BrowserKeystrokes);
        var tab = await reader.ExecuteAsync("Tab", default); var activation = await reader.ExecuteAsync("Enter", default);
        Assert.Equal(new[] { "Tab", "Enter" }, browser.Keys);
        Assert.Equal("Tab", tab.Command); Assert.Equal("Activate", activation.Command);
        Assert.Equal("Observed", activation.Outcome); Assert.Equal(1, activation.BrowserKeystrokes);
        Assert.Contains("View pricing", tab.Announcement, StringComparison.Ordinal);
        Assert.Equal("pricing", tab.CursorNodeId); Assert.Equal("pricing", tab.FocusedNodeId);
        Assert.Equal(ScreenReaderSession.Profile, activation.Profile); activation.Validate(ScreenReaderSession.Profile);
        Assert.Equal("AIReaderTextNotRecordedSpeech", activation.OutputKind);
        Assert.Equal("LastSnapshotFingerprintOnly", activation.SnapshotCoverage);
        Assert.True(reader.InputAttempted); Assert.True(reader.InputCompleted);
        Assert.NotEqual(initial.LastSnapshotSha256, tab.LastSnapshotSha256);
    }

    [Fact]
    public async Task ReaderTaskUncertainInputIsNeverPromotedToCompleted()
    {
        var browser = new Browser { FailKey = true }; var time = new FakeTimeProvider();
        using var reader = new PublicTaskReaderController(browser, Focus, time, time.GetTimestamp());
        var step = await reader.ExecuteAsync("Tab", default);
        Assert.Equal("BrowserError", step.Outcome); Assert.Equal("TEST_INPUT_UNCONFIRMED", step.ErrorCode);
        Assert.True(step.InputMayHaveOccurred); Assert.True(reader.InputAttempted); Assert.False(reader.InputCompleted);
        Assert.Equal(0, step.BrowserKeystrokes); Assert.Single(browser.Keys);
    }

    [Fact]
    public async Task ReaderTaskDeniedInputRemainsUnattemptedAndIsNotRetried()
    {
        var browser = new Browser { DenyKey = true }; var time = new FakeTimeProvider();
        using var reader = new PublicTaskReaderController(browser, Focus, time, time.GetTimestamp());
        var step = await reader.ExecuteAsync("Tab", default);
        Assert.Equal("Denied", step.Outcome); Assert.False(step.InputMayHaveOccurred);
        Assert.False(reader.InputAttempted); Assert.False(reader.InputCompleted); Assert.Empty(browser.Keys);
        await Assert.ThrowsAsync<BrowserInputDeniedException>(() => reader.ExecuteAsync("TypeSecret", default));
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task ReaderAnnouncementIsBoundedWithoutSplittingUnicodeOrLosingItsFullFingerprint()
    {
        var browser = new Browser(); var time = new FakeTimeProvider();
        browser.Snapshot = browser.Snapshot with { Nodes = [new("root", "RootWebArea", new string('x', 4095) + "\U0001f600" + new string('y', 20), true)] };
        using var reader = new PublicTaskReaderController(browser, Focus, time, time.GetTimestamp());
        var step = await reader.ExecuteAsync("Observe", default);
        using var originalReader = new ScreenReaderSession(browser, timeProvider: time);
        var original = await originalReader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.True(step.AnnouncementTruncated); Assert.True(step.Announcement.Length <= 4096);
        Assert.False(char.IsHighSurrogate(step.Announcement[^1]));
        Assert.StartsWith(step.Announcement, original.Announcement, StringComparison.Ordinal);
        Assert.Equal(PublicTaskRules.Hash(original.Announcement), step.AnnouncementSha256);
        Assert.NotEqual(PublicTaskRules.Hash(step.Announcement), step.AnnouncementSha256);
        step.Validate(ScreenReaderSession.Profile);
    }

    [Theory]
    [InlineData("speech")]
    [InlineData("full-snapshot")]
    [InlineData("fingerprint")]
    [InlineData("clock")]
    [InlineData("profile")]
    public async Task ReaderRecordsRejectUnsupportedClaimsAndChangedEvidence(string mutation)
    {
        var time = new FakeTimeProvider(); using var reader = new PublicTaskReaderController(new Browser(), Focus, time, time.GetTimestamp());
        var step = await reader.ExecuteAsync("Observe", default);
        var invalid = mutation switch
        {
            "speech" => step with { OutputKind = "RecordedSpeech" },
            "full-snapshot" => step with { SnapshotCoverage = "Complete" },
            "fingerprint" => step with { AnnouncementSha256 = new string('d', 64) },
            "clock" => step with { FinishedMilliseconds = -1 },
            _ => step with { Profile = "other" }
        };
        Assert.Equal("PUBLIC_TASK_READER_RECORD_INVALID", Assert.Throws<BrowserOperationException>(() => invalid.Validate(ScreenReaderSession.Profile)).Code);
    }

    internal static PublicTaskReport WithReader(PublicTaskReport report)
    {
        PublicTaskReaderRecord Record(string command, int sequence, int keys, string hash, double start, double finish) => new(PublicTaskReaderRecord.CurrentSchema,
            ScreenReaderSession.Profile, sequence, command, "Browse", "Observed", "View pricing, link", PublicTaskRules.Hash("View pricing, link"), false,
            "link-1", "link-1", keys, false, "NotRequested", null, null, "document", report.Start.Job.Source.TargetUrl, hash, 0, start, finish);
        return report with
        {
            ReaderProfile = ScreenReaderSession.Profile,
            Attempts = report.Attempts.Select(a => a with
            {
                InitialReader = Record("ReadCurrent", 1, 0, report.Files.Single(f => f.Path == a.BeforeSnapshot).Sha256, 0, 1),
                Events = a.Events.Select(e => e with { Reader = Record(e.Action == "Tab" ? "Tab" : e.Action == "Enter" ? "Activate" : "ReadCurrent", e.Sequence + 1, a.Method == "Keyboard" && e.InputCompleted ? 1 : 0, new string('e', 64), Math.Max(1, e.ElapsedMilliseconds - 1), e.ElapsedMilliseconds) }).ToImmutableArray()
            }).ToImmutableArray()
        };
    }

    [Fact]
    public void ReaderProfileWithoutRecordedCommandsCannotEstablishReaderTaskReadiness()
    {
        var report = WithReader(PublicTaskTests.Report());
        Assert.Equal("WebsiteEvidenceReady", PublicTaskAssessment.Evaluate(report, true, true).Outcome);
        var first = report.Attempts[0];
        report = report with { Attempts = report.Attempts.SetItem(0, first with { Events = [first.Events[0] with { Reader = null }] }) };
        Assert.Equal("InsufficientEvidence", PublicTaskAssessment.Evaluate(report, true, true).Outcome);
    }

    [Fact]
    public void NativeProofAcceptsOnlyTheExplicitIncompletePointerComparisonWithoutInventingReaderOutput()
    {
        var report = WithReader(PublicTaskTests.Report()); var pointer = report.Attempts[2];
        var incomplete = pointer with { Outcome = "Unconfirmed", ErrorCode = "PUBLIC_TASK_ATTEMPT_UNCONFIRMED", Events = [pointer.Events[0] with { Reader = null, InputCompleted = false }] };
        Assert.True(PublicTaskProof.HasReaderEvidence(pointer, false));
        Assert.True(PublicTaskProof.HasReaderEvidence(incomplete, true));
        Assert.False(PublicTaskProof.HasReaderEvidence(incomplete, false));
        Assert.False(PublicTaskProof.HasReaderEvidence(pointer with { Events = [pointer.Events[0] with { Reader = null }] }, true));
        Assert.False(PublicTaskProof.HasReaderEvidence(incomplete with { Method = "Keyboard" }, true));
        Assert.False(PublicTaskProof.HasReaderEvidence(incomplete with { InitialReader = null }, true));
        Assert.False(PublicTaskProof.HasReaderEvidence(incomplete with { Events = [incomplete.Events[0] with { InputAttempted = false }] }, true));
    }

    [Theory]
    [InlineData("initial-hash")]
    [InlineData("sequence")]
    [InlineData("overlap")]
    [InlineData("key-count")]
    [InlineData("focus")]
    public void ReaderRecordsMustMatchTheirOriginalEventAndInitialSnapshot(string mutation)
    {
        var report = WithReader(PublicTaskTests.Report()); var first = report.Attempts[0]; var step = first.Events[0];
        var record = step.Reader!;
        record = mutation switch
        {
            "sequence" => record with { Sequence = 3 },
            "overlap" => record with { StartedMilliseconds = 0 },
            "key-count" => record with { BrowserKeystrokes = 0 },
            "focus" => record with { FocusedNodeId = "other-control" },
            _ => record
        };
        first = first with { Events = [step with { Reader = record }] };
        if (mutation == "initial-hash") first = first with { InitialReader = first.InitialReader! with { LastSnapshotSha256 = new string('e', 64) } };
        report = report with { Attempts = report.Attempts.SetItem(0, first) };
        Assert.Equal("PUBLIC_TASK_REPORT_INVALID", Assert.Throws<BrowserOperationException>(() => report.Validate(report.Start)).Code);
    }

    [Theory]
    [InlineData("document")]
    [InlineData("epoch")]
    [InlineData("focus")]
    [InlineData("cursor")]
    public async Task ReaderInitialObservationMustMatchTheRetainedSnapshotContents(string mutation)
    {
        using var rig = await PublicTaskHttpTests.Rig.Create(WithReader);
        var report = JsonSerializer.Deserialize<PublicTaskReport>(rig.Files["report.json"], ReaderWorkerWire.Json)!;
        var first = report.Attempts[0]; var initial = first.InitialReader!;
        initial = mutation switch
        {
            "document" => initial with { DocumentId = "another-document" },
            "epoch" => initial with { CaptureEpoch = initial.CaptureEpoch + 1 },
            "focus" => initial with { FocusedNodeId = "not-focused" },
            _ => initial with { CursorNodeId = "absent-node" }
        };
        report = report with { Attempts = report.Attempts.SetItem(0, first with { InitialReader = initial }) };
        var files = rig.Files.SetItem("report.json", JsonSerializer.SerializeToUtf8Bytes(report, ReaderWorkerWire.Json));
        Assert.Equal("PUBLIC_TASK_ARCHIVE_INVALID", Assert.Throws<BrowserOperationException>(() => new PublicTaskBundle(report, files).Encode()).Code);
    }

    [Fact]
    public async Task ReaderPagesBoundEscapedTextWithoutSkippingCommands()
    {
        var escaped = new string('\u0001', 4096);
        using var rig = await PublicTaskHttpTests.Rig.Create(report =>
        {
            var attempt = report.Attempts[0]; var last = attempt.Events[0];
            report = report with
            {
                Attempts = report.Attempts.SetItem(0, attempt with
                {
                    Events = Enumerable.Range(1, 20).Select(n => last with { Sequence = n, ElapsedMilliseconds = n * 1000, Action = n == 20 ? "Enter" : "Tab" }).ToImmutableArray()
                })
            };
            report = WithReader(report); attempt = report.Attempts[0];
            return report with
            {
                Attempts = report.Attempts.SetItem(0, attempt with
                {
                    Events = attempt.Events.Select(e => e with { Reader = e.Reader! with { Announcement = escaped, AnnouncementSha256 = PublicTaskRules.Hash(escaped) } }).ToImmutableArray()
                })
            };
        });
        await using var server = await rig.Server(); using var http = rig.Http(server);
        var sequences = new List<int>(); int? offset = 0; var pages = 0;
        while (offset is not null)
        {
            using var response = await http.GetAsync(rig.Path + $"/reader/1/{offset}/{rig.Status.ReportSha256}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); var bytes = await response.Content.ReadAsByteArrayAsync(); Assert.InRange(bytes.Length, 1, 112 * 1024);
            var page = JsonSerializer.Deserialize<PortalTaskReaderPage>(bytes, PortalJson.Options)!;
            Assert.Equal(offset.Value, page.Offset); Assert.Equal(20, page.Total); Assert.Equal(0, page.MissingRecords);
            Assert.Equal(offset == 0, page.Initial is not null); Assert.NotEmpty(page.Events);
            Assert.All(page.Events, e => { Assert.Equal(escaped, e.Reader!.Announcement); Assert.Equal(e.Sequence + 1, e.Reader.Sequence); });
            sequences.AddRange(page.Events.Select(e => e.Sequence));
            Assert.Equal(sequences.Count < 20 ? sequences.Count : (int?)null, page.NextOffset);
            offset = page.NextOffset; Assert.InRange(++pages, 1, 20);
        }
        Assert.True(pages > 2); Assert.Equal(Enumerable.Range(1, 20), sequences); Assert.Equal(0, rig.Creates);
    }

    [Fact]
    public async Task HistoricalTasksDoNotFabricateReaderTranscripts()
    {
        using var rig = await PublicTaskHttpTests.Rig.Create(); await using var server = await rig.Server(); using var http = rig.Http(server);
        using var response = await http.GetAsync(rig.Path + $"/reader/1/0/{rig.Status.ReportSha256}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = (await response.Content.ReadFromJsonAsync<PortalTaskReaderPage>(PortalJson.Options))!;
        Assert.Equal("NotRetained", page.Coverage); Assert.Null(page.ReaderProfile); Assert.Null(page.Initial);
        Assert.Equal(2, page.MissingRecords); Assert.Null(Assert.Single(page.Events).Reader); Assert.Equal(0, rig.Creates);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 2)]
    public async Task ReaderRequestsRejectInvalidAttemptAndOffsetBoundaries(int attempt, int offset)
    {
        using var rig = await PublicTaskHttpTests.Rig.Create(WithReader); await using var server = await rig.Server(); using var http = rig.Http(server);
        using var response = await http.GetAsync(rig.Path + $"/reader/{attempt}/{offset}/{rig.Status.ReportSha256}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(0, rig.Creates);
        Assert.DoesNotContain("View pricing", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReaderExactEndPageDoesNotRepeatInitialOrCommands()
    {
        using var rig = await PublicTaskHttpTests.Rig.Create(WithReader); await using var server = await rig.Server(); using var http = rig.Http(server);
        using var response = await http.GetAsync(rig.Path + $"/reader/1/1/{rig.Status.ReportSha256}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var page = (await response.Content.ReadFromJsonAsync<PortalTaskReaderPage>(PortalJson.Options))!;
        Assert.Equal(1, page.Offset); Assert.Equal(1, page.Total); Assert.Null(page.Initial); Assert.Empty(page.Events); Assert.Null(page.NextOffset);
        Assert.Equal("Recorded", page.Coverage); Assert.Equal(0, rig.Creates);
    }

    [Fact]
    public async Task ReaderApiIsOriginalBoundAndLegacyEventsKeepTheirExactShape()
    {
        using var rig = await PublicTaskHttpTests.Rig.Create(WithReader); await using var server = await rig.Server(); using var http = rig.Http(server);
        using var response = await http.GetAsync(rig.Path + $"/reader/1/0/{rig.Status.ReportSha256}"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = (await response.Content.ReadFromJsonAsync<PortalTaskReaderPage>(PortalJson.Options))!;
        Assert.Equal("Recorded", page.Coverage); Assert.Equal(0, page.MissingRecords); Assert.Equal("ReadCurrent", page.Initial!.Command);
        Assert.Equal("Activate", Assert.Single(page.Events).Reader!.Command); Assert.Equal(rig.Status.ReportSha256, page.ReportSha256);
        using var legacy = await http.GetAsync(rig.Path + $"/events/1/0/{rig.Status.ReportSha256}");
        using var json = JsonDocument.Parse(await legacy.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("events")[0].TryGetProperty("reader", out _));
        using var changed = await http.GetAsync(rig.Path + $"/reader/1/0/{new string('d', 64)}"); Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        rig.DenyAfter = rig.Reads;
        using var revoked = await http.GetAsync(rig.Path + $"/reader/1/0/{rig.Status.ReportSha256}"); Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        Assert.DoesNotContain("View pricing", await revoked.Content.ReadAsStringAsync(), StringComparison.Ordinal); Assert.Equal(0, rig.Creates);
    }

    [Fact]
    public async Task ActualChromiumReaderCommandsAndFocusMatchTheRecordedSource()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync();
        await browser.DiagnosticPage.EvaluateAsync("""
            () => {
                document.body.innerHTML='<h1>Task</h1><button id="action">Complete task</button>';
                document.querySelector('#action').addEventListener('click', () => document.querySelector('h1').textContent='Task complete');
            }
            """);
        await using var navigation = new PublicNavigationBrowser(browser, PublicTaskTests.Start().Job, Path.GetTempPath());
        var time = TimeProvider.System; using var reader = new PublicTaskReaderController(browser, navigation.FocusFromSnapshotAsync, time, time.GetTimestamp());
        var tab = await reader.ExecuteAsync("Tab", default); var activate = await reader.ExecuteAsync("Enter", default);
        Assert.Contains("Complete task", tab.Announcement, StringComparison.Ordinal); Assert.Equal("Activate", activate.Command);
        Assert.Equal("Observed", activate.Outcome); Assert.True(reader.InputCompleted);
        Assert.Contains(reader.Snapshot!.Nodes, n => n.Role == "heading" && n.Name == "Task complete");
        Assert.Equal(reader.Snapshot.FocusedNode!.Id, activate.FocusedNodeId);
        activate.Validate(ScreenReaderSession.Profile);
    }

    private sealed class Browser : IScreenReaderBrowser
    {
        internal AccessibilitySnapshot Snapshot = new("document", [new("root", "RootWebArea", "Service", true), new("pricing", "link", "View pricing")], []) { DocumentUrl = "https://example.com/" };
        internal readonly List<string> Keys = [];
        internal bool FailKey, DenyKey;
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            if (DenyKey) throw new BrowserInputDeniedException("TEST_KEY_DENIED");
            Keys.Add(key);
            if (FailKey) throw new BrowserOperationException("TEST_INPUT_UNCONFIRMED");
            Snapshot = Snapshot with { Nodes = Snapshot.Nodes.Select(n => n with { IsFocused = n.Id == "pricing" }).ToImmutableArray() };
            return Task.CompletedTask;
        }
    }
}
