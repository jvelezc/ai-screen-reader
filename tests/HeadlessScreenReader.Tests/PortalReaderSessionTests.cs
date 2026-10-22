using System.Collections.Immutable;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class PortalReaderSessionTests
{
    internal static readonly Guid Organization = PortalProjectClientTests.Organization;
    internal static readonly PortalIdentity Actor = new(Guid.NewGuid(), "first@example.invalid", "private-bearer-canary");
    private static readonly PortalIdentity Other = new(Guid.NewGuid(), "second@example.invalid", "other-bearer-canary");

    [Theory]
    [InlineData("subject", "read")]
    [InlineData("subject", "observe")]
    [InlineData("subject", "close")]
    [InlineData("tenant", "read")]
    [InlineData("tenant", "observe")]
    [InlineData("tenant", "close")]
    [InlineData("revoked", "read")]
    [InlineData("revoked", "observe")]
    [InlineData("revoked", "close")]
    public async Task EveryOperationRechecksSubjectTenantAndCurrentMembership(string denial, string operation)
    {
        var project = new ReaderProjectStub(); var browser = new ReaderBrowserStub();
        await using var sessions = new PortalReaderSessions(project, new FakeTimeProvider(), () => Task.FromResult<IPortalReaderBrowser>(browser));
        var opened = await sessions.OpenAsync(Actor, Organization, default);
        project.Hidden = denial == "revoked";
        var actor = denial == "subject" ? Other : Actor;
        var organization = denial == "tenant" ? Guid.NewGuid() : Organization;
        var fault = await Assert.ThrowsAsync<PortalFault>(async () =>
        {
            switch (operation)
            {
                case "read": await sessions.ReadAsync(actor, organization, opened.SessionId, default); break;
                case "observe": await sessions.ObserveAsync(actor, organization, opened.SessionId, default); break;
                case "close": await sessions.CloseAsync(actor, organization, opened.SessionId, default); break;
            }
        });
        Assert.Equal(404, fault.Status);
        Assert.Equal(denial == "revoked" ? "CONTROL_WORKSPACE_NOT_FOUND" : "CONTROL_READER_NOT_FOUND", fault.Code);
        Assert.Equal(2, project.WorkspaceCalls);
        Assert.Equal(0, browser.Captures); Assert.Equal(0, browser.Closes);
        project.Hidden = false;
        Assert.Equal(opened, await sessions.ReadAsync(Actor, Organization, opened.SessionId, default));
    }

    [Fact]
    public async Task CapacityAndPerSubjectLimitPreserveExistingSessionsAndRecoverAfterClose()
    {
        var browsers = new List<ReaderBrowserStub>();
        await using var sessions = NewSessions(() => { var browser = new ReaderBrowserStub(); browsers.Add(browser); return Task.FromResult<IPortalReaderBrowser>(browser); });
        var first = await sessions.OpenAsync(Actor, Organization, default);
        var duplicate = await Assert.ThrowsAsync<PortalFault>(() => sessions.OpenAsync(Actor, Guid.NewGuid(), default));
        Assert.Equal("CONTROL_READER_ALREADY_OPEN", duplicate.Code);
        var second = await sessions.OpenAsync(Other, Organization, default);
        var thirdActor = new PortalIdentity(Guid.NewGuid(), "third@example.invalid", "private-third");
        var full = await Assert.ThrowsAsync<PortalFault>(() => sessions.OpenAsync(thirdActor, Organization, default));
        Assert.Equal("CONTROL_READER_CAPACITY_EXHAUSTED", full.Code);
        Assert.Equal(2, browsers.Count);
        Assert.Equal(first, await sessions.ReadAsync(Actor, Organization, first.SessionId, default));
        Assert.Equal("Closed", (await sessions.CloseAsync(Actor, Organization, first.SessionId, default)).State);
        var replacement = await sessions.OpenAsync(thirdActor, Organization, default);
        Assert.NotEqual(first.SessionId, replacement.SessionId);
        Assert.Equal(1, browsers[0].Closes);
        Assert.Equal(second, await sessions.ReadAsync(Other, Organization, second.SessionId, default));
        var stale = await Assert.ThrowsAsync<PortalFault>(() => sessions.CloseAsync(Actor, Organization, first.SessionId, default));
        Assert.Equal("CONTROL_READER_NOT_FOUND", stale.Code);
    }

    [Fact]
    public async Task ExpiryIsMonotonicAndIdleSweepActuallyClosesBrowsers()
    {
        var clock = new FakeTimeProvider(); var browser = new ReaderBrowserStub();
        await using var sessions = new PortalReaderSessions(new ReaderProjectStub(), clock, () => Task.FromResult<IPortalReaderBrowser>(browser));
        var start = clock.GetUtcNow();
        var opened = await sessions.OpenAsync(Actor, Organization, default);
        Assert.Equal(start + TimeSpan.FromMinutes(2), opened.ExpiresAt);
        clock.Advance(TimeSpan.FromMinutes(2) - TimeSpan.FromTicks(1));
        Assert.Equal(opened, await sessions.ReadAsync(Actor, Organization, opened.SessionId, default));
        clock.Advance(TimeSpan.FromTicks(1));
        await sessions.SweepAsync();
        Assert.Equal(1, browser.Closes);
        var expired = await Assert.ThrowsAsync<PortalFault>(() => sessions.ReadAsync(Actor, Organization, opened.SessionId, default));
        Assert.Equal("CONTROL_READER_NOT_FOUND", expired.Code);
    }

    [Fact]
    public async Task ObservationBudgetStopsFurtherCaptureWithoutInventingGoalOrInput()
    {
        var browser = new ReaderBrowserStub();
        await using var sessions = NewSessions(() => Task.FromResult<IPortalReaderBrowser>(browser));
        var opened = await sessions.OpenAsync(Actor, Organization, default);
        for (var count = 0; count < 100; count++)
        {
            var observation = await sessions.ObserveAsync(Actor, Organization, opened.SessionId, default);
            Assert.Equal("document", observation.Snapshot.DocumentId);
            Assert.False(observation.GoalVerified); Assert.Equal(0, observation.DispatchedInputs);
        }
        var fault = await Assert.ThrowsAsync<PortalFault>(() => sessions.ObserveAsync(Actor, Organization, opened.SessionId, default));
        Assert.Equal("CONTROL_READER_BUDGET_EXHAUSTED", fault.Code);
        Assert.Equal(100, browser.Captures);
        Assert.Equal(opened, await sessions.ReadAsync(Actor, Organization, opened.SessionId, default));
    }

    [Fact]
    public async Task TimerReapsAnIdleSessionWithoutAnotherClientRequest()
    {
        var clock = new FakeTimeProvider(); var browser = new ReaderBrowserStub();
        await using var sessions = new PortalReaderSessions(new ReaderProjectStub(), clock, () => Task.FromResult<IPortalReaderBrowser>(browser));
        await sessions.OpenAsync(Actor, Organization, default);
        clock.Advance(TimeSpan.FromMinutes(2));
        await browser.CloseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, browser.Closes); Assert.Equal(0, browser.Captures);
    }

    [Fact]
    public async Task ObservationCompletingAtExpiryIsDiscardedAndBrowserIsClosed()
    {
        var clock = new FakeTimeProvider(); var browser = new ReaderBrowserStub { CaptureRelease = new() };
        await using var sessions = new PortalReaderSessions(new ReaderProjectStub(), clock, () => Task.FromResult<IPortalReaderBrowser>(browser));
        var opened = await sessions.OpenAsync(Actor, Organization, default);
        var observe = sessions.ObserveAsync(Actor, Organization, opened.SessionId, default);
        await browser.CaptureEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromMinutes(2));
        browser.CaptureRelease.SetResult();
        Assert.Equal("CONTROL_READER_NOT_FOUND", (await Assert.ThrowsAsync<PortalFault>(() => observe)).Code);
        Assert.Equal(1, browser.Captures); Assert.Equal(1, browser.Closes);
    }

    [Fact]
    public async Task CanceledQueuedOperationNeverCapturesOrReleasesAnotherOperationsController()
    {
        var browser = new ReaderBrowserStub { CaptureRelease = new() };
        await using var sessions = NewSessions(() => Task.FromResult<IPortalReaderBrowser>(browser));
        var opened = await sessions.OpenAsync(Actor, Organization, default);
        var first = sessions.ObserveAsync(Actor, Organization, opened.SessionId, default);
        await browser.CaptureEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancel = new CancellationTokenSource();
        var queued = sessions.ObserveAsync(Actor, Organization, opened.SessionId, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.False(first.IsCompleted); Assert.Equal(1, browser.Captures); Assert.Equal(0, browser.Closes);
        browser.CaptureRelease.SetResult();
        Assert.Equal(opened.SessionId, (await first).SessionId);
        Assert.Equal(opened, await sessions.ReadAsync(Actor, Organization, opened.SessionId, default));
    }

    [Fact]
    public async Task QuarantinedExpiredBrowserDoesNotSilentlyReleaseCapacity()
    {
        var clock = new FakeTimeProvider(); var browser = new ReaderBrowserStub { FailClose = true };
        await using var sessions = new PortalReaderSessions(new ReaderProjectStub(), clock, () => Task.FromResult<IPortalReaderBrowser>(browser));
        var opened = await sessions.OpenAsync(Actor, Organization, default);
        clock.Advance(TimeSpan.FromMinutes(2));
        await sessions.SweepAsync();
        Assert.Equal(1, browser.Closes);
        Assert.Equal("CONTROL_READER_ALREADY_OPEN", (await Assert.ThrowsAsync<PortalFault>(() => sessions.OpenAsync(Actor, Organization, default))).Code);
        browser.FailClose = false;
        Assert.Equal("Closed", (await sessions.CloseAsync(Actor, Organization, opened.SessionId, default)).State);
    }

    [Fact]
    public async Task CloseRetainsControllerAndCapacityUntilActualShutdownSettles()
    {
        var browser = new ReaderBrowserStub { CloseRelease = new() };
        var opens = 0;
        await using var sessions = NewSessions(() => { opens++; return Task.FromResult<IPortalReaderBrowser>(opens == 1 ? browser : new ReaderBrowserStub()); });
        var opened = await sessions.OpenAsync(Actor, Organization, default);
        var close = sessions.CloseAsync(Actor, Organization, opened.SessionId, default);
        await browser.CloseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var reopen = sessions.OpenAsync(Actor, Organization, default);
        Assert.False(close.IsCompleted); Assert.False(reopen.IsCompleted); Assert.Equal(1, opens);
        browser.CloseRelease.SetResult();
        Assert.Equal("Closed", (await close).State);
        Assert.NotEqual(opened.SessionId, (await reopen).SessionId);
        Assert.Equal(2, opens); Assert.Equal(1, browser.Closes);
    }

    [Fact]
    public async Task CanceledCaptureJoinsActualWorkAndCleanupBeforeReplacement()
    {
        var browser = new ReaderBrowserStub { CaptureRelease = new() };
        var opens = 0;
        await using var sessions = NewSessions(() => { opens++; return Task.FromResult<IPortalReaderBrowser>(opens == 1 ? browser : new ReaderBrowserStub()); });
        var opened = await sessions.OpenAsync(Actor, Organization, default);
        using var cancel = new CancellationTokenSource();
        var observe = sessions.ObserveAsync(Actor, Organization, opened.SessionId, cancel.Token);
        await browser.CaptureEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        var reopen = sessions.OpenAsync(Actor, Organization, default);
        Assert.False(observe.IsCompleted); Assert.False(reopen.IsCompleted); Assert.Equal(0, browser.Closes);
        browser.CaptureRelease.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => observe);
        Assert.NotEqual(opened.SessionId, (await reopen).SessionId);
        Assert.Equal(1, browser.Captures); Assert.Equal(1, browser.Closes);
    }

    [Fact]
    public async Task CanceledLaunchDisposesTheActualResourceBeforeReturningCancellation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var browser = new ReaderBrowserStub(); var opens = 0;
        await using var sessions = NewSessions(async () => { opens++; entered.TrySetResult(); await release.Task; return browser; });
        using var cancel = new CancellationTokenSource();
        var opening = sessions.OpenAsync(Actor, Organization, cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        Assert.False(opening.IsCompleted); Assert.Equal(0, browser.Closes);
        release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening);
        Assert.Equal(1, browser.Closes);
        Assert.Equal("Open", (await sessions.OpenAsync(Actor, Organization, default)).State);
        Assert.Equal(2, opens);
    }

    [Theory]
    [InlineData("capture")]
    [InlineData("node-limit")]
    [InlineData("byte-limit")]
    public async Task UnusableObservationClosesSessionAndNeverReturnsPartialSuccess(string failure)
    {
        var browser = new ReaderBrowserStub
        {
            FailCapture = failure == "capture",
            Snapshot = failure == "node-limit" ? new("large", Enumerable.Range(0, 2049).Select(i => new AccessibleNode(i.ToString(), "button", "x")).ToImmutableArray(), [])
                : failure == "byte-limit" ? new("large", [new("node", "button", new string('x', 262144))], []) : ReaderBrowserStub.DefaultSnapshot
        };
        await using var sessions = NewSessions(() => Task.FromResult<IPortalReaderBrowser>(browser));
        var opened = await sessions.OpenAsync(Actor, Organization, default);
        var fault = await Assert.ThrowsAsync<PortalFault>(() => sessions.ObserveAsync(Actor, Organization, opened.SessionId, default));
        Assert.Equal(failure == "capture" ? "CONTROL_READER_CAPTURE_UNCONFIRMED" : "CONTROL_READER_OBSERVATION_TOO_LARGE", fault.Code);
        Assert.Equal(1, browser.Captures); Assert.Equal(1, browser.Closes);
        Assert.Equal("CONTROL_READER_NOT_FOUND", (await Assert.ThrowsAsync<PortalFault>(() => sessions.ReadAsync(Actor, Organization, opened.SessionId, default))).Code);
    }

    [Fact]
    public async Task FailedCleanupQuarantinesCapacityAndExplicitRetryCanRecover()
    {
        var browser = new ReaderBrowserStub { FailClose = true }; var opens = 0;
        await using var sessions = NewSessions(() => { opens++; return Task.FromResult<IPortalReaderBrowser>(browser); });
        var opened = await sessions.OpenAsync(Actor, Organization, default);
        Assert.Equal("CONTROL_READER_CLOSE_UNCONFIRMED", (await Assert.ThrowsAsync<PortalFault>(() => sessions.CloseAsync(Actor, Organization, opened.SessionId, default))).Code);
        Assert.Equal("CONTROL_READER_CLOSE_UNCONFIRMED", (await Assert.ThrowsAsync<PortalFault>(() => sessions.ReadAsync(Actor, Organization, opened.SessionId, default))).Code);
        Assert.Equal("CONTROL_READER_ALREADY_OPEN", (await Assert.ThrowsAsync<PortalFault>(() => sessions.OpenAsync(Actor, Organization, default))).Code);
        Assert.Equal(1, opens);
        browser.FailClose = false;
        Assert.Equal("Closed", (await sessions.CloseAsync(Actor, Organization, opened.SessionId, default)).State);
        Assert.Equal("Open", (await sessions.OpenAsync(Actor, Organization, default)).State);
        Assert.Equal(2, opens);
    }

    [Fact]
    public async Task FailedLaunchDoesNotConsumeCapacityAndClosedHostCannotReopen()
    {
        var opens = 0; var browser = new ReaderBrowserStub();
        var sessions = NewSessions(() => ++opens == 1 ? Task.FromException<IPortalReaderBrowser>(new InvalidOperationException("private-canary")) : Task.FromResult<IPortalReaderBrowser>(browser));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sessions.OpenAsync(Actor, Organization, default));
        await sessions.OpenAsync(Actor, Organization, default);
        await sessions.DisposeAsync();
        Assert.Equal(1, browser.Closes);
        Assert.Equal("CONTROL_READER_HOST_CLOSED", (await Assert.ThrowsAsync<PortalFault>(() => sessions.OpenAsync(Actor, Organization, default))).Code);
        await sessions.DisposeAsync();
        Assert.Equal(1, browser.Closes);
    }

    [Fact]
    public async Task HostShutdownAttemptsEveryBrowserAndReportsUnconfirmedCleanup()
    {
        var first = new ReaderBrowserStub { FailClose = true }; var second = new ReaderBrowserStub(); var opens = 0;
        var sessions = NewSessions(() => Task.FromResult<IPortalReaderBrowser>(++opens == 1 ? first : second));
        await sessions.OpenAsync(Actor, Organization, default);
        await sessions.OpenAsync(Other, Organization, default);
        var failure = await Assert.ThrowsAsync<AggregateException>(() => sessions.DisposeAsync().AsTask());
        Assert.Equal("CONTROL_READER_CLOSE_UNCONFIRMED", Assert.IsType<PortalFault>(Assert.Single(failure.InnerExceptions)).Code);
        Assert.Equal(1, first.Closes); Assert.Equal(1, second.Closes);
    }

    private static PortalReaderSessions NewSessions(Func<Task<IPortalReaderBrowser>> open) => new(new ReaderProjectStub(), new FakeTimeProvider(), open);
}

internal sealed class ReaderBrowserStub : IPortalReaderBrowser
{
    public static AccessibilitySnapshot DefaultSnapshot => new("document", [new("heading", "heading", "Exposed fixture", HeadingLevel: 1)], []);
    public AccessibilitySnapshot Snapshot { get; init; } = DefaultSnapshot;
    public bool FailCapture { get; init; }
    public bool FailClose { get; set; }
    public int Captures { get; private set; }
    public int Closes { get; private set; }
    public TaskCompletionSource CaptureEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource CloseEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource? CaptureRelease { get; init; }
    public TaskCompletionSource? CloseRelease { get; init; }
    public async Task<AccessibilitySnapshot> CaptureAsync()
    {
        Captures++; CaptureEntered.TrySetResult();
        if (CaptureRelease is not null) await CaptureRelease.Task;
        if (FailCapture) throw new PortalFault(503, "CONTROL_READER_CAPTURE_UNCONFIRMED");
        return Snapshot;
    }
    public async ValueTask DisposeAsync()
    {
        Closes++; CloseEntered.TrySetResult();
        if (CloseRelease is not null) await CloseRelease.Task;
        if (FailClose) throw new InvalidOperationException("private-close-canary");
    }
}

internal sealed class ReaderProjectStub : IPortalProjectClient
{
    public bool Hidden { get; set; }
    public int WorkspaceCalls { get; private set; }
    public Task<PortalIdentity> AuthenticateAsync(string bearer, CancellationToken token) => Task.FromResult(
        bearer == "reader-first" ? PortalReaderSessionTests.Actor : bearer == "reader-second" ? new PortalIdentity(PortalProjectClientTests.Actor, "second@example.invalid", bearer)
            : throw new PortalFault(401, "CONTROL_AUTHENTICATION_REQUIRED"));
    public Task<PortalWorkspaceResponse?> WorkspaceAsync(PortalIdentity actor, Guid organization, CancellationToken token)
    { WorkspaceCalls++; return Task.FromResult(Hidden ? null : new PortalWorkspaceResponse(organization, "Fixture workspace")); }
    public Task<PortalWorkspaceBootstrap> EnsureWorkspaceAsync(PortalIdentity actor, CancellationToken token) => throw new NotSupportedException();
    public Task<PortalScanResponse[]> ScansAsync(PortalIdentity actor, Guid organization, CancellationToken token) => throw new NotSupportedException();
    public Task<PortalScanCreatedResponse> CreateScanAsync(PortalIdentity actor, Guid organization, Guid request, CancellationToken token) => throw new NotSupportedException();
    public Task<PortalScanResponse?> ScanAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token) => throw new NotSupportedException();
    public Task<PortalJobResult?> JobAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token) => throw new NotSupportedException();
}
