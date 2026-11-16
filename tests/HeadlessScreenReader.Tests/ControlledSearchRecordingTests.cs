using System.Collections.Immutable;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class ControlledSearchRecordingTests
{
    [Fact]
    public void SearchRecordingIndexExplainsTheResultWithoutChangingMachineEvidence()
    {
        const string observation = "KeyboardGoalNotObservedAcrossFieldAndButtonAlternativesWithPointerGoalObserved";
        var trials = Enumerable.Range(1, 4).Select(trial => new SyntheticSearchTaskTrial(trial,
            trial <= 2 ? "FieldEnter" : trial == 3 ? "ButtonEnter" : "ButtonSpace", Guid.NewGuid().ToString("N"), "Completed", "Unknown", 15, 15)).ToImmutableArray();
        var html = ControlledSearchRecordings.Index(SyntheticSearchSites.Describe(BundledFixture.MockSearchKeyboardBlockedV1), trials, observation);
        Assert.Contains("The tested keyboard paths did not reach the search result. The mouse comparison reached it.", html);
        Assert.Contains("Enter from search field — second attempt", html);
        Assert.Contains("Enter on Search button", html);
        Assert.Contains("Space on Search button", html);
        Assert.DoesNotContain(observation, html);
        Assert.DoesNotContain("FieldEnter", html);
        Assert.Contains("Required website evidence has not been assessed.", html);
        Assert.Equal("FieldEnter", trials[0].Submission);
        Assert.Equal("Unknown", trials[0].Reachability);
    }

    [Theory]
    [InlineData(BundledFixture.MockSearchWorkingV1, TraversalReachability.Reached)]
    [InlineData(BundledFixture.MockSearchKeyboardBlockedV1, TraversalReachability.Unknown)]
    [Trait("Category", "Browser")]
    public async Task ActualControlledFieldSubmissionBindsItsOriginalsAndEveryGrant(BundledFixture fixture, TraversalReachability reachability)
    {
        var source = SyntheticSearchSites.Describe(fixture);
        var start = new SyntheticSearchTaskStart(SyntheticSearchTaskControl.Schema,
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, SyntheticSearchTaskJob.CurrentProfile, SyntheticSearchTaskSource.FromId(source.Id)),
            Guid.NewGuid(), Guid.NewGuid());
        var root = Path.GetFullPath(Path.Combine("artifacts", "controlled-search-recordings", Guid.NewGuid().ToString("N")));
        var grants = await SyntheticSearchTaskGrants.BeginAsync(Path.Combine(root, "control"), start);
        await grants.PrepareAsync(new(SyntheticSearchTaskControl.Schema, start, 1, SyntheticSearchTaskControl.Boundaries[0], null), _ => Task.FromResult(true));
        var sourceSequence = 0;
        var modes = 0;
        async Task Retain(string document, long epoch, ImmutableArray<CompletedCaptureSource> originals, CancellationToken token)
        {
            var frames = ImmutableArray.CreateBuilder<FixtureSourceFrame>(); var size = 0;
            async Task Flush()
            {
                if (frames.Count == 0) return;
                await grants.RetainSourcesAsync(new(SyntheticSearchSourceProtocol.Schema, start.ExecutionId, 1, ++sourceSequence, document, epoch, frames.ToImmutable()),
                    _ => Task.FromResult(true), token);
                frames.Clear(); size = 0;
            }
            foreach (var original in originals)
            {
                if (frames.Count == 64 || original.Content.Length > 512 * 1024 - size) await Flush();
                frames.Add(new(original.Frame.Sequence, original.Frame.File, original.Frame.PresentationUnixMilliseconds, original.Frame.Sha256, original.Content));
                size += original.Content.Length;
            }
            await Flush();
        }
        var result = await TraversalRecordingDemo.RunControlledSearchRouteAsync(Path.Combine(root, "route-1-media"), fixture, 1,
            async (input, token) =>
            {
                var sequence = grants.PreparedResponses + 1;
                Assert.True(grants.HasSources(1, input.DocumentId, input.CaptureEpoch));
                return (await grants.PrepareAsync(new(SyntheticSearchTaskControl.Schema, start, sequence,
                    SyntheticSearchTaskControl.Boundaries[sequence - 1], input), _ => Task.FromResult(true), token)).Decision;
            }, _ => { modes++; return Task.CompletedTask; }, Retain, CancellationToken.None);
        Assert.Equal("Complete", result.Recording.Stage);
        Assert.Null(result.Recording.ErrorCode);
        Assert.Equal(reachability, result.Receipt!.Reachability);
        Assert.Equal(15, result.Receipt.CompletedKeystrokes);
        Assert.Equal(16, grants.PreparedResponses);
        Assert.Equal(0, modes);
        Assert.True(result.Receipt.HasRecording);
        Assert.True(result.Recording.Shutdown!.ProcessExitConfirmed);
        Assert.Contains(result.Recording.Media!.Artifacts, artifact => artifact.File == "original.webm" && artifact.DecodedFrames > 0);
        var retained = grants.ReadRetainedSources();
        Assert.False(retained.HasUncommittedFiles);
        Assert.Equal(result.Recording.Media.PreciseFrames.Length, retained.Files.Length);
        Assert.Equal(result.Recording.Media.PreciseFrames.Select(frame => frame.Sha256).Order(StringComparer.Ordinal),
            retained.Files.Select(file => file.Image.Sha256).Order(StringComparer.Ordinal));
        Assert.All(retained.Files, file => Assert.Equal(result.Receipt.Observations[0].Snapshot.DocumentId, file.DocumentId));
        Assert.False(retained.IsCompletedRecording);
        Assert.Equal("SEARCH_TASK_GRANTS_INCOMPLETE", Assert.Throws<BrowserOperationException>(grants.RequireComplete).Code);
        await TraversalArtifactWriter.WriteAsync(root, 1, result.Receipt, result.Environment, result.Recording);
    }
}
