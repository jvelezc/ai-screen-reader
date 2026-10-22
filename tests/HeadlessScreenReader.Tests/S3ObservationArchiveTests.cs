using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class S3ObservationArchiveTests
{
    private const string Access = "s3-test-access-synthetic";
    private const string Secret = "s3-test-secret-never-log-this-canary";
    private static byte[] Bytes(FixtureJob job) => JsonSerializer.SerializeToUtf8Bytes(FixtureWorkerTests.Receipt(job), ObservationStore.Json);
    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static HttpResponseMessage Body(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private sealed record Call(string Method, string Address, string? Authorization, string? PayloadHash, string? Date, string? ContentType, byte[] Bytes);
    private sealed class Transport(Func<int, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<Call> Calls { get; } = [];
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Pending { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var payload = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(token);
            Calls.Add(new(request.Method.Method, request.RequestUri!.AbsoluteUri,
                request.Headers.TryGetValues("Authorization", out var authorization) ? authorization.Single() : null,
                request.Headers.TryGetValues("x-amz-content-sha256", out var hash) ? hash.Single() : null,
                request.Headers.TryGetValues("x-amz-date", out var date) ? date.Single() : null,
                request.Content?.Headers.ContentType?.MediaType, payload));
            return Pending is null ? response(Calls.Count) : await Pending(request, token);
        }
    }
    private static S3ObservationArchive Archive(Transport transport) => new(S3EvidenceConfiguration.Optional(Access, Secret)!, transport);

    [Fact]
    public void ConfigurationHasFixedProjectAuthorityAndCannotSerializeCredentials()
    {
        Assert.Null(S3EvidenceConfiguration.Optional(null, null));
        var value = S3EvidenceConfiguration.Optional(Access, Secret)!;
        Assert.Equal("https://gpnijaxrgtwzwtrqsbcu.storage.supabase.co/storage/v1/s3/", value.Endpoint.AbsoluteUri);
        Assert.Equal("hsr-evidence", value.Bucket);
        foreach (var text in new[] { value.ToString(), JsonSerializer.Serialize(value) })
        { Assert.DoesNotContain(Access, text); Assert.DoesNotContain(Secret, text); }
        var owner = Guid.NewGuid(); var proof = S3EvidenceConfiguration.LocalProof(12345, owner);
        Assert.Equal("http://127.0.0.1:12345/", proof.Endpoint.AbsoluteUri);
        Assert.Equal("hsr-proof-" + owner.ToString("N"), proof.Bucket);
    }

    [Theory]
    [InlineData("missing-access")]
    [InlineData("missing-secret")]
    [InlineData("empty")]
    [InlineData("management")]
    [InlineData("service")]
    [InlineData("public")]
    [InlineData("newline")]
    [InlineData("jwt")]
    public void IncompleteOrPrivilegedCredentialsCannotBecomeS3Authority(string scenario)
    {
        var access = scenario switch
        {
            "missing-access" => null,
            "empty" => "",
            "management" => "sbp_" + new string('a', 40),
            "public" => "sb_publishable_" + new string('a', 40),
            "newline" => Access + "\n",
            _ => Access
        };
        var secret = scenario switch
        {
            "missing-secret" => null,
            "service" => "sb_secret_" + new string('a', 40),
            "jwt" => "eyJhbGciOiJIUzI1NiJ9.eyJyb2xlIjoic2VydmljZV9yb2xlIn0.c2lnbmF0dXJl",
            _ => Secret
        };
        var error = Assert.Throws<ArgumentException>(() => S3EvidenceConfiguration.Optional(access, secret));
        Assert.Equal("EVIDENCE_S3_CONFIGURATION_INVALID", error.Message);
    }

    [Fact]
    public void ContentAddressIncludesEveryScopeAndDigestWithoutAnArbitraryPath()
    {
        var job = FixtureWorkerTests.Job(); var hash = new string('a', 64);
        Assert.Equal($"evidence-v1/{job.OrganizationId:N}/{job.ScanId:N}/{job.JobId:N}/{job.Generation}/{hash}/observation.json",
            S3ObservationArchive.ObjectKey(job, hash));
        Assert.NotEqual(S3ObservationArchive.ObjectKey(job, hash), S3ObservationArchive.ObjectKey(job with { Generation = 2 }, hash));
        Assert.NotEqual(S3ObservationArchive.ObjectKey(job, hash), S3ObservationArchive.ObjectKey(job, new string('b', 64)));
        Assert.Throws<ArgumentException>(() => S3ObservationArchive.ObjectKey(job, "../private"));
        Assert.Throws<ArgumentException>(() => S3ObservationArchive.ObjectKey(job, new string('A', 64)));
        Assert.Throws<BrowserOperationException>(() => S3ObservationArchive.ObjectKey(job with { OrganizationId = Guid.Empty }, hash));
        Assert.Throws<ArgumentException>(() => S3EvidenceConfiguration.LocalProof(0, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => S3EvidenceConfiguration.LocalProof(12345, Guid.Empty));
    }

    [Fact]
    public async Task MissingObjectRequiresSignedUploadAndActualExactReadback()
    {
        var job = FixtureWorkerTests.Job(); var bytes = Bytes(job); var hash = Digest(bytes);
        var transport = new Transport(index => index switch { 1 => new(HttpStatusCode.NotFound), 2 => new(HttpStatusCode.OK), _ => Body(bytes) });
        using var archive = Archive(transport);
        await archive.FinalizeAsync(job, bytes, hash, CancellationToken.None);
        Assert.Equal(new[] { "GET", "PUT", "GET" }, transport.Calls.Select(call => call.Method));
        Assert.All(transport.Calls, call =>
        {
            Assert.Equal("https://gpnijaxrgtwzwtrqsbcu.storage.supabase.co/storage/v1/s3/hsr-evidence/" + S3ObservationArchive.ObjectKey(job, hash), call.Address);
            Assert.StartsWith("AWS4-HMAC-SHA256 ", call.Authorization);
            Assert.Contains("/us-east-1/s3/aws4_request", call.Authorization);
            Assert.Matches("^[0-9]{8}T[0-9]{6}Z$", call.Date);
            Assert.DoesNotContain(Secret, call.Authorization); Assert.DoesNotContain(Access, call.Address);
        });
        Assert.Equal(bytes, transport.Calls[1].Bytes); Assert.Equal(hash, transport.Calls[1].PayloadHash);
        Assert.Equal("application/json", transport.Calls[1].ContentType);
    }

    [Fact]
    public async Task ExistingVerifiedObjectIsReadWithoutAnyRewrite()
    {
        var job = FixtureWorkerTests.Job(); var bytes = Bytes(job); var transport = new Transport(_ => Body(bytes));
        using var archive = Archive(transport);
        await archive.FinalizeAsync(job, bytes, Digest(bytes), CancellationToken.None);
        Assert.Equal("GET", Assert.Single(transport.Calls).Method);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("scope")]
    [InlineData("shutdown")]
    public async Task ConflictingExistingBytesNeverAuthorizeAnOverwrite(string scenario)
    {
        var job = FixtureWorkerTests.Job(); var bytes = Bytes(job);
        var existing = scenario switch
        {
            "scope" => Bytes(job with { Generation = 2 }),
            "shutdown" => JsonSerializer.SerializeToUtf8Bytes(FixtureWorkerTests.Receipt(job) with
            { Shutdown = FixtureWorkerTests.Receipt(job).Shutdown with { ProcessExitConfirmed = false } }, ObservationStore.Json),
            _ => "private-storage-detail"u8.ToArray()
        };
        var hash = scenario == "hash" ? Digest(bytes) : Digest(existing);
        var transport = new Transport(_ => Body(existing)); using var archive = Archive(transport);
        var error = await Assert.ThrowsAsync<ObservationArchiveException>(() => archive.ReadAsync(job, hash, CancellationToken.None));
        Assert.Equal(ObservationArchiveFailure.Invalid, error.Failure);
        Assert.Equal("GET", Assert.Single(transport.Calls).Method);
        Assert.DoesNotContain("private-storage-detail", error.ToString());
        if (scenario == "hash")
        {
            transport.Calls.Clear();
            await Assert.ThrowsAsync<ObservationArchiveException>(() => archive.FinalizeAsync(job, bytes, hash, CancellationToken.None));
            Assert.Equal("GET", Assert.Single(transport.Calls).Method);
        }
    }

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(307)]
    [InlineData(500)]
    public async Task FailedUploadNeverClaimsReadbackOrRetriesTheWrite(int status)
    {
        var job = FixtureWorkerTests.Job(); var bytes = Bytes(job);
        var transport = new Transport(index => new(index == 1 ? HttpStatusCode.NotFound : (HttpStatusCode)status));
        using var archive = Archive(transport);
        var error = await Assert.ThrowsAsync<ObservationArchiveException>(() => archive.FinalizeAsync(job, bytes, Digest(bytes), CancellationToken.None));
        Assert.Equal(ObservationArchiveFailure.WriteUnconfirmed, error.Failure);
        Assert.Equal(new[] { "GET", "PUT" }, transport.Calls.Select(call => call.Method));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulPutHeaderCannotSubstituteForMissingOrCorruptReadback(bool corrupt)
    {
        var job = FixtureWorkerTests.Job(); var bytes = Bytes(job);
        var transport = new Transport(index => index switch
        { 1 => new(HttpStatusCode.NotFound), 2 => new(HttpStatusCode.OK), _ => corrupt ? Body("corrupt"u8.ToArray()) : new(HttpStatusCode.NotFound) });
        using var archive = Archive(transport);
        var error = await Assert.ThrowsAsync<ObservationArchiveException>(() => archive.FinalizeAsync(job, bytes, Digest(bytes), CancellationToken.None));
        Assert.Equal(corrupt ? ObservationArchiveFailure.Invalid : ObservationArchiveFailure.WriteUnconfirmed, error.Failure);
        Assert.Equal(3, transport.Calls.Count);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(302)]
    [InlineData(206)]
    public async Task DenialOutageRedirectAndPartialReadRemainUnavailable(int status)
    {
        var transport = new Transport(_ => new((HttpStatusCode)status) { Content = new StringContent(Secret) });
        using var archive = Archive(transport);
        var error = await Assert.ThrowsAsync<ObservationArchiveException>(() => archive.ReadAsync(FixtureWorkerTests.Job(), new string('a', 64), CancellationToken.None));
        Assert.Equal(ObservationArchiveFailure.Unavailable, error.Failure);
        Assert.DoesNotContain(Secret, error.ToString()); Assert.Single(transport.Calls);
    }

    [Fact]
    public async Task ExactMaximumPayloadIsValidatedAndTheNextByteIsRejected()
    {
        var job = FixtureWorkerTests.Job(); var original = Bytes(job);
        var bytes = Enumerable.Repeat((byte)' ', FixtureObservation.MaximumBytes).ToArray(); original.CopyTo(bytes, 0);
        var transport = new Transport(_ => Body(bytes)); using var archive = Archive(transport);
        Assert.Equal(bytes, await archive.ReadAsync(job, Digest(bytes), CancellationToken.None));
        var tooLarge = bytes.Concat(new byte[] { (byte)' ' }).ToArray();
        using var oversized = Archive(new Transport(_ => Body(tooLarge)));
        var error = await Assert.ThrowsAsync<ObservationArchiveException>(() => oversized.ReadAsync(job, Digest(tooLarge), CancellationToken.None));
        Assert.Equal(ObservationArchiveFailure.Invalid, error.Failure);
    }

    private sealed class NonSeekable(byte[] bytes) : MemoryStream(bytes)
    { public override bool CanSeek => false; }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task MissingContentLengthCannotBypassTheEmptyOrOversizedBodyGuard(int extra)
    {
        var bytes = new byte[extra == 0 ? 0 : FixtureObservation.MaximumBytes + 1];
        var content = new StreamContent(new NonSeekable(bytes)); Assert.Null(content.Headers.ContentLength);
        var transport = new Transport(_ => new(HttpStatusCode.OK) { Content = content });
        using var archive = Archive(transport);
        var error = await Assert.ThrowsAsync<ObservationArchiveException>(() => archive.ReadAsync(FixtureWorkerTests.Job(), Digest(bytes), default));
        Assert.Equal(ObservationArchiveFailure.Invalid, error.Failure); Assert.Single(transport.Calls);
    }

    [Fact]
    public async Task IncorrectDigestCannotDispatchAnyStorageRequest()
    {
        var job = FixtureWorkerTests.Job(); var transport = new Transport(_ => throw new InvalidOperationException());
        using var archive = Archive(transport);
        var error = await Assert.ThrowsAsync<ObservationArchiveException>(() => archive.FinalizeAsync(job, Bytes(job), new string('a', 64), default));
        Assert.Equal(ObservationArchiveFailure.Invalid, error.Failure); Assert.Empty(transport.Calls);
        await Assert.ThrowsAsync<ArgumentException>(() => archive.ReadAsync(job, "../private", default));
        Assert.Empty(transport.Calls);
    }

    [Fact]
    public async Task CancellationJoinsTheActualProviderCallbackAndNeverReturnsLateMissingSuccess()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new Transport(_ => throw new InvalidOperationException())
        { Pending = async (_, _) => { entered.SetResult(); await release.Task; return new(HttpStatusCode.NotFound); } };
        using var archive = Archive(transport); using var stop = new CancellationTokenSource();
        var read = archive.ReadAsync(FixtureWorkerTests.Job(), new string('a', 64), stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); stop.Cancel();
        Assert.False(read.IsCompleted); release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.Equal("GET", Assert.Single(transport.Calls).Method);
    }

    [Fact]
    public async Task CallerMutationAfterDispatchCannotAlterTheUploadedEvidence()
    {
        var job = FixtureWorkerTests.Job(); var bytes = Bytes(job); var original = bytes.ToArray(); var hash = Digest(bytes);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
        var transport = new Transport(_ => throw new InvalidOperationException())
        {
            Pending = async (_, _) =>
            {
                if (++calls == 1) { entered.SetResult(); await release.Task; return new(HttpStatusCode.NotFound); }
                return calls == 2 ? new(HttpStatusCode.OK) : Body(original);
            }
        };
        using var archive = Archive(transport);
        var write = archive.FinalizeAsync(job, bytes, hash, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); bytes[0] ^= 1; release.SetResult(); await write;
        Assert.Equal(original, transport.Calls[1].Bytes); Assert.NotEqual(bytes, transport.Calls[1].Bytes);
        Assert.Equal(hash, transport.Calls[1].PayloadHash);
    }
}
