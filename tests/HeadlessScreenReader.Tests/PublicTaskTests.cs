using System.Collections.Immutable;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class PublicTaskTests
{
    internal static PublicTaskStart Start() => new(PublicTaskStart.CurrentSchema,
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            PublicReaderSource.FromUrl("https://example.com/"), new string('a', 64)), Guid.NewGuid(), Guid.NewGuid());
    internal static PublicTaskReport Report(PublicTaskStart? start = null)
    {
        start ??= Start(); var target = "https://example.com/pricing";
        var focus = new PublicTaskFocus("link-1", "link", "View pricing", target);
        var files = ImmutableArray.CreateBuilder<PublicTaskFile>();
        var attempts = Enumerable.Range(1, 3).Select(i =>
        {
            foreach (var path in new[] { $"attempt-{i}-before.json", $"attempt-{i}-after.json", $"attempt-{i}.webm" })
                files.Add(new(path, new string('b', 64), 100, path.EndsWith("webm", StringComparison.Ordinal) ? "video/webm" : "application/json"));
            return new PublicTaskAttempt(i, Guid.NewGuid(), i < 3 ? "Keyboard" : "Pointer", DateTimeOffset.UtcNow,
                i < 3 ? "NotReached" : "Reached", null, i < 3 ? start.Job.Source.TargetUrl : target, true, 0,
                [new(1, 1000, i < 3 ? "Enter" : "Pointer", focus, focus, start.Job.Source.TargetUrl, true, true)],
                $"attempt-{i}-before.json", $"attempt-{i}-after.json", $"attempt-{i}.webm", true, true, 25);
        }).ToImmutableArray();
        return new(PublicTaskReport.CurrentSchema, start, new("Pricing", "Reach pricing information", "View pricing", target), attempts, files.ToImmutable(),
            "Chromium test", "Playwright test", "Test OS", "1280x720", new string('c', 64));
    }

    [Fact]
    public void RepeatedCompletedKeyboardFailureWithSuccessfulPointerAndVerifiedOriginalsMeetsAllEightConditions()
    {
        var result = PublicTaskAssessment.Evaluate(Report(), true, true);
        Assert.Equal("WebsiteEvidenceReady", result.Outcome);
        Assert.Equal(8, result.Conditions.Length); Assert.All(result.Conditions, c => Assert.True(c.Satisfied, c.Id));
    }
    [Theory]
    [InlineData("engine")]
    [InlineData("policy")]
    [InlineData("lost-input")]
    [InlineData("no-activation")]
    [InlineData("missing-media")]
    [InlineData("changed-link")]
    [InlineData("no-repeat")]
    [InlineData("pointer-failed")]
    public void UncertaintyAndMissingComparisonsCannotEstablishAReadyBarrier(string scenario)
    {
        var report = Report(); var first = report.Attempts[0];
        first = scenario switch
        {
            "engine" => first with { Outcome = "Unconfirmed", ErrorCode = "PUBLIC_TASK_ATTEMPT_UNCONFIRMED" },
            "policy" => first with { PolicyInterruptions = 1 },
            "lost-input" => first with { Events = [first.Events[0] with { InputCompleted = false }] },
            "no-activation" => first with { Events = [first.Events[0] with { Action = "Tab" }] },
            "missing-media" => first with { Outcome = "Unconfirmed", DecodedVideoFrames = 0 },
            "changed-link" => first with { TaskTargetMatched = false },
            _ => first
        };
        report = report with { Attempts = report.Attempts.SetItem(0, first) };
        if (scenario == "no-repeat") report = report with { Attempts = report.Attempts.SetItem(1, report.Attempts[1] with { Outcome = "Reached", FinalUrl = report.Goal!.DestinationUrl }) };
        if (scenario == "pointer-failed") report = report with { Attempts = report.Attempts.SetItem(2, report.Attempts[2] with { Outcome = "NotReached", FinalUrl = report.Start.Job.Source.TargetUrl }) };
        var result = PublicTaskAssessment.Evaluate(report, true, true);
        Assert.Equal("InsufficientEvidence", result.Outcome); Assert.Contains(result.Conditions, c => !c.Satisfied);
    }
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void MissingOriginalVerificationOrNativeRemovalBlocksReadiness(bool originals, bool removal) =>
        Assert.Equal("InsufficientEvidence", PublicTaskAssessment.Evaluate(Report(), originals, removal).Outcome);

    [Fact]
    public void CurrentArtifactsRemainSatisfiedWhenAnInputOutcomeIsUnconfirmed()
    {
        var report = Report(); report = report with
        {
            Attempts = report.Attempts.SetItem(2,
            report.Attempts[2] with
            {
                Outcome = "Unconfirmed",
                ErrorCode = "PUBLIC_TASK_ATTEMPT_UNCONFIRMED",
                Events = [report.Attempts[2].Events[0] with { InputCompleted = false }]
            })
        };
        var result = PublicTaskAssessment.Evaluate(report, true, true);
        Assert.Equal("InsufficientEvidence", result.Outcome);
        foreach (var id in new[] { "AccessibilityContext", "ReproducibleEnvironment", "OriginalArtifacts" })
            Assert.True(result.Conditions.Single(c => c.Id == id).Satisfied, id);
        Assert.False(result.Conditions.Single(c => c.Id == "RecordedBarrier").Satisfied);
        Assert.False(result.Conditions.Single(c => c.Id == "PointerComparison").Satisfied);
    }
    [Fact]
    public void SuccessfulKeyboardAttemptsCoverOnlyTheTestedTask()
    {
        var report = Report(); report = report with { Attempts = report.Attempts.Select(a => a with { Outcome = "Reached", FinalUrl = report.Goal!.DestinationUrl }).ToImmutableArray() };
        Assert.Equal("NoBlockingBarrierInTestedTask", PublicTaskAssessment.Evaluate(report, true, true).Outcome);
        Assert.False(report.LegalCaseDetermined); Assert.Equal("NotPerformed", report.AssistiveTechnologyComparison);
    }
    [Theory]
    [InlineData("https://other.example/pricing")]
    [InlineData("https://example.com/checkout")]
    [InlineData("https://example.com/pricing?confirm=yes")]
    [InlineData("https://example.com/admin/help")]
    [InlineData("https://example.com/contact#delete")]
    [InlineData("http://example.com/pricing")]
    public void DiscoveredLinksCannotAuthorizeOffHostOrTransactionActions(string target) =>
        Assert.False(PublicTaskRules.SafeDestination(Start().Job.Source, target));
    [Fact]
    public void OriginalReportCannotBeRelabeledForAnotherJobOrGeneration()
    {
        var report = Report(); var wrong = report.Start with { Job = report.Start.Job with { JobId = Guid.NewGuid() } };
        Assert.Equal("PUBLIC_TASK_REPORT_INVALID", Assert.Throws<BrowserOperationException>(() => report.Validate(wrong)).Code);
        Assert.Throws<BrowserOperationException>(() => (report.Start.Job with { Generation = 2 }).Validate());
    }
    [Fact]
    public void TracePreservesActualNodeTransitionsAndUncertainInputWithoutClaimingReachability()
    {
        var first = Report().Attempts[0];
        first = first with { Events = [first.Events[0] with { After = null, InputCompleted = false }] };
        var graph = PublicTaskGraph.Build(first); var edge = Assert.Single(graph.Edges);
        Assert.True(edge.InputAttempted); Assert.False(edge.InputCompleted); Assert.NotEqual(edge.From, edge.To);
        Assert.Contains(graph.Nodes, n => n.Id == edge.To && !n.FocusObserved && n.Focus is null);
    }

    [Fact]
    public void SameUrlWithChangedInlineContentCannotBecomeAnUnchangedFailedTask()
    {
        var before = new AccessibilitySnapshot("document", [new("heading", "heading", "Service information"), new("link", "link", "View pricing")], [])
        { CaptureEpoch = 1, DocumentUrl = "https://example.com/" };
        Assert.True(PublicTaskRules.SameExposedContent(before, before with { Nodes = before.Nodes.SetItem(1, before.Nodes[1] with { IsFocused = true }) }));
        Assert.False(PublicTaskRules.SameExposedContent(before, before with { Nodes = before.Nodes.SetItem(0, before.Nodes[0] with { Name = "Pricing information inline" }) }));
        Assert.False(PublicTaskRules.SameExposedContent(before, before with { CaptureEpoch = 2 }));
        Assert.False(PublicTaskRules.SameExposedContent(before, before with { DocumentId = "another-document" }));
    }

    [Fact]
    public void TransientChromiumTextBoxIdsDoNotHideARealUnchangedKeyboardFailure()
    {
        var before = new AccessibilitySnapshot("document", [new("link", "link", "View pricing"),
            new("document:-1000000004", "InlineTextBox", "View pricing", ParentId: "link")], [])
        { CaptureEpoch = 1, DocumentUrl = "https://example.com/" };
        var after = before with { Nodes = before.Nodes.SetItem(1, before.Nodes[1] with { Id = "document:-1000000005" }) };
        Assert.True(PublicTaskRules.SameExposedContent(before, after));
        Assert.False(PublicTaskRules.SameExposedContent(before, after with { Nodes = after.Nodes.SetItem(1, after.Nodes[1] with { Name = "Pricing details now available" }) }));
        Assert.Equal("document:-1000000004", before.Nodes[1].Id); Assert.Equal("document:-1000000005", after.Nodes[1].Id);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CurrentAuthorizationIsRequiredBeforeAndAfterDurableIntentBeforeAnyAck(int denial)
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "public-task-tests", Guid.NewGuid().ToString("N")));
        var start = Start(); var calls = 0; using var ack = new MemoryStream();
        var progress = new PublicTaskProgress(start, root, (_, _) => Task.FromResult(++calls != denial));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new PublicTaskIntent(start, 1, 1, "Open", null), ReaderWorkerWire.Json);
        var failure = await Assert.ThrowsAsync<BrowserOperationException>(() => progress.HandleAsync(bytes, ack, default));
        Assert.Equal("PUBLIC_TASK_AUTHORITY_LOST", failure.Code); Assert.Equal(0, ack.Length); Assert.Equal(denial, calls);
        Assert.Equal(denial == 2 ? 1 : 0, Directory.Exists(root) ? Directory.GetFiles(root).Length : 0);
    }
    [Fact]
    public async Task AckPinsExactIntentAndDuplicateSequenceCannotAuthorizeInput()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "public-task-tests", Guid.NewGuid().ToString("N")));
        var start = Start(); using var ack = new MemoryStream(); var calls = 0;
        var progress = new PublicTaskProgress(start, root, (_, _) => { calls++; return Task.FromResult(true); });
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new PublicTaskIntent(start, 1, 1, "Open", null), ReaderWorkerWire.Json);
        await progress.HandleAsync(bytes, ack, default); ack.Position = 0;
        var receipt = JsonSerializer.Deserialize<PublicTaskAck>((await ReaderWorkerWire.ReadAsync(ack, 4096, default))!, ReaderWorkerWire.Json)!;
        Assert.Equal(start.ExecutionId, receipt.ExecutionId); Assert.Equal(1, receipt.Sequence); Assert.Equal(PublicTaskRules.Hash(bytes), receipt.IntentSha256);
        await Assert.ThrowsAsync<BrowserOperationException>(() => progress.HandleAsync(bytes, new MemoryStream(), default)); Assert.Equal(2, calls);
    }
    [Fact]
    public async Task PrivateOriginalReadRejectsChangedBytesAndInvalidOffsets()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "public-task-tests", Guid.NewGuid().ToString("N")));
        var job = Start().Job; using var store = new PublicTaskEvidenceStore(root, null);
        var bytes = Enumerable.Range(0, 70000).Select(i => (byte)(i % 251)).ToArray(); var digest = PublicTaskRules.Hash(bytes);
        await store.WriteAsync(job, digest, bytes, default); var file = new PublicTaskFile("attempt-1.webm", digest, bytes.Length, "video/webm");
        Assert.Equal(bytes[..65536], await store.PageAsync(job, file, 0, default));
        Assert.Equal(bytes[65536..], await store.PageAsync(job, file, 65536, default));
        Assert.Empty(await store.PageAsync(job, file, bytes.Length, default));
        await Assert.ThrowsAsync<BrowserOperationException>(() => store.PageAsync(job, file, 1, default));
        var path = Path.Combine(root, "public-task-originals", job.OrganizationId.ToString("N"), job.JobId.ToString("N"), digest);
        bytes[0] ^= 255; await File.WriteAllBytesAsync(path, bytes);
        await Assert.ThrowsAsync<BrowserOperationException>(() => store.PageAsync(job, file, 0, default));
    }
}
