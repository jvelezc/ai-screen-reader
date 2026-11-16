using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Tests;

public sealed class SyntheticSearchTaskTests
{
    [Theory]
    [InlineData(BundledFixture.MockSearchWorkingV1)]
    [InlineData(BundledFixture.MockSearchKeyboardBlockedV1)]
    public void SearchTaskSourcesMatchTheIndependentlyCompiledFixtures(BundledFixture fixture)
    {
        var actual = SyntheticSearchSites.Describe(fixture);
        var source = SyntheticSearchTaskSource.FromId(actual.Id);
        Assert.Equal(actual.SourceSha256, source.Sha256);
        Assert.Equal(BundledFixtureCatalog.Describe(fixture).ContentSha256, source.Sha256);
    }

    [Fact]
    public void SearchTaskBoundariesPinEveryPhysicalAction()
    {
        var boundaries = SyntheticSearchTaskControl.Boundaries;
        Assert.Equal(85, boundaries.Length);
        Assert.Equal(77, boundaries.Count(b => b.Action == "Native"));
        Assert.Equal(65, boundaries.Count(b => b.Kind == SyntheticSearchInputKind.Character));
        Assert.Equal(10, boundaries.Count(b => b.Kind == SyntheticSearchInputKind.Key));
        Assert.Single(boundaries, b => b.Kind == SyntheticSearchInputKind.MouseFocus);
        Assert.Single(boundaries, b => b.Kind == SyntheticSearchInputKind.MouseActivation);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, boundaries.Where(b => b.Action == "Open").Select(b => b.Trial));
        Assert.Equal(new[] { 4, 5 }, boundaries.Where(b => b.Action == "FocusMode").Select(b => b.Trial));
        Assert.Equal(new SyntheticSearchTaskBoundary(0, "Publish"), boundaries[^1]);
        Assert.Equal(new[] { 16, 16, 17, 18, 17 }, Enumerable.Range(1, 5).Select(trial => boundaries.Count(b => b.Trial == trial)));
        foreach (var trial in Enumerable.Range(1, 5))
        {
            var inputs = boundaries.Where(b => b.Trial == trial && b.Action == "Native").ToArray();
            Assert.Equal(Enumerable.Range(1, inputs.Length), inputs.Select(b => b.NativeSequence));
            Assert.Equal(Enumerable.Range(1, 13), inputs.Where(b => b.Kind == SyntheticSearchInputKind.Character).Select(b => b.CharacterIndex));
            Assert.Equal("sample course".Select(c => c == ' ' ? "Space" : c.ToString()),
                inputs.Where(b => b.Kind == SyntheticSearchInputKind.Character).Select(b => b.NativeKey));
        }
        Assert.Equal(13, MockTaskControl.Boundaries.Length);
    }

    [Fact]
    public void SearchTaskPlansCoverFieldAndButtonActivationWithoutChangingV1()
    {
        foreach (var trial in new[] { 1, 2 })
            Assert.Equal(new TraversalInstruction[] { new(ReaderCommand.Tab), new(ReaderCommand.EnterSearchText, "sample course"), new(ReaderCommand.PressKey, "Enter") },
                SyntheticSearchTaskControl.KeyboardPlan(trial).Instructions);
        Assert.Equal(new TraversalInstruction[] { new(ReaderCommand.Tab), new(ReaderCommand.EnterSearchText, "sample course"), new(ReaderCommand.Tab), new(ReaderCommand.Activate) },
            SyntheticSearchTaskControl.KeyboardPlan(3).Instructions);
        Assert.Equal(new TraversalInstruction[] { new(ReaderCommand.Tab), new(ReaderCommand.EnterSearchText, "sample course"), new(ReaderCommand.Tab), new(ReaderCommand.EnterFocusMode), new(ReaderCommand.PressKey, "Space") },
            SyntheticSearchTaskControl.KeyboardPlan(4).Instructions);
        Assert.Throws<BrowserOperationException>(() => SyntheticSearchTaskControl.KeyboardPlan(5));
        Assert.Equal(new TraversalInstruction(ReaderCommand.Tab), SyntheticSearchPolicy.KeyboardPlan("Enter").Instructions[2]);
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("scan")]
    [InlineData("job")]
    [InlineData("generation-zero")]
    [InlineData("generation-replay")]
    [InlineData("profile")]
    [InlineData("source-null")]
    [InlineData("source-id")]
    [InlineData("source-hash")]
    [InlineData("schema")]
    [InlineData("worker")]
    [InlineData("execution")]
    [InlineData("job-null")]
    public void SearchTaskRejectsForeignIdentityOrSource(string change)
    {
        var start = Start();
        start.Validate();
        var job = start.Job;
        var changed = change switch
        {
            "organization" => start with { Job = job with { OrganizationId = Guid.Empty } },
            "scan" => start with { Job = job with { ScanId = Guid.Empty } },
            "job" => start with { Job = job with { JobId = Guid.Empty } },
            "generation-zero" => start with { Job = job with { Generation = 0 } },
            "generation-replay" => start with { Job = job with { Generation = 2 } },
            "profile" => start with { Job = job with { Profile = MockTaskJob.CurrentProfile } },
            "source-null" => start with { Job = job with { Source = null! } },
            "source-id" => start with { Job = job with { Source = job.Source with { Id = "checkout-working" } } },
            "source-hash" => start with { Job = job with { Source = job.Source with { Sha256 = new('f', 64) } } },
            "schema" => start with { Schema = MockTaskControl.Schema },
            "worker" => start with { WorkerId = Guid.Empty },
            "execution" => start with { ExecutionId = Guid.Empty },
            _ => start with { Job = null! }
        };
        var code = change is "source-id" or "source-hash" ? "SEARCH_TASK_SOURCE_INVALID"
            : change is "schema" or "worker" or "execution" or "job-null" ? "SEARCH_TASK_START_INVALID" : "SEARCH_TASK_JOB_INVALID";
        Assert.Equal(code, Assert.Throws<BrowserOperationException>(changed.Validate).Code);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("foreign-start")]
    [InlineData("sequence-zero")]
    [InlineData("sequence-next")]
    [InlineData("boundary")]
    [InlineData("input-missing")]
    [InlineData("native-sequence")]
    [InlineData("native-kind")]
    [InlineData("source")]
    [InlineData("epoch")]
    [InlineData("extra-input")]
    public void SearchTaskControlRejectsMismatchedScope(string change)
    {
        var start = Start();
        var input = new SyntheticSearchInputIntent(2, SyntheticSearchInputKind.Character, "s", 1, start.Job.Source.Sha256, "document", 1, "search");
        var intent = new SyntheticSearchTaskIntent(SyntheticSearchTaskControl.Schema, start, 3, SyntheticSearchTaskControl.Boundaries[2], input);
        SyntheticSearchTaskControl.Validate(intent, start, 3);
        var changed = change switch
        {
            "schema" => intent with { Schema = MockTaskControl.Schema },
            "foreign-start" => intent with { Start = start with { ExecutionId = Guid.Parse("99999999-9999-9999-9999-999999999999") } },
            "sequence-zero" => intent with { Sequence = 0 },
            "sequence-next" => intent with { Sequence = 4 },
            "boundary" => intent with { Boundary = intent.Boundary with { Trial = 2 } },
            "input-missing" => intent with { Input = null },
            "native-sequence" => intent with { Input = input with { NativeSequence = 3 } },
            "native-kind" => intent with { Input = input with { Kind = SyntheticSearchInputKind.Key, NativeKey = "Enter", CharacterIndex = 0 } },
            "source" => intent with { Input = input with { SourceSha256 = new('f', 64) } },
            "epoch" => intent with { Input = input with { CaptureEpoch = 2 } },
            _ => intent with { Sequence = 1, Boundary = SyntheticSearchTaskControl.Boundaries[0] }
        };
        Assert.Equal("SEARCH_TASK_INTENT_INVALID", Assert.Throws<BrowserOperationException>(() => SyntheticSearchTaskControl.Validate(changed, start, change == "extra-input" ? 1 : 3)).Code);
    }

    [Theory]
    [InlineData(BundledFixture.MockSearchWorkingV1, TraversalReachability.Reached)]
    [InlineData(BundledFixture.MockSearchKeyboardBlockedV1, TraversalReachability.Unknown)]
    [Trait("Category", "Browser")]
    public async Task ActualFieldEnterSearchUsesFifteenIndividuallyGrantedKeys(BundledFixture fixture, TraversalReachability reachability)
    {
        var inputs = new List<SyntheticSearchInputIntent>();
        var folder = Path.GetFullPath(Path.Combine("artifacts", "search-task-field", Guid.NewGuid().ToString("N")));
        await using var probe = await ChromiumFixtureBrowser.LaunchGuardedSyntheticSearchRecordingAsync(folder, fixture,
            (input, _) => { inputs.Add(input); return Task.FromResult(SyntheticSearchInputDecision.Granted); });
        var result = await new TraversalRunner().RunAsync(probe.Browser, SyntheticSearchTaskControl.KeyboardPlan(1),
            waitOptions: new() { Timeout = TimeSpan.FromMilliseconds(400), MaxPolls = 10 });
        Assert.Equal(reachability, result.Reachability);
        Assert.Equal(15, result.AttemptedKeystrokes);
        Assert.Equal(15, result.CompletedKeystrokes);
        Assert.Equal(new[] { "Tab", "Enter" }, inputs.Where(i => i.Kind == SyntheticSearchInputKind.Key).Select(i => i.NativeKey));
        Assert.Equal(13, inputs.Count(i => i.Kind == SyntheticSearchInputKind.Character));
        Assert.Equal("searchbox", result.Observations[^2].Snapshot.FocusedNode!.Role);
        Assert.All(inputs, input => Assert.Equal(SyntheticSearchSites.Describe(fixture).SourceSha256, input.SourceSha256));
        var media = await probe.FinishAndInspectAsync();
        Assert.Contains(media.Artifacts, a => a.File == "original.webm" && a.DecodedFrames > 0);
        Assert.True(probe.ShutdownStatus.ProcessExitConfirmed);
    }

    private static SyntheticSearchTaskStart Start() => new(SyntheticSearchTaskControl.Schema,
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"), 1, SyntheticSearchTaskJob.CurrentProfile,
            SyntheticSearchTaskSource.FromId("search-working")), Guid.Parse("44444444-4444-4444-4444-444444444444"),
        Guid.Parse("55555555-5555-5555-5555-555555555555"));
}
