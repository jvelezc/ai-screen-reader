using HeadlessScreenReader.Cli.HostedControl;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class PortalReaderInitializationTests
{
    private static PortalIdentity Actor => PortalReaderSessionTests.Actor;
    private static Guid Organization => PortalReaderSessionTests.Organization;
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task ReservationSelectsNonsecretScopeAndStartsExpiryBeforeNativeInitialization()
    {
        var clock = new FakeTimeProvider(); var start = clock.GetUtcNow();
        Guid reserved = default; var project = new ReaderProjectStub();
        var browser = new ReaderBrowserStub { Initializing = () => clock.Advance(TimeSpan.FromSeconds(20)) };
        await using var readers = new PortalReaderSessions(project, clock, (organization, session) =>
        {
            Assert.Equal(1, project.WorkspaceCalls); Assert.Equal(Organization, organization);
            Assert.NotEqual(Guid.Empty, session); reserved = session; return browser;
        });
        var opened = await readers.OpenAsync(Actor, Organization, default);
        Assert.Equal(reserved, opened.SessionId); Assert.Equal(start + TimeSpan.FromMinutes(2), opened.ExpiresAt);
        Assert.Equal(1, browser.Initializations); Assert.Equal(0, browser.Captures);
        Assert.Equal("Open", opened.State); Assert.True(opened.ReadOnly);
    }

    [Theory]
    [InlineData(119999, false)]
    [InlineData(120000, true)]
    public async Task NativeStartupConsumesTheOriginalTwoMinuteLease(int elapsed, bool expired)
    {
        var clock = new FakeTimeProvider(); var start = clock.GetUtcNow();
        var browser = new ReaderBrowserStub { Initializing = () => clock.Advance(TimeSpan.FromMilliseconds(elapsed)) };
        await using var readers = new PortalReaderSessions(new ReaderProjectStub(), clock, (_, _) => browser);
        if (expired)
        {
            var error = await Assert.ThrowsAsync<PortalFault>(() => readers.OpenAsync(Actor, Organization, default));
            Assert.Equal(404, error.Status); Assert.Equal("CONTROL_READER_NOT_FOUND", error.Code);
            Assert.Equal(1, browser.Closes);
        }
        else
        {
            var opened = await readers.OpenAsync(Actor, Organization, default);
            Assert.Equal(start + TimeSpan.FromMinutes(2), opened.ExpiresAt); Assert.Equal("Open", opened.State);
            Assert.Equal(0, browser.Closes);
        }
        Assert.Equal(1, browser.Initializations); Assert.Equal(0, browser.Captures);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 99)]
    public async Task InitialNativeCaptureCountsWithoutServingItAsAFreshHttpObservation(int initial, int remaining)
    {
        var browser = new ReaderBrowserStub { InitialCaptures = initial };
        await using var readers = new PortalReaderSessions(new ReaderProjectStub(), new FakeTimeProvider(), (_, _) => browser);
        var opened = await readers.OpenAsync(Actor, Organization, default);
        for (var i = 0; i < remaining; i++)
        {
            var observed = await readers.ObserveAsync(Actor, Organization, opened.SessionId, default);
            Assert.Equal("document", observed.Snapshot.DocumentId); Assert.Equal(0, observed.DispatchedInputs); Assert.False(observed.GoalVerified);
        }
        var exhausted = await Assert.ThrowsAsync<PortalFault>(() => readers.ObserveAsync(Actor, Organization, opened.SessionId, default));
        Assert.Equal(409, exhausted.Status); Assert.Equal("CONTROL_READER_BUDGET_EXHAUSTED", exhausted.Code);
        Assert.Equal(remaining, browser.Captures); Assert.Equal(0, browser.Closes);
        Assert.Equal("Closed", (await readers.CloseAsync(Actor, Organization, opened.SessionId, default)).State);
    }

    [Fact]
    public async Task CanceledInitializationJoinsActualLaunchAndCleanupBeforeReleasingItsReservation()
    {
        var initialize = Signal(); var close = Signal();
        var browser = new ReaderBrowserStub { InitializeRelease = initialize, CloseRelease = close }; var opens = 0;
        await using var readers = new PortalReaderSessions(new ReaderProjectStub(), new FakeTimeProvider(), (_, _) => ++opens == 1 ? browser : new ReaderBrowserStub());
        using var caller = new CancellationTokenSource();
        var opening = readers.OpenAsync(Actor, Organization, caller.Token);
        await browser.InitializeEntered.Task; caller.Cancel(); Assert.False(opening.IsCompleted);
        initialize.SetResult(); await browser.CloseEntered.Task;
        var replacement = readers.OpenAsync(Actor, Organization, default);
        Assert.False(opening.IsCompleted); Assert.False(replacement.IsCompleted); Assert.Equal(1, opens);
        close.SetResult(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening);
        Assert.Equal("Open", (await replacement).State); Assert.Equal(2, opens); Assert.Equal(1, browser.Closes);
    }

    [Fact]
    public async Task FailedInitializationWithUnknownCleanupRetainsSubjectUntilExplicitRetryConfirmsAbsence()
    {
        var browser = new ReaderBrowserStub { FailInitialize = true, FailClose = true }; var opens = 0;
        var readers = new PortalReaderSessions(new ReaderProjectStub(), new FakeTimeProvider(), (_, _) => ++opens == 1 ? browser : new ReaderBrowserStub());
        try
        {
            var failed = await Assert.ThrowsAsync<PortalFault>(() => readers.OpenAsync(Actor, Organization, default));
            Assert.Equal("CONTROL_READER_CLOSE_UNCONFIRMED", failed.Code); Assert.Equal(503, failed.Status);
            Assert.DoesNotContain("private-initialization-canary", failed.ToString());
            var retry = await Assert.ThrowsAsync<PortalFault>(() => readers.OpenAsync(Actor, Organization, default));
            Assert.Equal("CONTROL_READER_CLOSE_UNCONFIRMED", retry.Code); Assert.Equal(1, opens); Assert.Equal(2, browser.Closes);
            browser.FailClose = false;
            Assert.Equal("Open", (await readers.OpenAsync(Actor, Organization, default)).State);
            Assert.Equal(2, opens); Assert.Equal(3, browser.Closes); Assert.True(browser.CleanupConfirmed);
        }
        finally { browser.FailClose = false; await readers.DisposeAsync(); }
    }

    [Fact]
    public async Task ConfirmedAbsenceReleasesCapacityWhileMissingGracefulAcknowledgementStillReturnsFailure()
    {
        var first = new ReaderBrowserStub { FailClose = true, ProveCleanupOnFailure = true }; var opens = 0;
        await using var readers = new PortalReaderSessions(new ReaderProjectStub(), new FakeTimeProvider(), (_, _) => ++opens == 1 ? first : new ReaderBrowserStub());
        var opened = await readers.OpenAsync(Actor, Organization, default);
        var failed = await Assert.ThrowsAsync<PortalFault>(() => readers.CloseAsync(Actor, Organization, opened.SessionId, default));
        Assert.Equal(503, failed.Status); Assert.Equal("CONTROL_READER_CLOSE_UNCONFIRMED", failed.Code); Assert.True(first.CleanupConfirmed);
        var replacement = await readers.OpenAsync(Actor, Organization, default);
        Assert.NotEqual(opened.SessionId, replacement.SessionId); Assert.Equal("Open", replacement.State); Assert.Equal(2, opens);
    }

    [Fact]
    public async Task OpaqueLegacyFactoryFailureCannotProveThatItsNativeResourcesNeverExisted()
    {
        var opens = 0;
        var readers = new PortalReaderSessions(new ReaderProjectStub(), new FakeTimeProvider(), () =>
        { opens++; return Task.FromException<IPortalReaderBrowser>(new IOException("private-factory-canary")); });
        Assert.Equal("CONTROL_READER_CLOSE_UNCONFIRMED", (await Assert.ThrowsAsync<PortalFault>(() => readers.OpenAsync(Actor, Organization, default))).Code);
        Assert.Equal("CONTROL_READER_CLOSE_UNCONFIRMED", (await Assert.ThrowsAsync<PortalFault>(() => readers.OpenAsync(Actor, Organization, default))).Code);
        Assert.Equal(1, opens);
        Assert.Equal("CONTROL_READER_CLOSE_UNCONFIRMED", Assert.IsType<PortalFault>(Assert.Single((await Assert.ThrowsAsync<AggregateException>(() => readers.DisposeAsync().AsTask())).InnerExceptions)).Code);
    }

    [Fact]
    public async Task HostDrainCutsOffAdmissionAndJoinsInitializationBeforeClosingItsBrowser()
    {
        var release = Signal(); var browser = new ReaderBrowserStub { InitializeRelease = release }; var opens = 0;
        var readers = new PortalReaderSessions(new ReaderProjectStub(), new FakeTimeProvider(), (_, _) => { opens++; return browser; });
        var opening = readers.OpenAsync(Actor, Organization, default); await browser.InitializeEntered.Task;
        var closing = readers.DisposeAsync().AsTask(); Assert.False(closing.IsCompleted);
        var queued = readers.OpenAsync(Actor, Organization, default);
        release.SetResult();
        Assert.Equal("CONTROL_READER_HOST_CLOSED", (await Assert.ThrowsAsync<PortalFault>(() => opening)).Code);
        await closing;
        Assert.Equal("CONTROL_READER_HOST_CLOSED", (await Assert.ThrowsAsync<PortalFault>(() => queued)).Code);
        Assert.Equal(1, opens); Assert.Equal(1, browser.Closes); Assert.True(browser.CleanupConfirmed);
    }
}
