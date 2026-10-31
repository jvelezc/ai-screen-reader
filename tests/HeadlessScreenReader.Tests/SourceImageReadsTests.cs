using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Mcp;
using HeadlessScreenReader.Worker;
using ModelContextProtocol.Client;

namespace HeadlessScreenReader.Tests;

public sealed class SourceImageReadsTests
{
    private static readonly FixtureJob Job = new(PortalProjectClientTests.Organization, PortalProjectClientTests.Scan, PortalProjectClientTests.Job, 1)
    { RecordObservation = true, TraversalRoute = 1 };
    private static readonly Lazy<Task<ImmutableArray<FixtureSourceFrame>>> InterruptedOriginals = new(async () =>
    {
        var frames = ImmutableArray.CreateBuilder<FixtureSourceFrame>();
        await Assert.ThrowsAsync<BrowserOperationException>(() => KeyboardFixtureWorker.RecordAsync(Job, checkpoint: (checkpoint, _) =>
            checkpoint.Phase == TraversalCheckpointPhase.Observed && checkpoint.CommandIndex == 1
                ? Task.FromException(new BrowserOperationException("TEST_SOURCE_INTERRUPTED")) : Task.CompletedTask,
            sourceSink: (values, _) =>
            {
                frames.AddRange(values.Select(value => new FixtureSourceFrame(value.Frame.Sequence, value.Frame.File,
                    value.Frame.PresentationUnixMilliseconds, value.Frame.Sha256, value.Content.ToArray())));
                return Task.CompletedTask;
            }));
        Assert.True(frames.Count >= 3); return frames.ToImmutable();
    });
    private sealed class Files : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "hsr-source-image-api-" + Guid.NewGuid().ToString("N"));
        public string Sources => Path.Combine(Root, ".runtime", "recorded-sources");
        public string Scope => Path.Combine(Sources, $"{Job.OrganizationId:N}-{Job.JobId:N}-{Job.Generation}");
        public Guid Worker { get; } = Guid.NewGuid();
        public RecordedSourceJournal Journal { get; private set; } = null!;
        public Files() => Directory.CreateDirectory(Root);
        public async Task Begin()
        {
            var ownership = new OwnershipJournal(Path.Combine(Root, ".runtime"));
            var native = await ownership.BeginAsync(Job with { RecordObservation = false, TraversalRoute = 0 }, "podman", "sha256:" + new string('a', 64), default);
            Journal = await RecordedSourceJournal.BeginAsync(Sources, native, Job, Worker, default);
        }
        public async Task Stage(bool actual = false)
        {
            await Begin();
            var frames = actual ? (await InterruptedOriginals.Value).Take(3).ToArray() : [ControlFrame()];
            var sequence = 0;
            foreach (var frame in frames)
            {
                var batch = new FixtureSourceBatch(FixtureSourceProtocol.Schema, Journal.Read().Header.Native.Owner, ++sequence, [frame]);
                await Journal.AppendAsync(batch, TraversalArchive.Hash(FixtureProgressProtocol.Encode(batch)), default);
            }
        }
        public RetainedSourceImages Manifest() => SourceImagesProjection.Manifest(Journal.Read(), Job, Worker);
        public void Dispose()
        {
            Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), Path.GetDirectoryName(Path.GetFullPath(Root)));
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
    // Deterministic signature-only byte-boundary control. Actual Chromium originals are exercised separately.
    private static FixtureSourceFrame ControlFrame()
    {
        var bytes = Enumerable.Range(0, RetainedSourceImagePage.PageBytes + 9).Select(index => (byte)(index % 251)).ToArray();
        bytes[0] = 0xff; bytes[1] = 0xd8; bytes[2] = 0xff;
        return new(1, "source-0001.jpg", 1000.25, TraversalArchive.Hash(bytes), bytes);
    }
    private sealed class Project : IPortalProjectClient
    {
        public PortalProjectStub Base { get; } = new();
        public Guid? Worker { get; set; }
        public int WorkerReads { get; private set; }
        public Project(Files files)
        {
            Worker = files.Worker; Base.Record = Base.Record with { State = "blocked" };
            Base.Job = new(Job.OrganizationId, Job.ScanId, Job.JobId, "blocked", 1, null, null, "WORKER_INPUT_OUTCOME_UNCONFIRMED")
            { RecordObservation = true, TraversalRoute = 1 };
        }
        public Task<PortalIdentity> AuthenticateAsync(string bearer, CancellationToken token) => Base.AuthenticateAsync(bearer, token);
        public Task<PortalWorkspaceBootstrap> EnsureWorkspaceAsync(PortalIdentity actor, CancellationToken token) => Base.EnsureWorkspaceAsync(actor, token);
        public Task<PortalWorkspaceResponse?> WorkspaceAsync(PortalIdentity actor, Guid organization, CancellationToken token) =>
            Base.Revoked ? Task.FromException<PortalWorkspaceResponse?>(new PortalFault(401, "CONTROL_AUTHENTICATION_REQUIRED")) : Base.WorkspaceAsync(actor, organization, token);
        public Task<PortalScanResponse[]> ScansAsync(PortalIdentity actor, Guid organization, CancellationToken token) => Base.ScansAsync(actor, organization, token);
        public Task<PortalScanCreatedResponse> CreateScanAsync(PortalIdentity actor, Guid organization, Guid request, CancellationToken token) => Base.CreateScanAsync(actor, organization, request, token);
        public Task<PortalScanResponse?> ScanAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token) => Base.ScanAsync(actor, organization, scan, token);
        public Task<PortalJobResult?> JobAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token) => Base.JobAsync(actor, organization, scan, token);
        public Task<Guid?> AttemptWorkerAsync(PortalIdentity actor, FixtureJob job, CancellationToken token)
        { WorkerReads++; Assert.Equal(PortalProjectClientTests.Bearer, actor.Token); return Task.FromResult(Worker); }
    }
    private sealed class Reader(Files files) : IPortalSourceImagesReader
    {
        private readonly PortalSourceImagesReader _actual = new(files.Root);
        public Action? DuringManifest { get; set; }
        public Action? DuringImage { get; set; }
        public Action? AfterImage { get; set; }
        public Exception? Failure { get; set; }
        public Func<RecordedSourceSnapshot?, RecordedSourceSnapshot?>? Change { get; set; }
        public Func<byte[], byte[]>? ChangeBytes { get; set; }
        public int Calls { get; private set; }
        public int ImageCalls { get; private set; }
        public async Task<RecordedSourceSnapshot?> ReadAsync(FixtureJob job, Guid worker, CancellationToken token)
        {
            Calls++; DuringManifest?.Invoke();
            if (DuringImage is null && Failure is not null) throw Failure;
            var value = await _actual.ReadAsync(job, worker, token);
            return Change is null ? value : Change(value);
        }
        public async Task<byte[]> ReadImageAsync(FixtureJob job, Guid worker, string prefix, RetainedSourceImageFile image, CancellationToken token)
        {
            ImageCalls++; DuringImage?.Invoke();
            if (Failure is not null) throw Failure;
            var value = await _actual.ReadImageAsync(job, worker, prefix, image, token);
            AfterImage?.Invoke(); return ChangeBytes is null ? value : ChangeBytes(value);
        }
    }
    private static PortalIdentity Actor => new(PortalProjectClientTests.Actor, "reader@example.invalid", PortalProjectClientTests.Bearer);
    private static string PathFor => $"/api/workspaces/{Job.OrganizationId:D}/scans/{Job.ScanId:D}/source-images";
    private static PortalProgressService Service(Files files, Project project, IPortalSourceImagesReader? reader = null) =>
        new(project, new PortalProgressReader(files.Root), sourceImagesReader: reader ?? new PortalSourceImagesReader(files.Root));
    private static PortalSourceImageRequest Request(RetainedSourceImages value, int offset = 0) =>
        new(value.PrefixSha256, value.Files[0].SourceSequence, value.Files[0].Sha256, offset);

    [Theory]
    [InlineData("missing", "NotRetained")]
    [InlineData("empty", "Empty")]
    [InlineData("uncommitted", "Empty")]
    [InlineData("available", "Available")]
    public async Task MissingEmptyAndCommittedImagesStayDistinctWithoutInventingARecording(string mode, string state)
    {
        using var f = new Files();
        if (mode == "available") await f.Stage();
        else if (mode != "missing") await f.Begin();
        if (mode == "uncommitted") File.WriteAllBytes(Path.Combine(f.Scope, "uncommitted.pending"), [1]);
        var result = await Service(f, new(f)).SourceManifestAsync(Actor, Job.OrganizationId, Job.ScanId, default);
        Assert.Equal(state, result.SourceState); Assert.Equal("blocked", result.Scan.State); Assert.Equal(f.Worker, result.WorkerId);
        if (mode == "missing") Assert.Null(result.Sources);
        else
        {
            Assert.Equal(mode == "available" ? 1 : 0, result.Sources!.Files.Length);
            Assert.Equal(mode == "uncommitted", result.Sources.HasUncommittedFiles);
            Assert.False(result.Sources.IsCompletedRecording); Assert.False(result.Sources.CanReplay);
            Assert.Equal("NotAssessed", result.Sources.Completeness); Assert.Equal("NotAssessed", result.Sources.GoalVerification);
        }
        if (mode != "available")
            Assert.Equal(404, (await Assert.ThrowsAsync<PortalFault>(() => Service(f, new(f)).SourceImageAsync(Actor, Job.OrganizationId,
                Job.ScanId, new(result.Sources?.PrefixSha256 ?? new string('a', 64), 1, new string('b', 64)), default))).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnrecordedOrUnstartedDoesNotReadStorageOrInventAProducer(bool unstarted)
    {
        using var f = new Files(); var p = new Project(f); var r = new Reader(f) { Failure = new InvalidOperationException("Storage must not be read.") };
        p.Base.Job = p.Base.Job with
        {
            Generation = unstarted ? 0 : 1,
            State = unstarted ? "queued" : "blocked",
            RecordObservation = unstarted,
            TraversalRoute = unstarted ? 1 : 0
        };
        p.Base.Record = p.Base.Record with { State = p.Base.Job.State };
        var result = await Service(f, p, r).SourceManifestAsync(Actor, Job.OrganizationId, Job.ScanId, default);
        Assert.Equal(unstarted ? "NotStarted" : "NotApplicable", result.SourceState); Assert.Null(result.Sources); Assert.Null(result.WorkerId);
        Assert.Equal(0, r.Calls); Assert.Equal(0, p.WorkerReads);
    }

    [Fact]
    public async Task ExactImagePagesReconstructTheOriginalThenRequireAnEmptyAuthorizedEofRead()
    {
        using var f = new Files(); await f.Stage(); var project = new Project(f); var service = Service(f, project);
        var manifest = (await service.SourceManifestAsync(Actor, Job.OrganizationId, Job.ScanId, default)).Sources!;
        using var joined = new MemoryStream(); var offset = 0; var reads = 0;
        while (true)
        {
            var page = await service.SourceImageAsync(Actor, Job.OrganizationId, Job.ScanId, Request(manifest, offset), default); reads++;
            Assert.Equal(offset, page.Offset); Assert.Equal(manifest.PrefixSha256, page.Source.PrefixSha256);
            Assert.Equal(f.Worker, page.Source.WorkerId); Assert.Equal(Job, page.Source.Job);
            Assert.Equal(TraversalArchive.Hash(page.Data), page.PageSha256); Assert.InRange(page.Data.Length, 1, RetainedSourceImagePage.PageBytes);
            joined.Write(page.Data); if (page.NextOffset is not { } next) break; offset = next;
        }
        Assert.Equal(2, reads); Assert.Equal(ControlFrame().Content, joined.ToArray());
        Assert.Equal(manifest.Files[0].Sha256, TraversalArchive.Hash(joined.ToArray()));
        var eof = await service.SourceImageAsync(Actor, Job.OrganizationId, Job.ScanId, Request(manifest, checked((int)joined.Length)), default);
        Assert.Empty(eof.Data); Assert.Null(eof.NextOffset); Assert.Equal(TraversalArchive.Hash([]), eof.PageSha256);
        Assert.Equal(8, project.WorkerReads); Assert.False(eof.Source.IsCompletedRecording); Assert.False(eof.Source.CanReplay);
    }

    [Theory]
    [InlineData("prefix")]
    [InlineData("hash")]
    [InlineData("sequence")]
    [InlineData("negative-offset")]
    [InlineData("unaligned-offset")]
    [InlineData("past-eof")]
    public async Task ByteIdentityAndOffsetGuardsCannotReadAnotherOriginal(string mode)
    {
        using var f = new Files(); await f.Stage(); var request = Request(f.Manifest()); var r = new Reader(f);
        request = mode switch
        {
            "prefix" => request with { PrefixSha256 = new string('a', 64) },
            "hash" => request with { ImageSha256 = new string('b', 64) },
            "sequence" => request with { SourceSequence = 2 },
            "negative-offset" => request with { Offset = -1 },
            "unaligned-offset" => request with { Offset = 1 },
            _ => request with { Offset = f.Manifest().Files[0].Bytes + 1 }
        };
        var error = await Assert.ThrowsAsync<PortalFault>(() => Service(f, new(f), r).SourceImageAsync(Actor, Job.OrganizationId, Job.ScanId, request, default));
        Assert.Equal(mode is "prefix" or "hash" ? 409 : mode == "sequence" ? 404 : 400, error.Status);
        Assert.Equal(0, r.ImageCalls);
    }

    [Theory]
    [InlineData(false, "membership")]
    [InlineData(true, "membership")]
    [InlineData(false, "producer")]
    [InlineData(true, "producer")]
    [InlineData(false, "generation")]
    [InlineData(true, "generation")]
    [InlineData(false, "io")]
    [InlineData(true, "io")]
    [InlineData(false, "unexpected")]
    [InlineData(true, "unexpected")]
    public async Task EveryReadRechecksAuthorityAndAttemptIncludingStorageFailures(bool imageRead, string failure)
    {
        using var f = new Files(); await f.Stage(); var p = new Project(f); var r = new Reader(f); var request = Request(f.Manifest());
        void Change()
        {
            if (failure == "membership" || failure is "io" or "unexpected") p.Base.Revoked = true;
            if (failure == "producer") p.Worker = Guid.NewGuid();
            if (failure == "generation") p.Base.Job = p.Base.Job with { Generation = 2 };
        }
        if (imageRead) r.DuringImage = Change; else r.DuringManifest = Change;
        r.Failure = failure == "io" ? new IOException("private source payload") : failure == "unexpected" ? new InvalidOperationException("private source payload") : null;
        var error = await Assert.ThrowsAsync<PortalFault>(async () =>
        {
            if (imageRead) await Service(f, p, r).SourceImageAsync(Actor, Job.OrganizationId, Job.ScanId, request, default);
            else await Service(f, p, r).SourceManifestAsync(Actor, Job.OrganizationId, Job.ScanId, default);
        });
        Assert.Equal(failure is "producer" or "generation" ? "CONTROL_SOURCE_IMAGES_ATTEMPT_CHANGED" : "CONTROL_AUTHENTICATION_REQUIRED", error.Code);
        Assert.DoesNotContain("private source payload", error.Code); Assert.Equal(1, r.Calls); Assert.Equal(imageRead ? 1 : 0, r.ImageCalls);
    }

    [Theory]
    [InlineData("worker")]
    [InlineData("scan")]
    [InlineData("native")]
    [InlineData("schema")]
    [InlineData("bytes")]
    public async Task AProviderCannotSubstitutePrivateHeaderScopeOrInventByteTotals(string failure)
    {
        using var f = new Files(); await f.Stage(); var r = new Reader(f)
        {
            Change = value => failure switch
            {
                "worker" => value! with { Header = value.Header with { WorkerId = Guid.NewGuid() } },
                "scan" => value! with { Header = value.Header with { Job = Job with { ScanId = Guid.NewGuid() } } },
                "native" => value! with { Header = value.Header with { Native = value.Header.Native with { Owner = Guid.Empty } } },
                "schema" => value! with { Header = value.Header with { Schema = "unrelated" } },
                _ => value! with { Bytes = value.Bytes + 1 }
            }
        };
        var error = await Assert.ThrowsAsync<PortalFault>(() => Service(f, new(f), r).SourceManifestAsync(Actor, Job.OrganizationId, Job.ScanId, default));
        Assert.Equal("CONTROL_SOURCE_IMAGES_INVALID", error.Code); Assert.Equal(0, r.ImageCalls);
    }

    [Fact]
    public async Task SourceAppendInvalidatesPriorPrefixWithoutChangingOriginalImagesOrAuthorizingReplay()
    {
        using var f = new Files(); await f.Stage(); var before = f.Manifest(); var image = before.Files[0];
        var original = File.ReadAllBytes(Path.Combine(f.Scope, image.File)); var frame = ControlFrame() with { SourceSequence = 2, File = "source-0002.jpg" };
        var batch = new FixtureSourceBatch(FixtureSourceProtocol.Schema, f.Journal.Read().Header.Native.Owner, 2, [frame]);
        await f.Journal.AppendAsync(batch, TraversalArchive.Hash(FixtureProgressProtocol.Encode(batch)), default);
        var after = f.Manifest(); Assert.NotEqual(before.PrefixSha256, after.PrefixSha256); Assert.Equal(2, after.Files.Length);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(f.Scope, image.File))); Assert.False(after.CanReplay);
        Assert.Equal("CONTROL_SOURCE_IMAGES_CHANGED", (await Assert.ThrowsAsync<PortalFault>(() =>
            Service(f, new(f)).SourceImageAsync(Actor, Job.OrganizationId, Job.ScanId, Request(before), default))).Code);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("producer")]
    [InlineData("prefix")]
    [InlineData("unrecorded")]
    [InlineData("missing-files")]
    [InlineData("duplicate")]
    [InlineData("bytes")]
    [InlineData("completed")]
    [InlineData("replay")]
    [InlineData("goal")]
    [InlineData("completeness")]
    [InlineData("scope")]
    public async Task DomainSubsetCannotBecomeAFinalRecordingOrAnUnboundManifest(string change)
    {
        using var f = new Files(); await f.Stage(); var value = f.Manifest();
        value = change switch
        {
            "schema" => value with { Schema = "other" },
            "producer" => value with { WorkerId = Guid.Empty },
            "prefix" => value with { PrefixSha256 = new string('A', 64) },
            "unrecorded" => value with { Job = Job with { RecordObservation = false, TraversalRoute = 0 } },
            "missing-files" => value with { Files = default },
            "duplicate" => value with { Files = [value.Files[0], value.Files[0]], Bytes = value.Bytes * 2 },
            "bytes" => value with { Bytes = value.Bytes + 1 },
            "completed" => value with { IsCompletedRecording = true },
            "replay" => value with { CanReplay = true },
            "goal" => value with { GoalVerification = "Satisfied" },
            "completeness" => value with { Completeness = "Complete" },
            _ => value with { Scope = "OriginalVideo" }
        };
        Assert.Equal("WORKER_SOURCE_IMAGES_INVALID", Assert.Throws<BrowserOperationException>(value.Validate).Code);
    }

    [Theory]
    [InlineData("zero-sequence")]
    [InlineData("above-sequence")]
    [InlineData("path")]
    [InlineData("nan-time")]
    [InlineData("zero-time")]
    [InlineData("negative-time")]
    [InlineData("short-bytes")]
    [InlineData("large-bytes")]
    [InlineData("hash")]
    [InlineData("content-type")]
    public async Task ImageMetadataHasAnExactBoundedOriginalIdentity(string change)
    {
        using var f = new Files(); await f.Stage(); var image = f.Manifest().Files[0];
        image = change switch
        {
            "zero-sequence" => image with { SourceSequence = 0 },
            "above-sequence" => image with { SourceSequence = 251 },
            "path" => image with { File = "../source-0001.jpg" },
            "nan-time" => image with { PresentationUnixMilliseconds = double.NaN },
            "zero-time" => image with { PresentationUnixMilliseconds = 0 },
            "negative-time" => image with { PresentationUnixMilliseconds = -1 },
            "short-bytes" => image with { Bytes = 3 },
            "large-bytes" => image with { Bytes = RetainedSourceImageFile.MaximumBytes + 1 },
            "hash" => image with { Sha256 = new string('A', 64) },
            _ => image with { ContentType = "image/svg+xml" }
        };
        Assert.Equal("WORKER_SOURCE_IMAGES_INVALID", Assert.Throws<BrowserOperationException>(image.Validate).Code);
    }

    [Fact]
    public async Task ChangedOriginalBytesNeverReachAnAuthorizedImagePage()
    {
        using var f = new Files(); await f.Stage(); var manifest = f.Manifest(); var request = Request(manifest);
        var bytes = ControlFrame().Content; bytes[^1] ^= 1;
        File.WriteAllBytes(Path.Combine(f.Scope, manifest.Files[0].File), bytes);
        var fault = await Assert.ThrowsAsync<PortalFault>(() => Service(f, new(f)).SourceImageAsync(Actor, Job.OrganizationId, Job.ScanId, request, default));
        Assert.Equal("CONTROL_SOURCE_IMAGES_INVALID", fault.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MembershipLossAfterActualFileReadCannotReleaseImageBytesOrEof(bool eof)
    {
        using var f = new Files(); await f.Stage(); var p = new Project(f); var r = new Reader(f) { AfterImage = () => p.Base.Revoked = true };
        var manifest = f.Manifest();
        var error = await Assert.ThrowsAsync<PortalFault>(() => Service(f, p, r).SourceImageAsync(Actor, Job.OrganizationId, Job.ScanId,
            Request(manifest, eof ? manifest.Files[0].Bytes : 0), default));
        Assert.Equal("CONTROL_AUTHENTICATION_REQUIRED", error.Code); Assert.Equal(1, r.ImageCalls);
    }

    [Fact]
    public async Task CancellationAfterActualFileReadCannotReleaseAnImagePage()
    {
        using var f = new Files(); await f.Stage(); using var stop = new CancellationTokenSource();
        var r = new Reader(f) { AfterImage = stop.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(f, new(f), r).SourceImageAsync(Actor,
            Job.OrganizationId, Job.ScanId, Request(f.Manifest()), stop.Token));
        Assert.Equal(1, r.ImageCalls); Assert.True(stop.IsCancellationRequested);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AReadProviderCannotSubstituteBytesAfterTheOriginalFileWasVerified(bool shorten)
    {
        using var f = new Files(); await f.Stage(); var r = new Reader(f)
        {
            ChangeBytes = value =>
            {
                if (shorten) return value[..^1];
                value[^1] ^= 1; return value;
            }
        };
        var error = await Assert.ThrowsAsync<PortalFault>(() => Service(f, new(f), r).SourceImageAsync(Actor, Job.OrganizationId,
            Job.ScanId, Request(f.Manifest()), default));
        Assert.Equal("CONTROL_SOURCE_IMAGES_INVALID", error.Code); Assert.Equal(1, r.ImageCalls);
    }

    [Fact]
    public void PublishedSourceImageSchemasPreserveBoundsRequiredIdentityAndSubsetMeaning()
    {
        using var json = JsonDocument.Parse(PortalOpenApi.Document); var schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
        var response = schemas.GetProperty("RetainedSourceImages").GetProperty("properties");
        Assert.Equal(new[] { "NotApplicable", "NotStarted", "NotRetained", "Empty", "Available" },
            response.GetProperty("sourceState").GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
        var source = response.GetProperty("sources").GetProperty("properties");
        Assert.Equal("RetainedOriginalSourceSubset", source.GetProperty("scope").GetProperty("const").GetString());
        Assert.Equal("NotAssessed", source.GetProperty("completeness").GetProperty("const").GetString());
        Assert.False(source.GetProperty("isCompletedRecording").GetProperty("const").GetBoolean()); Assert.False(source.GetProperty("canReplay").GetProperty("const").GetBoolean());
        Assert.Equal(250, source.GetProperty("files").GetProperty("maxItems").GetInt32());
        Assert.Equal(25 * 1024 * 1024, source.GetProperty("bytes").GetProperty("maximum").GetInt32()); Assert.False(source.TryGetProperty("native", out _));
        var request = schemas.GetProperty("RetainedSourceImageRequest");
        Assert.Equal(new[] { "prefixSha256", "sourceSequence", "imageSha256" }, request.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(512 * 1024, request.GetProperty("properties").GetProperty("offset").GetProperty("maximum").GetInt32());
        var page = schemas.GetProperty("RetainedSourceImagePage").GetProperty("properties");
        Assert.Equal(87384, page.GetProperty("data").GetProperty("maxLength").GetInt32());
        Assert.False(page.GetProperty("source").GetProperty("properties").TryGetProperty("files", out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceHttpAuthenticationAndClosedBodyBoundaryProtectBothRoutes(bool byteRead)
    {
        using var f = new Files(); await f.Stage(); var project = new Project(f);
        await using var server = await HostedControlServer.StartAsync(new(PortalProjectClientTests.Key, f.Root, new string('a', 40), 0), project, new PortalObservationStub());
        using var anonymous = new HttpClient { BaseAddress = server.BaseAddress };
        using var denied = byteRead ? await anonymous.PostAsJsonAsync(PathFor + "/bytes", Request(f.Manifest()), PortalJson.Options) : await anonymous.GetAsync(PathFor);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode); Assert.True(denied.Headers.CacheControl!.NoStore);
        using var authorized = PortalHttpTests.Authorized(server);
        using var foreign = await authorized.GetAsync(PathFor.Replace(Job.OrganizationId.ToString("D"), Guid.NewGuid().ToString("D"), StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        var body = JsonSerializer.SerializeToNode(Request(f.Manifest()), PortalJson.Options)!; body["workerId"] = f.Worker;
        using var extra = await authorized.PostAsync(PathFor + "/bytes", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, extra.StatusCode); Assert.True(extra.Headers.CacheControl!.NoStore);
        Assert.Equal(0, project.WorkerReads);
    }

    [Fact]
    public async Task ActualInterruptedChromiumImagesSurviveProtectedHttpAndPrivateMcpReconnect()
    {
        using var f = new Files(); await f.Stage(actual: true); var project = new Project(f);
        await using var server = await HostedControlServer.StartAsync(new(PortalProjectClientTests.Key, f.Root, new string('a', 40), 0), project, new PortalObservationStub());
        using var http = PortalHttpTests.Authorized(server);
        using var response = await http.GetAsync(PathFor); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
        var manifest = (await response.Content.ReadFromJsonAsync<PortalSourceImagesResponse>(PortalJson.Options))!;
        Assert.Equal("blocked", manifest.Scan.State); Assert.Equal("Available", manifest.SourceState); Assert.Equal(3, manifest.Sources!.Files.Length);
        var payload = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("native", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sha256:" + new string('a', 64), payload); Assert.DoesNotContain(f.Journal.Read().Header.Native.Owner.ToString("D"), payload);
        for (var connection = 0; connection < 2; connection++)
        {
            await using var mcp = await Connect(server.BaseAddress);
            var listed = await mcp.CallToolAsync("scan_source_manifest", new Dictionary<string, object?> { ["scanId"] = Job.ScanId.ToString("D") });
            Assert.False(listed.IsError ?? false); var source = listed.StructuredContent!.Value.GetProperty("data").GetProperty("sources");
            Assert.Equal(manifest.Sources.PrefixSha256, source.GetProperty("prefixSha256").GetString()); Assert.False(source.GetProperty("canReplay").GetBoolean());
            var image = manifest.Sources.Files[0];
            var reply = await mcp.CallToolAsync("scan_source_image", new Dictionary<string, object?>
            { ["scanId"] = Job.ScanId.ToString("D"), ["prefixSha256"] = manifest.Sources.PrefixSha256, ["sourceSequence"] = image.SourceSequence, ["imageSha256"] = image.Sha256 });
            Assert.False(reply.IsError ?? false);
            var page = reply.StructuredContent!.Value.GetProperty("data").Deserialize<RetainedSourceImagePage>(PortalJson.Options)!;
            Assert.Equal(File.ReadAllBytes(Path.Combine(f.Scope, image.File)), page.Data); Assert.Equal(image.Sha256, page.PageSha256);
            Assert.Equal(image.PresentationUnixMilliseconds, page.Source.Image.PresentationUnixMilliseconds);
            Assert.False(page.Source.IsCompletedRecording); Assert.Equal("NotAssessed", page.Source.GoalVerification);
            using var eof = await http.PostAsJsonAsync(PathFor + "/bytes", Request(manifest.Sources, image.Bytes), PortalJson.Options);
            Assert.Equal(HttpStatusCode.OK, eof.StatusCode); Assert.Empty((await eof.Content.ReadFromJsonAsync<RetainedSourceImagePage>(PortalJson.Options))!.Data);
        }
        Assert.Equal("blocked", project.Base.Job.State); Assert.Null(project.Base.Job.WorkerArchive);
    }

    private static Task<McpClient> Connect(Uri origin)
    {
        var assembly = typeof(SourceImageReadsTests).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>().Single(value => value.Key == "McpServerAssembly").Value!;
        return McpClient.CreateAsync(new StdioClientTransport(new()
        {
            Command = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            Arguments = [assembly, "--hosted"],
            WorkingDirectory = Path.GetDirectoryName(assembly),
            Name = "original source image integration",
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["HSR_MCP_API_ORIGIN"] = origin.ToString(),
                ["HSR_MCP_ORGANIZATION"] = Job.OrganizationId.ToString("D"),
                ["HSR_MCP_ACCESS_TOKEN"] = PortalProjectClientTests.Bearer
            }
        }));
    }
}
