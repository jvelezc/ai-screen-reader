using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using Microsoft.Extensions.Time.Testing;
using static HeadlessScreenReader.Tests.OwnedScanSessionTests;

namespace HeadlessScreenReader.Tests;

public sealed class PrivateViewerBrokerTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static ViewerScope Scope() => new(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));
    private static RetainedViewerObservation Observation(ViewerScope scope) => new(scope.ScanId, 1, 1,
        new OwnedBrowser().Snapshot() with { DocumentUrl = null }, false);
    private static ViewerConnection Connect(PrivateViewerBroker broker, Guid subject, ViewerScope scope, TimeSpan? lifetime = null) =>
        broker.Exchange(subject, scope, broker.IssueBootstrap(subject, scope, Minute).Secret.Value, lifetime ?? Minute);

    [Fact]
    public async Task Bootstrap_IsSingleExchangeEvenUnderConcurrentAttemptsAndStatusOmitsSecrets()
    {
        var scope = Scope(); var subject = Guid.NewGuid(); await using var broker = new PrivateViewerBroker(scope, subject);
        var bootstrap = broker.IssueBootstrap(subject, scope, Minute);
        var exchanges = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            try { return broker.Exchange(subject, scope, bootstrap.Secret.Value, Minute); }
            catch (BrowserOperationException) { return null; }
        })));
        var lease = Assert.Single(exchanges, c => c is not null)!;
        Assert.Equal(1, broker.Status.ActiveConnections); Assert.Equal(1, broker.Status.IssuedBootstraps);
        Assert.DoesNotContain(bootstrap.Secret.Value, System.Text.Json.JsonSerializer.Serialize(broker.Status));
        Assert.DoesNotContain(lease.Secret.Value, System.Text.Json.JsonSerializer.Serialize(broker.Status));
        Assert.DoesNotContain(lease.Secret.Value, lease.Secret.ToString());
    }

    [Theory]
    [InlineData("subject")]
    [InlineData("scan")]
    [InlineData("session")]
    [InlineData("unknown-token")]
    [InlineData("malformed-token")]
    public async Task DeniedExchange_CannotConsumeTheAuthorizedBootstrap(string partition)
    {
        var scope = Scope(); var subject = Guid.NewGuid(); await using var broker = new PrivateViewerBroker(scope, subject);
        var bootstrap = broker.IssueBootstrap(subject, scope, Minute);
        var foreignScope = partition == "scan" ? scope with { ScanId = Guid.NewGuid().ToString("N") }
            : partition == "session" ? scope with { SessionId = Guid.NewGuid().ToString("N") } : scope;
        var token = partition == "unknown-token" ? new string('0', 64) : partition == "malformed-token" ? "private-malformed" : bootstrap.Secret.Value;
        Assert.Equal("VIEWER_AUTHORITY_DENIED", Assert.Throws<BrowserOperationException>(() => broker.Exchange(
            partition == "subject" ? Guid.NewGuid() : subject, foreignScope, token, Minute)).Code);
        Assert.Equal(0, broker.Status.ActiveConnections);
        Assert.NotEqual(Guid.Empty, broker.Exchange(subject, scope, bootstrap.Secret.Value, Minute).Id);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(60000)]
    public async Task BootstrapExpiry_UsesMonotonicExactBoundsAndIgnoresWallClockShift(int milliseconds)
    {
        var time = new SkewTimeProvider(); var scope = Scope(); var subject = Guid.NewGuid();
        await using var broker = new PrivateViewerBroker(scope, subject, time);
        var first = broker.IssueBootstrap(subject, scope, TimeSpan.FromMilliseconds(milliseconds));
        var second = broker.IssueBootstrap(subject, scope, TimeSpan.FromMilliseconds(milliseconds));
        time.UtcOffset = TimeSpan.FromDays(1);
        time.Advance(TimeSpan.FromMilliseconds(milliseconds - 1));
        Assert.NotEqual(Guid.Empty, broker.Exchange(subject, scope, first.Secret.Value, Minute).Id);
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Throws<BrowserOperationException>(() => broker.Exchange(subject, scope, second.Secret.Value, Minute));
    }

    [Theory]
    [InlineData("bootstrap", 999)]
    [InlineData("bootstrap", 60001)]
    [InlineData("connection", 999)]
    [InlineData("connection", 120001)]
    public async Task InvalidLifetime_CannotConsumeGrantOrBootstrap(string stage, int milliseconds)
    {
        var scope = Scope(); var subject = Guid.NewGuid(); await using var broker = new PrivateViewerBroker(scope, subject);
        if (stage == "bootstrap")
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => broker.IssueBootstrap(subject, scope, TimeSpan.FromMilliseconds(milliseconds)));
            Assert.Equal(0, broker.Status.IssuedBootstraps);
        }
        else
        {
            var ticket = broker.IssueBootstrap(subject, scope, Minute);
            Assert.Throws<ArgumentOutOfRangeException>(() => broker.Exchange(subject, scope, ticket.Secret.Value, TimeSpan.FromMilliseconds(milliseconds)));
            Assert.NotEqual(Guid.Empty, broker.Exchange(subject, scope, ticket.Secret.Value, Minute).Id);
        }
    }

    [Theory]
    [InlineData("subject")]
    [InlineData("scan")]
    [InlineData("session")]
    [InlineData("id")]
    [InlineData("secret")]
    public async Task EveryReadAndRevoke_RequiresExactAuthenticatedScopeAndConnection(string partition)
    {
        var scope = Scope(); var subject = Guid.NewGuid(); await using var broker = new PrivateViewerBroker(scope, subject);
        var lease = Connect(broker, subject, scope); var calls = 0;
        var identity = partition == "subject" ? Guid.NewGuid() : subject;
        var requestedScope = partition == "scan" ? scope with { ScanId = Guid.NewGuid().ToString("N") }
            : partition == "session" ? scope with { SessionId = Guid.NewGuid().ToString("N") } : scope;
        var id = partition == "id" ? Guid.NewGuid() : lease.Id; var secret = partition == "secret" ? new string('0', 64) : lease.Secret.Value;
        await Assert.ThrowsAsync<BrowserOperationException>(() => broker.DeliverAsync(identity, requestedScope, id, secret, 1,
            (_, _) => { calls++; return Task.CompletedTask; }, (_, _) => Task.CompletedTask));
        await Assert.ThrowsAsync<BrowserOperationException>(() => broker.RevokeAsync(identity, requestedScope, id, secret));
        Assert.Equal(0, calls); Assert.Equal(1, broker.Status.ActiveConnections);
        await broker.RevokeAsync(subject, scope, lease.Id, lease.Secret.Value);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(120000)]
    public async Task ConnectionExpiry_ClosesReadsAtExactMonotonicBoundary(int milliseconds)
    {
        var time = new FakeTimeProvider(); var scope = Scope(); var subject = Guid.NewGuid();
        await using var broker = new PrivateViewerBroker(scope, subject, time);
        var lease = Connect(broker, subject, scope, TimeSpan.FromMilliseconds(milliseconds)); var sends = 0;
        time.Advance(TimeSpan.FromMilliseconds(milliseconds - 1));
        await broker.DeliverAsync(subject, scope, lease.Id, lease.Secret.Value, 1,
            (send, _) => send(Observation(scope)), (_, _) => { sends++; return Task.CompletedTask; });
        time.Advance(TimeSpan.FromMilliseconds(1));
        await Assert.ThrowsAsync<BrowserOperationException>(() => broker.DeliverAsync(subject, scope, lease.Id, lease.Secret.Value, 1,
            (send, _) => send(Observation(scope)), (_, _) => { sends++; return Task.CompletedTask; }));
        Assert.Equal(1, sends); Assert.Equal(0, broker.Status.ActiveConnections);
        await broker.RevokeAsync(subject, scope, lease.Id, lease.Secret.Value);
    }

    [Theory]
    [InlineData("revoke")]
    [InlineData("expiry")]
    [InlineData("close")]
    public async Task Revocation_RejectsLateSourceAndWaitsActualSourceAfterCancelledCaller(string stop)
    {
        var time = new FakeTimeProvider(); var scope = Scope(); var subject = Guid.NewGuid();
        var broker = new PrivateViewerBroker(scope, subject, time); var lease = Connect(broker, subject, scope, TimeSpan.FromSeconds(1));
        var started = NewSignal(); var release = NewSignal(); var sends = 0;
        var actual = broker.DeliverAsync(subject, scope, lease.Id, lease.Secret.Value, 1, async (send, _) =>
        { started.SetResult(); await release.Task; await send(Observation(scope)); }, (_, _) => { sends++; return Task.CompletedTask; });
        await started.Task;
        using var wait = new CancellationTokenSource(); var caller = actual.WaitAsync(wait.Token); wait.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => caller);
        Task stopped;
        if (stop == "close") stopped = broker.DisposeAsync().AsTask();
        else { if (stop == "expiry") time.Advance(TimeSpan.FromSeconds(1)); stopped = broker.RevokeAsync(subject, scope, lease.Id, lease.Secret.Value); }
        Assert.False(stopped.IsCompleted); Assert.Equal(1, broker.Status.ActiveReads);
        release.SetResult(); await Assert.ThrowsAsync<BrowserOperationException>(() => actual); await stopped;
        Assert.Equal(0, sends); Assert.Equal(0, broker.Status.ActiveReads);
        await broker.DisposeAsync();
    }

    [Fact]
    public async Task UnawaitedTransportDelivery_RemainsActualWorkUntilItSettlesAndDrainTimeoutStaysClosed()
    {
        var time = new FakeTimeProvider(); var scope = Scope(); var subject = Guid.NewGuid();
        var broker = new PrivateViewerBroker(scope, subject, time); var lease = Connect(broker, subject, scope);
        var sent = NewSignal(); var release = NewSignal();
        var actual = broker.DeliverAsync(subject, scope, lease.Id, lease.Secret.Value, 1,
            (send, unusedToken) => { _ = send(Observation(scope)); return Task.CompletedTask; },
            (_, _) => { sent.SetResult(); return release.Task; });
        await sent.Task; Assert.False(actual.IsCompleted);
        var revoke = broker.RevokeAsync(subject, scope, lease.Id, lease.Secret.Value);
        Assert.False(revoke.IsCompleted); time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal("VIEWER_STOP_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => revoke)).Code);
        Assert.Equal(1, broker.Status.ActiveReads); release.SetResult(); await actual;
        await Assert.ThrowsAsync<BrowserOperationException>(() => broker.DeliverAsync(subject, scope, lease.Id, lease.Secret.Value, 1,
            (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask));
        Assert.Equal(0, broker.Status.ActiveReads);
        await Assert.ThrowsAsync<BrowserOperationException>(() => broker.DisposeAsync().AsTask());
    }

    [Theory]
    [InlineData("scan")]
    [InlineData("epoch")]
    [InlineData("snapshot-epoch")]
    [InlineData("url")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("null")]
    [InlineData("ordinal")]
    public async Task InvalidOrMissingSource_CannotDeliverAnInventedObservation(string partition)
    {
        var scope = Scope(); var subject = Guid.NewGuid(); await using var broker = new PrivateViewerBroker(scope, subject);
        var lease = Connect(broker, subject, scope); var observation = Observation(scope); var sends = 0;
        observation = partition switch
        {
            "scan" => observation with { ScanId = Guid.NewGuid().ToString("N") },
            "epoch" => observation with { Epoch = 2 },
            "snapshot-epoch" => observation with { Snapshot = observation.Snapshot with { CaptureEpoch = 2 } },
            "url" => observation with { Snapshot = observation.Snapshot with { DocumentUrl = "https://private.invalid" } },
            "ordinal" => observation with { Ordinal = -1 },
            _ => observation
        };
        await Assert.ThrowsAsync<BrowserOperationException>(() => broker.DeliverAsync(subject, scope, lease.Id, lease.Secret.Value, 1,
            async (send, _) => { if (partition == "missing") return; await send(partition == "null" ? null! : observation); if (partition == "duplicate") await send(observation); },
            (_, _) => { sends++; return Task.CompletedTask; }));
        Assert.Equal(partition == "duplicate" ? 1 : 0, sends); Assert.Equal(0, broker.Status.ActiveReads);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    public async Task GrantAndReadBudgets_RejectBeforeSourceAndDoNotConsumeReplacementAuthority(int maximum)
    {
        var scope = Scope(); var subject = Guid.NewGuid(); await using var broker = new PrivateViewerBroker(scope, subject, maximumGrants: maximum);
        var lease = Connect(broker, subject, scope);
        for (var i = 1; i < maximum; i++) broker.IssueBootstrap(subject, scope, Minute);
        Assert.Equal("VIEWER_GRANT_LIMIT", Assert.Throws<BrowserOperationException>(() => broker.IssueBootstrap(subject, scope, Minute)).Code);
        Assert.Equal(maximum, broker.Status.IssuedBootstraps);
        var release = NewSignal(); var started = NewSignal(); var calls = 0;
        var first = broker.DeliverAsync(subject, scope, lease.Id, lease.Secret.Value, 1,
            async (send, _) => { started.SetResult(); await release.Task; await send(Observation(scope)); }, (_, _) => Task.CompletedTask);
        await started.Task;
        Assert.Equal("VIEWER_READ_BUSY", (await Assert.ThrowsAsync<BrowserOperationException>(() => broker.DeliverAsync(subject, scope, lease.Id, lease.Secret.Value, 1,
            (_, _) => { calls++; return Task.CompletedTask; }, (_, _) => Task.CompletedTask))).Code);
        Assert.Equal(0, calls); release.SetResult(); await first;
    }
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Theory]
    [InlineData("scan")]
    [InlineData("session")]
    [InlineData("subject")]
    [InlineData("budget-low")]
    [InlineData("budget-high")]
    public void InvalidBrokerScopeOrBounds_CannotCreateAuthority(string partition)
    {
        var scope = Scope();
        Assert.ThrowsAny<ArgumentException>(() => new PrivateViewerBroker(
            partition == "scan" ? scope with { ScanId = "private-id" } : partition == "session" ? scope with { SessionId = "" } : scope,
            partition == "subject" ? Guid.Empty : Guid.NewGuid(), maximumGrants: partition == "budget-low" ? 0 : partition == "budget-high" ? 17 : 16));
    }

    [Theory]
    [InlineData("subject")]
    [InlineData("scope")]
    public async Task UnauthorizedIssuer_CannotConsumeTheTrustedGrantBudget(string partition)
    {
        var scope = Scope(); var subject = Guid.NewGuid(); await using var broker = new PrivateViewerBroker(scope, subject, maximumGrants: 1);
        Assert.Throws<BrowserOperationException>(() => broker.IssueBootstrap(partition == "subject" ? Guid.NewGuid() : subject,
            partition == "scope" ? Scope() : scope, Minute));
        Assert.Equal(0, broker.Status.IssuedBootstraps); Assert.NotNull(broker.IssueBootstrap(subject, scope, Minute));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidReadEpoch_RejectsBeforeSource(long epoch)
    {
        var scope = Scope(); var subject = Guid.NewGuid(); await using var broker = new PrivateViewerBroker(scope, subject);
        var lease = Connect(broker, subject, scope); var calls = 0;
        await Assert.ThrowsAsync<BrowserOperationException>(() => broker.DeliverAsync(subject, scope, lease.Id, lease.Secret.Value, epoch,
            (_, _) => { calls++; return Task.CompletedTask; }, (_, _) => Task.CompletedTask));
        Assert.Equal(0, calls); Assert.Equal(0, broker.Status.ActiveReads);
    }

    [Fact]
    public async Task SettledSource_CannotUseAnEscapedPublicationCallbackLater()
    {
        var scope = Scope(); var subject = Guid.NewGuid(); await using var broker = new PrivateViewerBroker(scope, subject);
        var lease = Connect(broker, subject, scope); Func<RetainedViewerObservation, Task>? escaped = null; var sends = 0;
        await Assert.ThrowsAsync<BrowserOperationException>(() => broker.DeliverAsync(subject, scope, lease.Id, lease.Secret.Value, 1,
            (send, _) => { escaped = send; return Task.CompletedTask; }, (_, _) => { sends++; return Task.CompletedTask; }));
        await Assert.ThrowsAsync<BrowserOperationException>(() => escaped!(Observation(scope)));
        Assert.Equal(0, sends); Assert.Equal(0, broker.Status.ActiveReads);
    }

    [Fact]
    public async Task GlobalReadBound_AdmitsFourActualReadsAndRejectsFifthBeforeItsSource()
    {
        var scope = Scope(); var subject = Guid.NewGuid(); await using var broker = new PrivateViewerBroker(scope, subject);
        var connections = Enumerable.Range(0, 5).Select(_ => Connect(broker, subject, scope)).ToArray();
        var release = NewSignal(); var sourceCalls = 0;
        var reads = connections.Take(4).Select(connection => broker.DeliverAsync(subject, scope, connection.Id, connection.Secret.Value, 1,
            async (send, _) => { sourceCalls++; await release.Task; await send(Observation(scope)); }, (_, _) => Task.CompletedTask)).ToArray();
        Assert.Equal(4, broker.Status.ActiveReads);
        await Assert.ThrowsAsync<BrowserOperationException>(() => broker.DeliverAsync(subject, scope, connections[4].Id, connections[4].Secret.Value, 1,
            (_, _) => { sourceCalls++; return Task.CompletedTask; }, (_, _) => Task.CompletedTask));
        Assert.Equal(4, sourceCalls); release.SetResult(); await Task.WhenAll(reads); Assert.Equal(0, broker.Status.ActiveReads);
    }
    private sealed class SkewTimeProvider : TimeProvider
    {
        private readonly FakeTimeProvider _monotonic = new();
        public TimeSpan UtcOffset { get; set; }
        public override DateTimeOffset GetUtcNow() => _monotonic.GetUtcNow() + UtcOffset;
        public override long GetTimestamp() => _monotonic.GetTimestamp();
        public override long TimestampFrequency => _monotonic.TimestampFrequency;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => _monotonic.CreateTimer(callback, state, dueTime, period);
        public void Advance(TimeSpan value) => _monotonic.Advance(value);
    }
}
