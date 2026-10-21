using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class FixtureWorkerTests
{
    public static FixtureJob Job() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1);
    public static FixtureObservation Receipt(FixtureJob job) => new(FixtureObservation.CurrentSchema, job,
        "synthetic-shop", FixtureObservation.Profile, FixtureObservation.ContentSha256,
        new("document", [new("heading", "heading", "Synthetic shop")], []) { CaptureEpoch = 1 }, 0, false,
        new(BrowserShutdownPhase.Closed, 0, true, true, true, true, true, false, null)
        {
            CollectorCallSettled = true,
            CollectorStopAcknowledged = true,
            TransportCallSettled = true,
            ProcessCallSettled = true,
            ProcessExitConfirmed = true,
            ProcessExitConfirmationMilliseconds = 12,
            NativeElapsedMilliseconds = 14
        });
    private static byte[] Bytes(FixtureObservation receipt) => JsonSerializer.SerializeToUtf8Bytes(receipt, ObservationStore.Json);

    [Theory]
    [InlineData("organization")]
    [InlineData("scan")]
    [InlineData("job")]
    [InlineData("generation-zero")]
    [InlineData("generation-four")]
    public void DescriptorRejectsMissingIdentityAndUnsupportedAttempts(string field)
    {
        var good = Job();
        var invalid = field switch
        {
            "organization" => good with { OrganizationId = Guid.Empty },
            "scan" => good with { ScanId = Guid.Empty },
            "job" => good with { JobId = Guid.Empty },
            "generation-zero" => good with { Generation = 0 },
            _ => good with { Generation = 4 }
        };
        Assert.Equal("WORKER_DESCRIPTOR_INVALID", Assert.Throws<BrowserOperationException>(invalid.Validate).Code);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void DescriptorBuildsExactTenantScanJobGenerationReference(long generation)
    {
        var job = new FixtureJob(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), generation);
        job.Validate();
        Assert.Equal($"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/cccccccccccccccccccccccccccccccc/{generation}/observation.json", job.Reference);
    }

    [Fact]
    public async Task RealChromiumWorkerObservesBoundedFixtureWithoutInputThenConfirmsOwnedExit()
    {
        var job = Job(); var result = await FixtureWorker.ObserveAsync(job);
        result.Validate(job);
        Assert.Equal(BundledFixtureCatalog.Describe(BundledFixture.Traversal).ContentSha256, result.FixtureSha256);
        Assert.Contains(result.Snapshot.Nodes, n => n.Role == "heading" && n.Name == "Synthetic shop");
        Assert.Equal(0, result.DispatchedInputs); Assert.False(result.GoalVerified);
        Assert.True(result.Shutdown.ProcessExitConfirmed); Assert.Equal(0, result.Shutdown.ActiveCalls);
        Assert.True(result.Shutdown.TransportCallSettled);
        var json = Encoding.UTF8.GetString(Bytes(result));
        Assert.DoesNotContain("traversal-account-canary", json); Assert.DoesNotContain("traversal-secret-canary", json);
        Assert.DoesNotContain("lease_token", json);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"Url\":\"https://example.com\"}")]
    public async Task WorkerRejectsInvalidOrExtendedDescriptorBeforeBrowserLaunch(string json)
    {
        using var output = new MemoryStream();
        var exit = await FixtureWorker.RunAsync(new MemoryStream(Encoding.UTF8.GetBytes(json)), output);
        Assert.Equal(3, exit); Assert.Empty(output.ToArray());
    }
    [Fact]
    public async Task WorkerRejectsOversizedDescriptorWithoutOutput()
    {
        using var output = new MemoryStream();
        Assert.Equal(3, await FixtureWorker.RunAsync(new MemoryStream(new byte[1025]), output));
        Assert.Empty(output.ToArray());
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("scan")]
    [InlineData("job")]
    [InlineData("generation")]
    [InlineData("schema")]
    [InlineData("fixture")]
    [InlineData("profile")]
    [InlineData("digest")]
    [InlineData("input")]
    [InlineData("goal")]
    [InlineData("epoch")]
    [InlineData("empty-document")]
    [InlineData("empty-nodes")]
    public void ReceiptCannotChangeBindingIdentitySemanticsOrObservationClaim(string field)
    {
        var job = Job(); var good = Receipt(job);
        var invalid = field switch
        {
            "organization" => good with { Job = job with { OrganizationId = Guid.NewGuid() } },
            "scan" => good with { Job = job with { ScanId = Guid.NewGuid() } },
            "job" => good with { Job = job with { JobId = Guid.NewGuid() } },
            "generation" => good with { Job = job with { Generation = 2 } },
            "schema" => good with { Schema = "other" },
            "fixture" => good with { Fixture = "another" },
            "profile" => good with { ReaderProfile = "other" },
            "digest" => good with { FixtureSha256 = new('a', 64) },
            "input" => good with { DispatchedInputs = 1 },
            "goal" => good with { GoalVerified = true },
            "epoch" => good with { Snapshot = good.Snapshot with { CaptureEpoch = 2 } },
            "empty-document" => good with { Snapshot = good.Snapshot with { DocumentId = "" } },
            _ => good with { Snapshot = good.Snapshot with { Nodes = [] } }
        };
        Assert.Equal("WORKER_OBSERVATION_UNCONFIRMED", Assert.Throws<BrowserOperationException>(() => invalid.Validate(job)).Code);
    }

    [Theory]
    [InlineData("phase")]
    [InlineData("active-call")]
    [InlineData("context-pending")]
    [InlineData("browser-pending")]
    [InlineData("collector-pending")]
    [InlineData("collector-denied")]
    [InlineData("transport-pending")]
    [InlineData("transport-undisposed")]
    [InlineData("process-pending")]
    [InlineData("exit-unconfirmed")]
    [InlineData("late-exit")]
    [InlineData("late-native")]
    [InlineData("deadline")]
    [InlineData("rejected")]
    [InlineData("fault")]
    [InlineData("root-stop-pending")]
    public void ReceiptRejectsEveryUnconfirmedShutdownBoundary(string field)
    {
        var job = Job(); var good = Receipt(job); var shutdown = good.Shutdown;
        var invalid = field switch
        {
            "phase" => shutdown with { Phase = BrowserShutdownPhase.ClosingBrowser },
            "active-call" => shutdown with { ActiveCalls = 1 },
            "context-pending" => shutdown with { ContextCallSettled = false },
            "browser-pending" => shutdown with { BrowserCallSettled = false },
            "collector-pending" => shutdown with { CollectorCallSettled = false },
            "collector-denied" => shutdown with { CollectorStopAcknowledged = false },
            "transport-pending" => shutdown with { TransportCallSettled = false },
            "transport-undisposed" => shutdown with { TransportDisposed = false },
            "process-pending" => shutdown with { ProcessCallSettled = false },
            "exit-unconfirmed" => shutdown with { ProcessExitConfirmed = false },
            "late-exit" => shutdown with { ProcessExitConfirmationMilliseconds = 4000 },
            "late-native" => shutdown with { NativeElapsedMilliseconds = 5000 },
            "deadline" => shutdown with { BrowserDeadlineExceeded = true },
            "rejected" => shutdown with { BrowserCallRejected = true },
            "fault" => shutdown with { ErrorCode = "CHROMIUM_BROWSER_CLOSE_UNCONFIRMED" },
            _ => shutdown with { OwnedProcessStopRequested = true, OwnedProcessStopCallSettled = false }
        };
        Assert.Equal("WORKER_OBSERVATION_UNCONFIRMED",
            Assert.Throws<BrowserOperationException>(() => (good with { Shutdown = invalid }).Validate(job)).Code);
    }
    [Theory]
    [InlineData(3999.999, 4999.999)]
    [InlineData(0, 0)]
    public void ReceiptAcceptsTimelyActualExitAndSettledNativeBoundary(double exit, double native)
    {
        var job = Job(); var good = Receipt(job);
        var result = good with { Shutdown = good.Shutdown with { ProcessExitConfirmationMilliseconds = exit, NativeElapsedMilliseconds = native } };
        var decoded = ObservationStore.Validate(Bytes(result), job);
        Assert.Equal(job, decoded.Job); Assert.Equal(result.Shutdown, decoded.Shutdown);
        Assert.Equal(result.Snapshot.Nodes.ToArray(), decoded.Snapshot.Nodes.ToArray());
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1_048_577)]
    public void ReceiptByteBoundsRejectEmptyAndOversizedPayload(int size)
    {
        Assert.Equal("WORKER_OBSERVATION_SIZE_INVALID", Assert.Throws<InvalidDataException>(() => ObservationStore.Validate(new byte[size], Job())).Message);
    }
    [Fact]
    public void ReceiptByteLimitAcceptsExactMaximumAndRejectsUnknownFields()
    {
        var job = Job(); var bytes = Bytes(Receipt(job));
        var padded = new byte[FixtureObservation.MaximumBytes]; Array.Fill(padded, (byte)' '); bytes.CopyTo(padded, 0);
        Assert.Equal(job, ObservationStore.Validate(padded, job).Job);
        Assert.Throws<InvalidDataException>(() => ObservationStore.Validate(Encoding.UTF8.GetBytes("{\"Unexpected\":true}"), job));
    }
    [Theory]
    [InlineData("DispatchedInputs")]
    [InlineData("GoalVerified")]
    [InlineData("ReaderProfile")]
    public void ReceiptCannotInventOmittedRequiredObservations(string field)
    {
        var job = Job(); var payload = System.Text.Json.Nodes.JsonNode.Parse(Bytes(Receipt(job)))!.AsObject();
        payload.Remove(field);
        Assert.Throws<JsonException>(() => ObservationStore.Validate(Encoding.UTF8.GetBytes(payload.ToJsonString()), job));
    }
    [Theory]
    [InlineData("BrowserDeadlineExceeded")]
    [InlineData("OwnedProcessStopRequested")]
    [InlineData("NativeElapsedMilliseconds")]
    public void ReceiptRequiresExplicitShutdownFactsEvenWhenDefaultWouldLookSuccessful(string field)
    {
        var job = Job(); var payload = System.Text.Json.Nodes.JsonNode.Parse(Bytes(Receipt(job)))!.AsObject();
        payload["Shutdown"]!.AsObject().Remove(field);
        Assert.Equal("WORKER_SHUTDOWN_FACTS_MISSING", Assert.Throws<InvalidDataException>(() =>
            ObservationStore.Validate(Encoding.UTF8.GetBytes(payload.ToJsonString()), job)).Message);
    }
    [Fact]
    public void DuplicateJsonClaimsAreRejectedRatherThanSelectingOneValue()
    {
        var job = Job(); var json = Encoding.UTF8.GetString(Bytes(Receipt(job))).Replace("\"DispatchedInputs\":0", "\"DispatchedInputs\":1,\"DispatchedInputs\":0", StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => ObservationStore.Validate(Encoding.UTF8.GetBytes(json), job));
    }
    [Theory]
    [InlineData(-1)]
    [InlineData(4000.001)]
    public void ReceiptRejectsNegativeAndLateExitEvidence(double timestamp)
    {
        var job = Job(); var good = Receipt(job);
        Assert.Throws<BrowserOperationException>(() => (good with { Shutdown = good.Shutdown with { ProcessExitConfirmationMilliseconds = timestamp } }).Validate(job));
    }
    [Theory]
    [InlineData(2048, true)]
    [InlineData(2049, false)]
    public void ObservationNodeLimitIsInclusiveAndRejectsOverflow(int count, bool accepted)
    {
        var job = Job(); var good = Receipt(job);
        var result = good with { Snapshot = good.Snapshot with { Nodes = [.. Enumerable.Range(0, count).Select(i => new AccessibleNode(i.ToString(), "heading", "Fixture"))] } };
        if (accepted) result.Validate(job); else Assert.Throws<BrowserOperationException>(() => result.Validate(job));
        Assert.Equal(count, result.Snapshot.Nodes.Length);
    }
    [Theory]
    [InlineData("Host=example.com;Username=worker;Password=disposable;SSL Mode=Disable")]
    [InlineData("Host=example.com;Username=worker;Password=disposable;SSL Mode=Require")]
    [InlineData("Host=127.0.0.1;Username=worker")]
    public void DatabaseConfigurationRequiresExplicitCredentialAndVerifiedTlsAwayFromLoopback(string connection)
    {
        Assert.Throws<ArgumentException>(() => new PostgresWorkerLeases(connection));
    }
    [Fact]
    public async Task PersistedBytesAreExactHashedImmutableAndHaveNoPendingFiles()
    {
        var job = Job(); var bytes = Bytes(Receipt(job)); var root = Path.Combine(Path.GetTempPath(), "hsr-worker-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ObservationStore(root); var stored = await store.PersistAsync(bytes, job, CancellationToken.None);
            Assert.Equal(job.Reference, stored.Reference); Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), stored.Sha256);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(root, stored.Reference)));
            await Assert.ThrowsAsync<IOException>(() => store.PersistAsync(Bytes(Receipt(job) with { Snapshot = Receipt(job).Snapshot with { DocumentId = "changed" } }), job, CancellationToken.None));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(root, stored.Reference)));
            Assert.Empty(Directory.GetFiles(root, "*.pending", SearchOption.AllDirectories));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public void RuntimeChildEnvironmentDoesNotCarryDatabaseOrSupabaseSecrets()
    {
        var keys = IsolatedFixtureExecutor.ChildEnvironment().Keys;
        Assert.DoesNotContain("HSR_WORKER_DATABASE", keys); Assert.DoesNotContain("SUPABASE_ACCESS_TOKEN", keys);
        Assert.DoesNotContain("SUPABASE_SERVICE_ROLE_KEY", keys); Assert.DoesNotContain("PGPASSWORD", keys);
        Assert.All(keys, key => Assert.Contains(key, new[] { "PATH", "SystemRoot", "WINDIR", "TEMP", "TMP", "HOME", "USERPROFILE", "LOCALAPPDATA", "APPDATA", "XDG_RUNTIME_DIR", "DBUS_SESSION_BUS_ADDRESS" }));
    }
    [Theory]
    [InlineData("podman", "latest")]
    [InlineData("podman", "example:tag")]
    [InlineData("cmd", "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void RuntimeRequiresKnownEngineAndImmutableLocalImage(string runtime, string image)
    {
        Assert.Throws<ArgumentException>(() => new IsolatedFixtureExecutor(runtime, image));
    }
    [Fact]
    public void LeaseSerializationAndDiagnosticTextDoNotExposeCapability()
    {
        var secret = Guid.NewGuid(); var lease = new WorkerLease(Job(), Guid.NewGuid(), secret);
        Assert.DoesNotContain(secret.ToString(), JsonSerializer.Serialize(lease)); Assert.DoesNotContain(secret.ToString(), lease.ToString());
    }
}
