using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Diagnostics;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli.HostedControl;

/// <summary>Actual packaged Kestrel/Chromium proof with synthetic identities only; never application-JWT acceptance.</summary>
internal static class IsolatedReaderHttpProof
{
    internal static async Task<int> RunAsync()
    {
        var stage = "configuration";
        try
        {
            var image = Environment.GetEnvironmentVariable("HSR_WORKER_IMAGE") ?? throw new InvalidOperationException();
            var root = Environment.GetEnvironmentVariable("HSR_WORKER_EVIDENCE") ?? throw new InvalidOperationException();
            var runtime = Environment.GetEnvironmentVariable("HSR_WORKER_RUNTIME") ?? "podman";
            var proof = await CheckAsync(root, runtime, image, Environment.GetEnvironmentVariable("HSR_WORKER_SECCOMP"), value => stage = value).ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(proof));
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Schema = "hsr-isolated-reader-http-proof-v1",
                State = "Unconfirmed",
                Stage = stage,
                ErrorCode = "CONTROL_ISOLATED_READER_PROOF_UNCONFIRMED",
                HttpStatus = error is HttpRequestException http ? (int?)http.StatusCode : null,
                FailureKind = error is JsonException ? "json-contract" : error is PortalFault ? "control-operation" : "operation",
                SyntheticIdentityOnly = true,
                AppJwtValidated = false,
                HostedTenantReaderAccepted = false
            }));
            return 3;
        }
    }

    internal static async Task<object> CheckAsync(string root, string runtime, string image, string? seccomp = null, Action<string>? setStage = null)
    {
        var organization = Guid.NewGuid();
        var project = new SyntheticReaderProject(organization);
        var adapters = new List<PortalHostedReaderBrowser>();
        setStage?.Invoke("reader-recovery");
        await using var nativeHost = await ReaderProofHostProcess.StartAsync(root, runtime, image, seccomp).ConfigureAwait(false);
        var client = new ReaderHostClient(root, image);
        if (!await client.ReadyAsync().ConfigureAwait(false)) throw new InvalidDataException();
        await using var readers = new PortalReaderSessions(project, TimeProvider.System, (subject, tenant, session) =>
        {
            var adapter = new PortalHostedReaderBrowser(client, subject, tenant, session);
            adapters.Add(adapter); return adapter;
        });
        var options = new PortalOptions("sb_publishable_synthetic_reader_canary_only", root, new string('a', 40), 0);
        setStage?.Invoke("listener-start");
        await using var server = await HostedControlServer.StartAsync(options, project, new PortalObservationReader(root),
            readerSessions: readers, readerRuntimeReady: client.ReadyAsync).ConfigureAwait(false);
        using var handler = new SocketsHttpHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { BaseAddress = server.BaseAddress, Timeout = TimeSpan.FromSeconds(35) };
        var collection = $"/api/workspaces/{organization:D}/reader-sessions";
        http.DefaultRequestHeaders.Authorization = new("Bearer", SyntheticReaderProject.Owner);
        setStage?.Invoke("open-request");
        using var openedResponse = await http.PostAsJsonAsync(collection, new PortalEmptyRequest(), PortalJson.Options).ConfigureAwait(false);
        openedResponse.EnsureSuccessStatusCode();
        if (openedResponse.StatusCode != HttpStatusCode.Created) throw new InvalidDataException();
        setStage?.Invoke("open-contract");
        var opened = await ReadAsync<PortalReaderSessionResponse>(openedResponse).ConfigureAwait(false);
        var resource = collection + $"/{opened.SessionId:D}";
        if (opened.OrganizationId != organization || opened.State != "Open" || !opened.ReadOnly
            || openedResponse.Headers.Location?.OriginalString != resource) throw new InvalidDataException();
        var duplicate = await client.ExchangeAsync(client.Endpoint(), ReaderHostOperation.Open, project.OwnerId, organization, Guid.NewGuid()).ConfigureAwait(false);
        if (duplicate.ErrorCode != "READER_HOST_SUBJECT_ALREADY_OPEN") throw new InvalidDataException();
        var siblingSubject = Guid.NewGuid(); var siblingOrganization = Guid.NewGuid(); var siblingSession = Guid.NewGuid();
        var sibling = await client.ExchangeAsync(client.Endpoint(), ReaderHostOperation.Open, siblingSubject, siblingOrganization, siblingSession).ConfigureAwait(false);
        if (sibling.ErrorCode is not null) throw new InvalidDataException();
        var capacity = await client.ExchangeAsync(client.Endpoint(), ReaderHostOperation.Open, Guid.NewGuid(), organization, Guid.NewGuid()).ConfigureAwait(false);
        if (capacity.ErrorCode != "READER_HOST_CAPACITY_EXHAUSTED") throw new InvalidDataException();
        var wrongScope = await client.ExchangeAsync(client.Endpoint(), ReaderHostOperation.Retire, project.OwnerId, siblingOrganization, siblingSession).ConfigureAwait(false);
        var wrongOrganization = await client.ExchangeAsync(client.Endpoint(), ReaderHostOperation.Observe, project.OwnerId, siblingOrganization, opened.SessionId).ConfigureAwait(false);
        if (wrongScope.ErrorCode != "READER_HOST_SESSION_NOT_FOUND" || wrongOrganization.ErrorCode != "READER_HOST_SESSION_NOT_FOUND") throw new InvalidDataException();
        var siblingSnapshot = await client.ExchangeAsync(client.Endpoint(), ReaderHostOperation.Observe, siblingSubject, siblingOrganization, siblingSession, sibling.DocumentId).ConfigureAwait(false);
        if (siblingSnapshot.ErrorCode is not null) throw new InvalidDataException();
        var siblingRetired = await client.ExchangeAsync(client.Endpoint(), ReaderHostOperation.Retire, siblingSubject, siblingOrganization, siblingSession).ConfigureAwait(false);
        if (siblingRetired.Retirement is not { NativeShutdownConfirmed: true, ContainerAbsenceConfirmed: true }) throw new InvalidDataException();
        setStage?.Invoke("first-observation");
        using var firstResponse = await http.PostAsJsonAsync(resource + "/observations", new PortalEmptyRequest(), PortalJson.Options).ConfigureAwait(false);
        var first = await ReadAsync<PortalReaderSnapshotResponse>(firstResponse).ConfigureAwait(false);
        setStage?.Invoke("second-observation");
        using var secondResponse = await http.PostAsJsonAsync(resource + "/observations", new PortalEmptyRequest(), PortalJson.Options).ConfigureAwait(false);
        var second = await ReadAsync<PortalReaderSnapshotResponse>(secondResponse).ConfigureAwait(false);
        if (first.Snapshot.DocumentId != second.Snapshot.DocumentId || first.Snapshot.Nodes.IsDefaultOrEmpty
            || first.DispatchedInputs != 0 || second.DispatchedInputs != 0 || first.GoalVerified || second.GoalVerified)
            throw new InvalidDataException();
        setStage?.Invoke("authority-denial");
        http.DefaultRequestHeaders.Authorization = new("Bearer", SyntheticReaderProject.Other);
        using var wrongOwner = await http.DeleteAsync(resource).ConfigureAwait(false);
        if (wrongOwner.StatusCode != HttpStatusCode.NotFound) throw new InvalidDataException();
        http.DefaultRequestHeaders.Authorization = null;
        using var anonymous = await http.GetAsync(resource).ConfigureAwait(false);
        if (anonymous.StatusCode != HttpStatusCode.Unauthorized) throw new InvalidDataException();
        http.DefaultRequestHeaders.Authorization = new("Bearer", SyntheticReaderProject.Owner);
        project.Revoked = true;
        using var revoked = await http.PostAsJsonAsync(resource + "/observations", new PortalEmptyRequest(), PortalJson.Options).ConfigureAwait(false);
        if (revoked.StatusCode != HttpStatusCode.NotFound) throw new InvalidDataException();
        project.Revoked = false;
        setStage?.Invoke("close-request");
        using var closedResponse = await http.DeleteAsync(resource).ConfigureAwait(false);
        var closed = await ReadAsync<PortalReaderClosedResponse>(closedResponse).ConfigureAwait(false);
        if (closed.State != "Closed" || closed.SessionId != opened.SessionId || adapters.Count != 1
            || !adapters[0].NativeShutdownConfirmed || !adapters[0].CleanupConfirmed) throw new InvalidDataException();
        using var stale = await http.GetAsync(resource).ConfigureAwait(false);
        if (stale.StatusCode != HttpStatusCode.NotFound) throw new InvalidDataException();
        setStage?.Invoke("server-drain");
        return new
        {
            Schema = "hsr-isolated-reader-http-proof-v1",
            State = "IsolatedReaderHttpReady",
            Image = image,
            SameDocument = true,
            FreshHttpObservations = 2,
            InitialNativeCaptures = 1,
            ObservedNodes = second.Snapshot.Nodes.Length,
            AnonymousDenied = true,
            OtherSubjectDenied = true,
            RevokedWorkspaceDenied = true,
            StaleSessionDenied = true,
            NativeShutdownConfirmed = true,
            ContainerAbsenceConfirmed = true,
            NativeExecutionOutsideApi = true,
            GlobalSubjectReservationConfirmed = true,
            GlobalCapacityConfirmed = true,
            WorkerScopeDenied = true,
            SiblingDocumentPreserved = true,
            SyntheticIdentityOnly = true,
            AppJwtValidated = false,
            HostedTenantReaderAccepted = false,
            DatabaseAccessed = false,
            ObservationPersisted = false,
            DispatchedInputs = 0,
            GoalVerified = false
        };
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(PortalJson.Options).ConfigureAwait(false) ?? throw new InvalidDataException();
    }

    private sealed class ReaderProofHostProcess(Process process) : IAsyncDisposable
    {
        public static async Task<ReaderProofHostProcess> StartAsync(string root, string runtime, string image, string? seccomp)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in new[] { "exec", "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, "HeadlessScreenReader.Cli.runtimeconfig.json"),
                "--depsfile", Path.Combine(AppContext.BaseDirectory, "HeadlessScreenReader.Cli.deps.json"),
                Path.Combine(AppContext.BaseDirectory, "HeadlessScreenReader.Worker.dll"), "--host-reader" }) start.ArgumentList.Add(argument);
            start.Environment.Clear();
            foreach (var pair in IsolatedFixtureExecutor.ChildEnvironment()) start.Environment[pair.Key] = pair.Value;
            start.Environment["HSR_WORKER_EVIDENCE"] = root; start.Environment["HSR_WORKER_IMAGE"] = image;
            start.Environment["HSR_WORKER_RUNTIME"] = runtime;
            if (seccomp is not null) start.Environment["HSR_WORKER_SECCOMP"] = seccomp;
            var process = new Process { StartInfo = start };
            var owner = new ReaderProofHostProcess(process);
            try
            {
                if (!process.Start()) throw new IOException();
                if (await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false) != "READER_HOST_READY")
                    throw new IOException();
                return owner;
            }
            catch { await owner.DisposeAsync().ConfigureAwait(false); throw; }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                process.StandardInput.Close();
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40)).ConfigureAwait(false); }
                catch (TimeoutException)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                    throw new IOException("READER_PROOF_HOST_DRAIN_UNCONFIRMED");
                }
                if (process.ExitCode != 0 || (await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false)).Length != 0
                    || (await process.StandardError.ReadToEndAsync().ConfigureAwait(false)).Length != 0)
                    throw new IOException("READER_PROOF_HOST_EXIT_UNCONFIRMED");
            }
            finally { process.Dispose(); }
        }
    }

    private sealed class SyntheticReaderProject(Guid organization) : IPortalProjectClient
    {
        internal const string Owner = "synthetic-reader-owner";
        internal const string Other = "synthetic-reader-other";
        private readonly Guid _owner = Guid.NewGuid(), _other = Guid.NewGuid();
        public Guid OwnerId => _owner;
        private bool _revoked;
        public bool Revoked { get => Volatile.Read(ref _revoked); set => Volatile.Write(ref _revoked, value); }
        public Task<PortalIdentity> AuthenticateAsync(string bearer, CancellationToken token) => Task.FromResult(
            bearer == Owner ? new PortalIdentity(_owner, "owner@example.invalid", bearer)
            : bearer == Other ? new PortalIdentity(_other, "other@example.invalid", bearer)
            : throw new PortalFault(401, "CONTROL_AUTHENTICATION_REQUIRED"));
        public Task<PortalWorkspaceResponse?> WorkspaceAsync(PortalIdentity actor, Guid tenant, CancellationToken token) =>
            Task.FromResult<PortalWorkspaceResponse?>(!Revoked && tenant == organization ? new(organization, "Synthetic native fixture") : null);
        public Task<PortalWorkspaceBootstrap> EnsureWorkspaceAsync(PortalIdentity actor, CancellationToken token) => throw new NotSupportedException();
        public Task<PortalScanResponse[]> ScansAsync(PortalIdentity actor, Guid tenant, CancellationToken token) => throw new NotSupportedException();
        public Task<PortalScanCreatedResponse> CreateScanAsync(PortalIdentity actor, Guid tenant, Guid request, CancellationToken token) => throw new NotSupportedException();
        public Task<PortalScanResponse?> ScanAsync(PortalIdentity actor, Guid tenant, Guid scan, CancellationToken token) => throw new NotSupportedException();
        public Task<PortalJobResult?> JobAsync(PortalIdentity actor, Guid tenant, Guid scan, CancellationToken token) => throw new NotSupportedException();
    }
}
