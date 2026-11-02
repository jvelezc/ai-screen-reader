using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class PortalReaderAuthorityTests
{
    [Theory]
    [InlineData("initialize", 401, false)]
    [InlineData("observe", 401, false)]
    [InlineData("command", 401, false)]
    [InlineData("initialize", 404, false)]
    [InlineData("observe", 404, false)]
    [InlineData("command", 404, false)]
    [InlineData("initialize", 401, true)]
    [InlineData("observe", 401, true)]
    [InlineData("command", 401, true)]
    [InlineData("initialize", 404, true)]
    [InlineData("observe", 404, true)]
    [InlineData("command", 404, true)]
    public async Task ActualHttpPostWorkDenialIsSafeProblemWithoutCapturedData(string operation, int status, bool failClose)
    {
        var project = new AuthorityProject();
        var browser = new AuthorityBrowser(operation) { FailClose = failClose };
        var readers = new PortalReaderSessions(project, new FakeTimeProvider(), (_, _) => browser);
        await using var server = await HostedControlServer.StartAsync(new(PortalProjectClientTests.Key,
            Path.GetFullPath(Path.Combine("artifacts", "reader-authority-http", Guid.NewGuid().ToString("N"))),
            new string('a', 40), 0), project, new PortalObservationStub(), readerSessions: readers);
        using var http = new HttpClient { BaseAddress = server.BaseAddress };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "reader-first");
        var actor = PortalReaderSessionTests.Actor;
        var organization = PortalReaderSessionTests.Organization;
        var collection = $"/api/workspaces/{organization:D}/reader-sessions";
        PortalReaderSessionResponse? opened = null;
        PortalReaderSnapshotResponse? observation = null;
        try
        {
            if (operation != "initialize") opened = await readers.OpenAsync(actor, organization, default);
            if (operation == "command") observation = await readers.ObserveAsync(actor, organization, opened!.SessionId, default);
            var pending = operation switch
            {
                "initialize" => http.PostAsJsonAsync(collection, new { }),
                "observe" => http.PostAsJsonAsync($"{collection}/{opened!.SessionId:D}/observations", new { }),
                _ => http.PostAsJsonAsync($"{collection}/{opened!.SessionId:D}/commands",
                    new PortalReaderCommandRequest(nameof(ReaderCommand.NextHeading), observation!.Snapshot.DocumentId))
            };
            await browser.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            project.Hidden = status == 404;
            project.Failure = status == 401 ? new(401, "CONTROL_AUTHENTICATION_REQUIRED") : null;
            browser.Release.SetResult();
            using var response = await pending;
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal((HttpStatusCode)status, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "");
            using var document = JsonDocument.Parse(body);
            var expected = failClose ? "CONTROL_READER_ACCESS_CHANGED_CLOSE_UNCONFIRMED"
                : status == 401 ? "CONTROL_AUTHENTICATION_REQUIRED" : "CONTROL_WORKSPACE_NOT_FOUND";
            Assert.Equal(expected, document.RootElement.GetProperty("title").GetString());
            Assert.Equal(expected, document.RootElement.GetProperty("detail").GetString());
            Assert.Equal(status, document.RootElement.GetProperty("status").GetInt32());
            Assert.Matches("^[a-f0-9]{32}$", document.RootElement.GetProperty("supportReference").GetString()!);
            Assert.False(document.RootElement.TryGetProperty("snapshot", out _));
            Assert.DoesNotContain("Exposed fixture", body);
            Assert.DoesNotContain("private-authority-close-canary", body);
            Assert.DoesNotContain("reader-first", body);
            Assert.Equal(1, browser.Closes);
            Assert.Equal(!failClose, browser.CleanupConfirmed);
            if (status == 401) Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        }
        finally
        {
            browser.FailClose = false;
            browser.Release.TrySetResult();
            await readers.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("initialize")]
    [InlineData("observe")]
    [InlineData("command")]
    public async Task NativePolicyFailureAndFailedCleanupCannotBeRelabeledAsMembershipLoss(string operation)
    {
        var project = new AuthorityProject();
        var browser = new AuthorityBrowser(operation) { FailClose = true };
        var readers = new PortalReaderSessions(project, new FakeTimeProvider(), (_, _) => browser);
        try
        {
            var work = await StartWorkAsync(readers, operation);
            await browser.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            browser.NativeFailure = new(403, "CONTROL_READER_NATIVE_POLICY_DENIED");
            browser.Release.SetResult();
            var failure = await Assert.ThrowsAsync<PortalFault>(async () => await work);
            Assert.Equal(503, failure.Status);
            Assert.Equal("CONTROL_READER_CLOSE_UNCONFIRMED", failure.Code);
            Assert.False(browser.CleanupConfirmed);
            Assert.Equal(1, browser.Closes);
            Assert.Null(project.Failure);
            Assert.False(project.Hidden);
        }
        finally
        {
            browser.FailClose = false;
            browser.Release.TrySetResult();
            await readers.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("initialize", 401, "CONTROL_AUTHENTICATION_REQUIRED")]
    [InlineData("observe", 401, "CONTROL_AUTHENTICATION_REQUIRED")]
    [InlineData("command", 401, "CONTROL_AUTHENTICATION_REQUIRED")]
    [InlineData("initialize", 403, "CONTROL_WORKSPACE_FORBIDDEN")]
    [InlineData("observe", 403, "CONTROL_WORKSPACE_FORBIDDEN")]
    [InlineData("command", 403, "CONTROL_WORKSPACE_FORBIDDEN")]
    [InlineData("initialize", 503, "CONTROL_PROVIDER_UNAVAILABLE")]
    [InlineData("observe", 503, "CONTROL_PROVIDER_UNAVAILABLE")]
    [InlineData("command", 503, "CONTROL_PROVIDER_UNAVAILABLE")]
    public async Task ProviderExpiryDenialOrOutageAfterNativeWorkCannotBecomeAResult(string operation, int status, string code)
    {
        var project = new AuthorityProject();
        var browser = new AuthorityBrowser(operation);
        var opens = 0;
        await using var readers = new PortalReaderSessions(project, new FakeTimeProvider(),
            (_, _) => ++opens == 1 ? browser : new ReaderBrowserStub());
        var work = await StartWorkAsync(readers, operation);
        await browser.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(work.IsCompleted);
        project.Failure = new(status, code);
        browser.Release.SetResult();

        var failure = await Assert.ThrowsAsync<PortalFault>(async () => await work);
        Assert.Equal(status, failure.Status);
        Assert.Equal(code, failure.Code);
        Assert.Equal(1, browser.Closes);
        Assert.True(browser.CleanupConfirmed);
        Assert.All(project.Calls, call =>
        {
            Assert.Same(PortalReaderSessionTests.Actor, call.Actor);
            Assert.Equal(PortalReaderSessionTests.Organization, call.Organization);
        });
        project.Failure = null;
        Assert.Equal("Open", (await readers.OpenAsync(PortalReaderSessionTests.Actor, PortalReaderSessionTests.Organization, default)).State);
        Assert.Equal(2, opens);
    }

    [Theory]
    [InlineData("initialize", 404)]
    [InlineData("observe", 404)]
    [InlineData("command", 404)]
    [InlineData("initialize", 401)]
    [InlineData("observe", 401)]
    [InlineData("command", 401)]
    public async Task KnownAccessLossWithFailedCleanupClearsAccessWithoutAcknowledgingRetirement(string operation, int status)
    {
        var project = new AuthorityProject();
        var browser = new AuthorityBrowser(operation) { FailClose = true };
        var opens = 0;
        var ownedSession = Guid.Empty;
        var readers = new PortalReaderSessions(project, new FakeTimeProvider(), (_, _, session) =>
        {
            opens++;
            if (opens != 1) return new ReaderBrowserStub();
            ownedSession = session;
            return browser;
        });
        try
        {
            var work = await StartWorkAsync(readers, operation);
            await browser.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            project.Hidden = status == 404;
            project.Failure = status == 401 ? new(401, "CONTROL_AUTHENTICATION_REQUIRED") : null;
            browser.Release.SetResult();

            var failure = await Assert.ThrowsAsync<PortalFault>(async () => await work);
            Assert.Equal(status, failure.Status);
            Assert.Equal("CONTROL_READER_ACCESS_CHANGED_CLOSE_UNCONFIRMED", failure.Code);
            Assert.False(browser.CleanupConfirmed);
            Assert.Equal(1, browser.Closes);
            Assert.Equal(1, opens);
            project.Hidden = false;
            project.Failure = null;
            var retained = await Assert.ThrowsAsync<PortalFault>(() => readers.ReadAsync(
                PortalReaderSessionTests.Actor, PortalReaderSessionTests.Organization, ownedSession, default));
            Assert.Equal(503, retained.Status);
            Assert.Equal("CONTROL_READER_CLOSE_UNCONFIRMED", retained.Code);
            Assert.Equal(1, browser.Closes);

            browser.FailClose = false;
            var closed = await readers.CloseAsync(PortalReaderSessionTests.Actor, PortalReaderSessionTests.Organization, ownedSession, default);
            Assert.Equal("Closed", closed.State);
            Assert.True(browser.CleanupConfirmed);
            Assert.Equal(2, browser.Closes);
            Assert.Equal("Open", (await readers.OpenAsync(PortalReaderSessionTests.Actor, PortalReaderSessionTests.Organization, default)).State);
            Assert.Equal(2, opens);
        }
        finally
        {
            browser.FailClose = false;
            browser.Release.TrySetResult();
            await readers.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("initialize")]
    [InlineData("observe")]
    [InlineData("command")]
    public async Task ProviderOutageAndFailedCleanupCannotInventConfirmedAccessLoss(string operation)
    {
        var project = new AuthorityProject();
        var browser = new AuthorityBrowser(operation) { FailClose = true };
        var readers = new PortalReaderSessions(project, new FakeTimeProvider(), (_, _) => browser);
        try
        {
            var work = await StartWorkAsync(readers, operation);
            await browser.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            project.Failure = new(503, "CONTROL_PROVIDER_UNAVAILABLE");
            browser.Release.SetResult();
            var failure = await Assert.ThrowsAsync<PortalFault>(async () => await work);
            Assert.Equal(503, failure.Status);
            Assert.Equal("CONTROL_READER_CLOSE_UNCONFIRMED", failure.Code);
            Assert.False(browser.CleanupConfirmed);
            Assert.Equal(1, browser.Closes);
        }
        finally
        {
            browser.FailClose = false;
            browser.Release.TrySetResult();
            await readers.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("initialize")]
    [InlineData("observe")]
    [InlineData("command")]
    public async Task PostWorkDenialJoinsActualCloseBeforeReleasingCapacity(string operation)
    {
        var project = new AuthorityProject();
        var close = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var browser = new AuthorityBrowser(operation) { CloseRelease = close };
        var opens = 0;
        await using var readers = new PortalReaderSessions(project, new FakeTimeProvider(),
            (_, _) => ++opens == 1 ? browser : new ReaderBrowserStub());
        var work = await StartWorkAsync(readers, operation);
        await browser.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        project.Hidden = true;
        browser.Release.SetResult();
        await browser.CloseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(work.IsCompleted);
        Assert.False(browser.CleanupConfirmed);
        project.Hidden = false;
        var replacement = readers.OpenAsync(PortalReaderSessionTests.Actor, PortalReaderSessionTests.Organization, default);
        Assert.False(replacement.IsCompleted);
        Assert.Equal(1, opens);
        close.SetResult();
        var denied = await Assert.ThrowsAsync<PortalFault>(async () => await work);
        Assert.Equal(404, denied.Status);
        Assert.Equal("CONTROL_WORKSPACE_NOT_FOUND", denied.Code);
        Assert.True(browser.CleanupConfirmed);
        Assert.Equal(1, browser.Closes);
        Assert.Equal("Open", (await replacement).State);
        Assert.Equal(2, opens);
    }

    [Theory]
    [InlineData("initialize")]
    [InlineData("observe")]
    [InlineData("command")]
    public async Task MembershipLostDuringJoinedNativeWorkCannotPublishItsResult(string operation)
    {
        var project = new ReaderProjectStub();
        var browser = new AuthorityBrowser(operation);
        var opens = 0;
        await using var readers = new PortalReaderSessions(project, new FakeTimeProvider(),
            (_, _) => ++opens == 1 ? browser : new ReaderBrowserStub());
        var actor = PortalReaderSessionTests.Actor;
        var organization = PortalReaderSessionTests.Organization;
        PortalReaderSessionResponse? opened = null;
        PortalReaderSnapshotResponse? observed = null;
        if (operation != "initialize") opened = await readers.OpenAsync(actor, organization, default);
        if (operation == "command") observed = await readers.ObserveAsync(actor, organization, opened!.SessionId, default);
        Task work = operation switch
        {
            "initialize" => readers.OpenAsync(actor, organization, default),
            "observe" => readers.ObserveAsync(actor, organization, opened!.SessionId, default),
            _ => readers.CommandAsync(actor, organization, opened!.SessionId,
                new(nameof(ReaderCommand.NextHeading), observed!.Snapshot.DocumentId), default)
        };
        await browser.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(work.IsCompleted);
        project.Hidden = true;
        browser.Release.SetResult();

        var denied = await Assert.ThrowsAsync<PortalFault>(async () => await work);
        Assert.Equal(404, denied.Status);
        Assert.Equal("CONTROL_WORKSPACE_NOT_FOUND", denied.Code);
        Assert.Equal(1, browser.Closes);
        Assert.True(browser.CleanupConfirmed);
        Assert.Equal(operation == "initialize" ? 0 : operation == "observe" ? 1 : 2, browser.Captures);
        Assert.Equal(1, opens);

        project.Hidden = false;
        var replacement = await readers.OpenAsync(actor, organization, default);
        Assert.Equal("Open", replacement.State);
        Assert.Equal(2, opens);
        if (opened is not null) Assert.NotEqual(opened.SessionId, replacement.SessionId);
    }

    private static async Task<Task> StartWorkAsync(PortalReaderSessions readers, string operation)
    {
        var actor = PortalReaderSessionTests.Actor;
        var organization = PortalReaderSessionTests.Organization;
        if (operation == "initialize") return readers.OpenAsync(actor, organization, default);
        var opened = await readers.OpenAsync(actor, organization, default);
        if (operation == "observe") return readers.ObserveAsync(actor, organization, opened.SessionId, default);
        var observation = await readers.ObserveAsync(actor, organization, opened.SessionId, default);
        return readers.CommandAsync(actor, organization, opened.SessionId,
            new(nameof(ReaderCommand.NextHeading), observation.Snapshot.DocumentId), default);
    }

    private sealed class AuthorityProject : IPortalProjectClient
    {
        private readonly ReaderProjectStub _unused = new();
        public bool Hidden { get; set; }
        public PortalFault? Failure { get; set; }
        public List<(PortalIdentity Actor, Guid Organization)> Calls { get; } = [];
        public Task<PortalWorkspaceResponse?> WorkspaceAsync(PortalIdentity actor, Guid organization, CancellationToken token)
        {
            Calls.Add((actor, organization));
            return Failure is { } failure ? Task.FromException<PortalWorkspaceResponse?>(failure)
                : Task.FromResult(Hidden ? null : new PortalWorkspaceResponse(organization, "Authority fixture"));
        }
        public Task<PortalIdentity> AuthenticateAsync(string bearer, CancellationToken token) => _unused.AuthenticateAsync(bearer, token);
        public Task<PortalWorkspaceBootstrap> EnsureWorkspaceAsync(PortalIdentity actor, CancellationToken token) => _unused.EnsureWorkspaceAsync(actor, token);
        public Task<PortalScanResponse[]> ScansAsync(PortalIdentity actor, Guid organization, CancellationToken token) => _unused.ScansAsync(actor, organization, token);
        public Task<PortalScanCreatedResponse> CreateScanAsync(PortalIdentity actor, Guid organization, Guid request, CancellationToken token) => _unused.CreateScanAsync(actor, organization, request, token);
        public Task<PortalScanResponse?> ScanAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token) => _unused.ScanAsync(actor, organization, scan, token);
        public Task<PortalJobResult?> JobAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token) => _unused.JobAsync(actor, organization, scan, token);
    }

    private sealed class AuthorityBrowser(string operation) : IPortalReaderBrowser
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CloseEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? CloseRelease { get; init; }
        public bool FailClose { get; set; }
        public PortalFault? NativeFailure { get; set; }
        public int Captures { get; private set; }
        public int Closes { get; private set; }
        public bool CleanupConfirmed { get; private set; }

        public Task InitializeAsync() => operation == "initialize" ? WaitForWorkAsync() : Task.CompletedTask;

        public async Task<AccessibilitySnapshot> CaptureAsync()
        {
            Captures++;
            if ((operation == "observe" && Captures == 1) || (operation == "command" && Captures == 2))
                await WaitForWorkAsync();
            return ReaderBrowserStub.DefaultSnapshot;
        }

        private async Task WaitForWorkAsync()
        {
            Entered.TrySetResult();
            await Release.Task;
            if (NativeFailure is { } failure) throw failure;
        }

        public async ValueTask DisposeAsync()
        {
            Closes++;
            CloseEntered.TrySetResult();
            if (CloseRelease is not null) await CloseRelease.Task;
            CleanupConfirmed = !FailClose;
            if (FailClose) throw new IOException("private-authority-close-canary");
        }
    }
}
